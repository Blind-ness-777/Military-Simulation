using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 계획 명령: 웨이포인트 대기 조건(신호·H시·○소대 접촉/도착), 신호 발신, H시 지정, 본부의 자동 통지.
    /// 작전의 동시성은 플레이어의 반사신경이 아니라 미리 짠 계획으로 맞춘다. 실제 군대가 통신이 불확실해서
    /// H시·암호 신호·통제선을 쓰는 것과 같은 이유다.
    /// - 신호·H시 변경·통지는 모두 명령처럼 통신을 탄다(두절이면 보류, 회복되면 도착).
    /// - H시 도래와 "자기 소대 접촉"은 소대가 스스로 아는 것이라 통신이 필요 없다.
    /// - 대기 중 두절이 길어지면 기존 규칙대로 자체 판단(고수)으로 넘어가지만, 대기 조건은 살아 있다.
    /// </summary>
    public partial class Simulation
    {
        /// <summary>같은 소대의 접촉 통지를 다시 보내기까지의 최소 간격(초). 교전 중 보고마다 통지가 쏟아지지 않게.</summary>
        private const int ContactNoticeIntervalSeconds = 60;

        private readonly Dictionary<Side, int> _hHours = new Dictionary<Side, int>();
        private readonly Dictionary<int, int> _lastContactNoticeSeconds = new Dictionary<int, int>();

        /// <summary>본부가 정한 H시(경과 초). 소대마다 알고 있는 값은 Platoon.KnownHHourSeconds(통신 때문에 다를 수 있다).</summary>
        public bool TryGetHHour(Side side, out int elapsedSeconds)
        {
            return _hHours.TryGetValue(side, out elapsedSeconds);
        }

        /// <summary>H시를 정하거나 바꾼다. 진영의 모든 부대에 통신으로 전달된다(두절된 소대는 예전 H시로 움직인다).</summary>
        public void SetHHour(Side side, int elapsedSeconds)
        {
            if (elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            _hHours[side] = elapsedSeconds;
            string held = BroadcastPlanOrder(side, platoon =>
                new PendingOrder(platoon, OrderKind.HHour, $"{platoon.Name} H시 {FormatClock(elapsedSeconds)}")
                {
                    HHourSeconds = elapsedSeconds,
                    LogOnSubmit = false
                });
            Log(side, $"H시 지정: {FormatClock(elapsedSeconds)}{held}");
        }

        /// <summary>신호를 진영의 모든 부대에 보낸다. 어떤 소대가 움직일지는 각 소대가 기다리는 신호 번호가 정한다.</summary>
        public void SendSignal(Side side, int number)
        {
            if (number < 1 || number > WaitCondition.MaxSignal) throw new ArgumentOutOfRangeException(nameof(number));
            string held = BroadcastPlanOrder(side, platoon =>
                new PendingOrder(platoon, OrderKind.Signal, $"{platoon.Name} 신호 {number}")
                {
                    SignalNumber = number,
                    LogOnSubmit = false
                });
            Log(side, $"신호 {number} 발신{held}");
        }

        /// <summary>
        /// 진영 전체에 같은 명령을 보내고, 두절로 보류된 소대 목록을 로그 꼬리말로 돌려준다.
        /// 통신망을 쓰는 부대는 본부가 실제 상태를 모르므로 전멸 여부를 따지지 않는다(도착 시점에 버려진다).
        /// </summary>
        private string BroadcastPlanOrder(Side side, Func<Platoon, PendingOrder> createOrder)
        {
            List<string> held = null;
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.Side != side)
                {
                    continue;
                }

                bool usesNetwork = UsesCommandNetwork(platoon);
                if (!usesNetwork && platoon.IsDestroyed)
                {
                    continue;
                }

                PendingOrder order = createOrder(platoon);
                SubmitOrder(order);
                if (usesNetwork && order.IsWaitingForComms)
                {
                    (held ??= new List<string>()).Add(platoon.Name);
                }
            }

            return held == null ? string.Empty : $" (두절 — 회복되면 전달: {string.Join(", ", held)})";
        }

        /// <summary>
        /// 본부가 소대의 접촉·도착을 알게 된 순간(보고 도착 시점). 그 사건을 기다리는 소대에게 통지를 보낸다.
        /// 기다리는지는 본부가 내린 계획(현재 경로, 대기 조건, 도착 전 이동 명령)으로 판단한다.
        /// </summary>
        private void OnPlanEventKnown(Platoon subject, PlanEventKind kind)
        {
            if (kind == PlanEventKind.None)
            {
                return;
            }

            if (kind == PlanEventKind.Contact
                && _lastContactNoticeSeconds.TryGetValue(subject.Id, out int last)
                && ElapsedSeconds - last < ContactNoticeIntervalSeconds)
            {
                return;
            }

            bool sent = false;
            foreach (Platoon platoon in _platoons)
            {
                if (platoon == subject || platoon.Side != subject.Side || !ReferencesPlanEvent(platoon, subject, kind))
                {
                    continue;
                }

                string what = kind == PlanEventKind.Contact ? "접촉" : "도착";
                SubmitOrder(new PendingOrder(platoon, OrderKind.PlanNotice, $"{platoon.Name} 통지: {subject.Name} {what}")
                {
                    NoticeSubject = subject,
                    NoticeKind = kind,
                    LogOnSubmit = false,
                    LogOnDelivery = false
                });
                sent = true;
            }

            if (sent && kind == PlanEventKind.Contact)
            {
                _lastContactNoticeSeconds[subject.Id] = ElapsedSeconds;
            }
        }

        private bool ReferencesPlanEvent(Platoon platoon, Platoon subject, PlanEventKind kind)
        {
            if (WaitsFor(platoon.ActiveWait, subject, kind))
            {
                return true;
            }

            foreach (Waypoint waypoint in platoon.Waypoints)
            {
                if (WaitsFor(waypoint.Wait, subject, kind))
                {
                    return true;
                }
            }

            foreach (PendingOrder order in _pendingOrders)
            {
                if (order.Platoon != platoon || order.Kind != OrderKind.Move)
                {
                    continue;
                }

                foreach (Waypoint waypoint in order.Waypoints)
                {
                    if (WaitsFor(waypoint.Wait, subject, kind))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool WaitsFor(WaitCondition wait, Platoon subject, PlanEventKind kind)
        {
            if (wait == null)
            {
                return false;
            }

            return kind == PlanEventKind.Contact ? wait.ContactPlatoon == subject : wait.ArrivalPlatoon == subject;
        }

        /// <summary>대기 중인 소대의 조건을 확인하고, 충족되면 대기를 풀어 남은 경로를 이어 가게 한다.</summary>
        private void UpdateWaits()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (!platoon.IsWaiting || platoon.IsDestroyed || platoon.IsBroken)
                {
                    continue;
                }

                if (!TryRelease(platoon, out string reason))
                {
                    continue;
                }

                FirePolicy? firePolicy = platoon.FirePolicyOnRelease;
                platoon.EndWait();
                string result = platoon.IsMoving ? "출발" : "대기 종료";
                if (firePolicy.HasValue)
                {
                    platoon.FirePolicy = firePolicy.Value;
                    result += $", {DisplayNames.Of(firePolicy.Value)}";
                }

                Report(platoon, $"{platoon.Name} 대기 해제({reason}) — {result}");
            }
        }

        /// <summary>
        /// 대기 조건 중 하나라도 충족됐는지. 대기를 푸는 데 쓴 신호·통지는 소모한다
        /// (다음 대기 지점이 같은 신호를 기다리면 새 신호가 필요하다).
        /// </summary>
        private bool TryRelease(Platoon platoon, out string reason)
        {
            WaitCondition wait = platoon.ActiveWait;
            if (wait.Signal > 0 && platoon.HasSignal(wait.Signal))
            {
                platoon.ConsumeSignal(wait.Signal);
                reason = $"신호 {wait.Signal} 수신";
                return true;
            }

            if (wait.AtHHour && platoon.KnownHHourSeconds.HasValue && ElapsedSeconds >= platoon.KnownHHourSeconds.Value)
            {
                reason = $"H시 {FormatClock(platoon.KnownHHourSeconds.Value)}";
                return true;
            }

            if (wait.ContactPlatoon != null)
            {
                if (wait.ContactPlatoon == platoon)
                {
                    if (IsInContactLocally(platoon))
                    {
                        reason = "적 접촉";
                        return true;
                    }
                }
                else if (platoon.KnowsPlanEvent(wait.ContactPlatoon.Id, PlanEventKind.Contact))
                {
                    platoon.ConsumePlanEvent(wait.ContactPlatoon.Id, PlanEventKind.Contact);
                    reason = $"{wait.ContactPlatoon.Name} 접촉 통보";
                    return true;
                }
            }

            if (wait.ArrivalPlatoon != null)
            {
                if (wait.ArrivalPlatoon == platoon)
                {
                    reason = "도착";
                    return true;
                }

                if (platoon.KnowsPlanEvent(wait.ArrivalPlatoon.Id, PlanEventKind.Arrival))
                {
                    platoon.ConsumePlanEvent(wait.ArrivalPlatoon.Id, PlanEventKind.Arrival);
                    reason = $"{wait.ArrivalPlatoon.Name} 도착 통보";
                    return true;
                }
            }

            reason = null;
            return false;
        }

        /// <summary>소대 자신이 아는 접촉: 추적 중인 적이 있거나, 사격 중이거나, 최근 사격을 받음. 통신과 무관하다.</summary>
        private bool IsInContactLocally(Platoon platoon)
        {
            if (platoon.IsFiring || ElapsedSeconds - platoon.LastFiredUponSeconds <= Rules.ContactMemorySeconds)
            {
                return true;
            }

            foreach (LocalContact local in platoon.KnownEnemies.Values)
            {
                if (local.IsTracking)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>대기 없는 웨이포인트에 사격 방침이 붙어 있으면 도착하는 순간 바꾼다.</summary>
        private void ApplyFirePolicyOnArrival(Platoon platoon, Waypoint reached)
        {
            if (!reached.FirePolicyOnRelease.HasValue || platoon.FirePolicy == reached.FirePolicyOnRelease.Value)
            {
                return;
            }

            platoon.FirePolicy = reached.FirePolicyOnRelease.Value;
            Report(platoon, $"{platoon.Name} 사격 방침 {DisplayNames.Of(platoon.FirePolicy)}");
        }
    }
}
