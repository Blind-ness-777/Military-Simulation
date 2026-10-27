using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 명령 전달, 통신 상태, 연락 두절 부대의 자체 판단.
    /// 본부(PlatoonRole.Headquarters)가 있는 진영만 명령이 통신을 거친다. 본부가 없는 진영은 명령이 즉시 적용된다.
    /// </summary>
    public partial class Simulation
    {
        private readonly List<PendingOrder> _pendingOrders = new List<PendingOrder>();
        private readonly List<JammingZone> _jammingZones = new List<JammingZone>();
        private int _nextCommsCheckSeconds;

        /// <summary>발신했지만 아직 도착하지 않은 명령(발신 순서).</summary>
        public IReadOnlyList<PendingOrder> PendingOrders => _pendingOrders;

        public IReadOnlyList<JammingZone> JammingZones => _jammingZones;

        public void AddJammingZone(JammingZone zone)
        {
            _jammingZones.Add(zone ?? throw new ArgumentNullException(nameof(zone)));
            _nextCommsCheckSeconds = ElapsedSeconds;
        }

        public void RemoveJammingZone(JammingZone zone)
        {
            _jammingZones.Remove(zone);
            _nextCommsCheckSeconds = ElapsedSeconds;
        }

        public bool TryGetHeadquarters(Side side, out Platoon headquarters)
        {
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.Side == side && platoon.IsHeadquarters)
                {
                    headquarters = platoon;
                    return true;
                }
            }

            headquarters = null;
            return false;
        }

        /// <summary>본부가 있던 진영이 본부를 잃었는지. 플레이어 진영이면 관전 모드로 전환하는 기준이다.</summary>
        public bool IsHeadquartersLost(Side side)
        {
            return TryGetHeadquarters(side, out Platoon headquarters) && headquarters.IsDestroyed;
        }

        public void OrderMove(
            Platoon platoon,
            IReadOnlyList<Vec2> points,
            MoveMode moveMode,
            bool append,
            ContactBehavior contactBehavior = ContactBehavior.TakeCover)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            var waypoints = new List<Waypoint>(points.Count);
            foreach (Vec2 point in points)
            {
                waypoints.Add(new Waypoint(Map.ClampInside(point), moveMode, contactBehavior));
            }

            Vec2 destination = waypoints[waypoints.Count - 1].Position;
            string verb = append ? "경로 추가" : "이동";
            string description =
                $"{platoon.Name} {verb} → {Map.GetGridReference(destination)} ({DisplayNames.Of(moveMode)}, 접촉 시 {DisplayNames.Of(contactBehavior)})";

            SubmitOrder(new PendingOrder(platoon, OrderKind.Move, description) { Waypoints = waypoints, Append = append });
        }

        /// <summary>
        /// 웨이포인트 속성(이동 방식·접촉 시 행동·대기 조건·해제 시 사격 방침)을 지점마다 따로 지정하는 이동 명령(계획 명령).
        /// </summary>
        public void OrderMove(Platoon platoon, IReadOnlyList<Waypoint> waypoints, bool append)
        {
            if (waypoints == null || waypoints.Count == 0)
            {
                return;
            }

            var clamped = new List<Waypoint>(waypoints.Count);
            int waits = 0;
            foreach (Waypoint waypoint in waypoints)
            {
                clamped.Add(waypoint.WithPosition(Map.ClampInside(waypoint.Position)));
                if (waypoint.HasWait)
                {
                    waits++;
                }
            }

            Waypoint first = clamped[0];
            Vec2 destination = clamped[clamped.Count - 1].Position;
            string verb = append ? "경로 추가" : "이동";
            string waitText = waits > 0 ? $", 대기 {waits}곳" : string.Empty;
            string description =
                $"{platoon.Name} {verb} → {Map.GetGridReference(destination)} ({DisplayNames.Of(first.MoveMode)}, 접촉 시 {DisplayNames.Of(first.ContactBehavior)}{waitText})";

            SubmitOrder(new PendingOrder(platoon, OrderKind.Move, description) { Waypoints = clamped, Append = append });
        }

        public void OrderWatch(Platoon platoon, float bearingDegrees, float arcDegrees)
        {
            float bearing = Bearing.Normalize(bearingDegrees);
            string description = $"{platoon.Name} 경계 방위 {bearing:000} (폭 {arcDegrees:0}도)";
            SubmitOrder(new PendingOrder(platoon, OrderKind.Watch, description)
            {
                WatchBearingDegrees = bearing,
                WatchArcDegrees = arcDegrees
            });
        }

        public void OrderHold(Platoon platoon)
        {
            SubmitOrder(new PendingOrder(platoon, OrderKind.Hold, $"{platoon.Name} 정지"));
        }

        public void OrderFirePolicy(Platoon platoon, FirePolicy firePolicy)
        {
            string description = $"{platoon.Name} 사격 방침 {DisplayNames.Of(firePolicy)}";
            SubmitOrder(new PendingOrder(platoon, OrderKind.FirePolicy, description) { FirePolicy = firePolicy });
        }

        /// <summary>
        /// 명령을 발신한다. 본부가 없는 진영이나 본부 자신에게 내리는 명령은 즉시 적용한다.
        /// 그 외에는 같은 부대에 대기 중인 이전 명령을 취소하고, 통신 상태에 따라 도착 시각을 정한다.
        /// 단, 경로 추가(Append) 이동은 이전 대기 명령 뒤에 이어 붙어야 하므로 취소하지 않고, 그보다 먼저 도착하지 않게 한다.
        /// 통신망을 쓰는 명령은 발신 시점에 소대의 실제 상태(전멸·붕괴)를 검사하지 않는다.
        /// 본부는 보고로만 알기 때문이다. 받을 수 없는 명령은 도착 시점에 처리한다(DeliverPendingOrders).
        /// </summary>
        private void SubmitOrder(PendingOrder order)
        {
            Platoon platoon = order.Platoon;
            if (!UsesCommandNetwork(platoon))
            {
                if (!CanTakeOrders(platoon))
                {
                    return;
                }

                ApplyOrder(order);
                if (order.LogOnSubmit)
                {
                    Log(platoon.Side, $"명령: {order.Description}");
                }

                return;
            }

            bool chainsAfterPending = order.Kind == OrderKind.Move && order.Append;
            if (!chainsAfterPending)
            {
                RemoveConflictingPendingOrders(order);
            }

            SetComms(platoon, EvaluateComms(platoon));
            order.IssuedAtSeconds = ElapsedSeconds;
            order.DeliverAtSeconds = GetDeliveryTime(platoon.Comms);
            if (chainsAfterPending && !order.IsWaitingForComms)
            {
                order.DeliverAtSeconds = Math.Max(order.DeliverAtSeconds, GetLastDeliveryTime(platoon));
            }

            // 목록 순서가 곧 적용 순서다. 같은 시각에 도착하면 먼저 발신한 명령이 먼저 적용된다.
            _pendingOrders.Add(order);

            if (!order.LogOnSubmit)
            {
                return;
            }

            string arrival = order.IsWaitingForComms
                ? "통신 두절 — 회복되면 전달"
                : $"도착 예정 {FormatClock(order.DeliverAtSeconds)}";
            Log(platoon.Side, $"명령 발신: {order.Description} ({arrival})");
        }

        private bool UsesCommandNetwork(Platoon platoon)
        {
            return !platoon.IsHeadquarters && TryGetHeadquarters(platoon.Side, out _);
        }

        private bool CanTakeOrders(Platoon platoon)
        {
            if (platoon.IsDestroyed)
            {
                Log(platoon.Side, $"명령 불가: {platoon.Name} 전멸");
                return false;
            }

            if (platoon.IsBroken)
            {
                Log(platoon.Side, $"명령 불가: {platoon.Name} 사기 붕괴");
                return false;
            }

            return true;
        }

        private void ApplyOrder(PendingOrder order)
        {
            Platoon platoon = order.Platoon;
            switch (order.Kind)
            {
                case OrderKind.Move:
                    platoon.SetPath(order.Waypoints, order.Append);
                    break;
                case OrderKind.Watch:
                    platoon.SetWatch(order.WatchBearingDegrees, order.WatchArcDegrees);
                    break;
                case OrderKind.Hold:
                    platoon.Hold();
                    break;
                case OrderKind.FirePolicy:
                    platoon.FirePolicy = order.FirePolicy;
                    break;
                case OrderKind.Signal:
                    platoon.ReceiveSignal(order.SignalNumber);
                    break;
                case OrderKind.HHour:
                    platoon.KnownHHourSeconds = order.HHourSeconds;
                    break;
                case OrderKind.PlanNotice:
                    platoon.LearnPlanEvent(order.NoticeSubject.Id, order.NoticeKind);
                    break;
            }

            // 플레이어 명령을 받으면 자체 판단을 멈추고 지휘를 따른다(신호·H시·통지는 정보라서 제외).
            if (platoon.IsSelfDirected && IsMovementOrder(order.Kind))
            {
                platoon.IsSelfDirected = false;
                platoon.SelfDirectedStage = 0;
            }
        }

        private int GetDeliveryTime(CommsState comms)
        {
            switch (comms)
            {
                case CommsState.Direct:
                    return ElapsedSeconds + Rules.DirectCommandDelaySeconds;
                case CommsState.Normal:
                    return ElapsedSeconds + Rules.NormalCommandDelaySeconds;
                case CommsState.Degraded:
                    return ElapsedSeconds + Rules.DegradedCommandDelaySeconds;
                default:
                    return PendingOrder.WaitingForComms;
            }
        }

        private void RemovePendingOrders(Platoon platoon)
        {
            _pendingOrders.RemoveAll(order => order.Platoon == platoon);
        }

        /// <summary>
        /// 새 명령이 대신하는 대기 명령만 지운다. 이동·경계·정지는 서로 대신하고, 사격 방침은 사격 방침만, H시는 H시만 대신한다.
        /// (사격 방침을 바꿨다고 이동 명령이 취소되면 안 된다.) 신호와 통지는 무엇도 대신하지 않고, 무엇에도 지워지지 않는다.
        /// </summary>
        private void RemoveConflictingPendingOrders(PendingOrder newOrder)
        {
            int group = GetReplaceGroup(newOrder.Kind);
            if (group < 0)
            {
                return;
            }

            _pendingOrders.RemoveAll(pending =>
                pending.Platoon == newOrder.Platoon && GetReplaceGroup(pending.Kind) == group);
        }

        /// <summary>서로 대신하는 명령 묶음. -1이면 대신하지도 지워지지도 않는다.</summary>
        private static int GetReplaceGroup(OrderKind kind)
        {
            switch (kind)
            {
                case OrderKind.Move:
                case OrderKind.Watch:
                case OrderKind.Hold:
                    return 0;
                case OrderKind.FirePolicy:
                    return 1;
                case OrderKind.HHour:
                    return 2;
                default:
                    return -1;
            }
        }

        private static bool IsMovementOrder(OrderKind kind)
        {
            return GetReplaceGroup(kind) == 0;
        }

        /// <summary>이 부대에 대기 중인 명령 중 가장 늦은 도착 시각. 없으면 0.</summary>
        private int GetLastDeliveryTime(Platoon platoon)
        {
            int last = 0;
            foreach (PendingOrder pending in _pendingOrders)
            {
                if (pending.Platoon == platoon && pending.DeliverAtSeconds > last)
                {
                    last = pending.DeliverAtSeconds;
                }
            }

            return last;
        }

        private void DeliverPendingOrders()
        {
            for (int i = 0; i < _pendingOrders.Count; i++)
            {
                PendingOrder order = _pendingOrders[i];
                if (order.IsWaitingForComms || order.DeliverAtSeconds > ElapsedSeconds)
                {
                    continue;
                }

                _pendingOrders.RemoveAt(i);
                i--;

                Platoon platoon = order.Platoon;
                if (platoon.IsDestroyed)
                {
                    // 받을 사람이 없다. 본부는 응답이 없다는 것만 안다.
                    continue;
                }

                if (platoon.IsBroken)
                {
                    Report(platoon, $"명령 불가: {platoon.Name} 사기 붕괴");
                    continue;
                }

                ApplyOrder(order);
                if (order.LogOnDelivery)
                {
                    Report(platoon, $"명령 수신: {order.Description}");
                }
            }
        }

        /// <summary>
        /// 통신 상태를 주기적으로 다시 판정한다. 두절에서 회복된 부대의 보류 명령은 이때 출발한다.
        /// 재밍 구역이 바뀌면 다음 틱에 바로 다시 판정한다.
        /// </summary>
        private void UpdateComms()
        {
            if (ElapsedSeconds < _nextCommsCheckSeconds)
            {
                return;
            }

            _nextCommsCheckSeconds = ElapsedSeconds + Rules.CommsCheckIntervalSeconds;

            foreach (Platoon platoon in _platoons)
            {
                if (platoon.IsDestroyed || !UsesCommandNetwork(platoon))
                {
                    continue;
                }

                SetComms(platoon, EvaluateComms(platoon));
            }
        }

        /// <summary>
        /// 통신 상태 변경은 모두 여기를 거친다. 두절 시작 시각 기록, 보류 명령·보고 출발,
        /// 실시간 보고가 끊기는 순간의 상태 기록, 로그를 한곳에서 처리한다.
        /// </summary>
        private void SetComms(Platoon platoon, CommsState current)
        {
            CommsState previous = platoon.Comms;
            if (current == previous)
            {
                return;
            }

            bool wasLive = IsReportingLive(platoon);
            platoon.Comms = current;
            if (wasLive && !IsReportingLive(platoon))
            {
                OnReportingLiveLost(platoon);
            }

            if (current == CommsState.Cut)
            {
                platoon.CommsCutSinceSeconds = ElapsedSeconds;
            }
            else if (previous == CommsState.Cut)
            {
                ReleaseHeldOrders(platoon);
                ReleaseHeldReports(platoon);
            }

            // 직접 ↔ 정상 전환은 평범한 일이라 로그를 남기지 않는다.
            if (current >= CommsState.Degraded || previous >= CommsState.Degraded)
            {
                Log(platoon.Side, $"{platoon.Name} 통신 {DisplayNames.Of(current)}");
            }
        }

        private void ReleaseHeldOrders(Platoon platoon)
        {
            foreach (PendingOrder order in _pendingOrders)
            {
                if (order.Platoon == platoon && order.IsWaitingForComms)
                {
                    order.DeliverAtSeconds = GetDeliveryTime(platoon.Comms);
                }
            }
        }

        /// <summary>
        /// 본부와 이 부대 사이의 통신 상태.
        /// 순서: 목소리 거리 → 통신 수단 유무 → 위성통신(하늘 시야) 또는 가시선 무전(지형 음영) → 재밍.
        /// </summary>
        internal CommsState EvaluateComms(Platoon platoon)
        {
            if (!TryGetHeadquarters(platoon.Side, out Platoon headquarters))
            {
                return CommsState.Normal;
            }

            if (platoon == headquarters)
            {
                return CommsState.Direct;
            }

            if (headquarters.IsDestroyed)
            {
                return CommsState.Cut;
            }

            float distance = Vec2.Distance(platoon.Position, headquarters.Position);
            if (distance <= Rules.VoiceRange)
            {
                return CommsState.Direct;
            }

            CommsState state = EvaluateLink(headquarters, platoon, distance);
            return ApplyJamming(state, headquarters, platoon);
        }

        private CommsState EvaluateLink(Platoon headquarters, Platoon platoon, float distance)
        {
            CommsEquipment shared = headquarters.CommsEquipment & platoon.CommsEquipment;

            bool satcomAvailable = (shared & CommsEquipment.Satcom) != 0
                && HasSkyView(headquarters)
                && HasSkyView(platoon);
            if (satcomAvailable)
            {
                return CommsState.Normal;
            }

            if ((shared & CommsEquipment.LineOfSightRadio) == 0 || distance > Rules.RadioRange)
            {
                return CommsState.Cut;
            }

            float obstruction = LineOfSight.GetMaxTerrainObstruction(
                Map,
                headquarters.Position,
                headquarters.UnitType.AntennaHeight,
                platoon.Position,
                platoon.UnitType.AntennaHeight);

            if (obstruction <= 0f)
            {
                return CommsState.Normal;
            }

            return obstruction > Rules.RadioShadowCutDepth ? CommsState.Cut : CommsState.Degraded;
        }

        /// <summary>
        /// 위성통신용 하늘 시야. 숲 안이거나, 주변 8방향 지형이 마스크 각도보다 높게 솟아 있으면 막힌 것으로 본다.
        /// </summary>
        private bool HasSkyView(Platoon platoon)
        {
            Vec2 position = platoon.Position;
            if (Map.GetTerrainAt(position) == TerrainType.Forest)
            {
                return false;
            }

            float baseHeight = Map.GetHeightAt(position) + platoon.UnitType.AntennaHeight;
            double maskSlope = Math.Tan(Rules.SatcomMaskAngleDegrees * Math.PI / 180.0);
            float step = Map.CellSize;

            for (int direction = 0; direction < 8; direction++)
            {
                Vec2 unit = Bearing.ToDirection(direction * 45f);
                for (float distance = step; distance <= Rules.SatcomSkyCheckRadius; distance += step)
                {
                    Vec2 sample = Map.ClampInside(position + unit * distance);
                    if ((Map.GetHeightAt(sample) - baseHeight) / distance > maskSlope)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private CommsState ApplyJamming(CommsState state, Platoon headquarters, Platoon platoon)
        {
            foreach (JammingZone zone in _jammingZones)
            {
                if (zone.AffectedSide != platoon.Side)
                {
                    continue;
                }

                if (!zone.Contains(platoon.Position) && !zone.Contains(headquarters.Position))
                {
                    continue;
                }

                if (zone.Strength == JammingStrength.Block)
                {
                    return CommsState.Cut;
                }

                state = state == CommsState.Degraded || state == CommsState.Cut ? CommsState.Cut : CommsState.Degraded;
            }

            return state;
        }

        /// <summary>
        /// 본부 사상자 가운데 통신병이 포함되는지 판정한다. 통신병을 잃으면 본부는 통신 수단을 모두 잃는다.
        /// </summary>
        private void CheckRadioOperatorLoss(Platoon headquarters, int casualties)
        {
            int remaining = headquarters.Effective;
            for (int i = 0; i < casualties && remaining > 0; i++, remaining--)
            {
                double chance = Math.Min(1.0, (double)Rules.HeadquartersRadioOperators / remaining);
                if (_random.NextDouble() < chance)
                {
                    headquarters.CommsEquipment = CommsEquipment.None;
                    _nextCommsCheckSeconds = ElapsedSeconds;
                    Log(headquarters.Side, $"{headquarters.Name} 통신병 손실 — 무전·위성통신 불가");
                    return;
                }
            }
        }

        /// <summary>
        /// 연락 두절 부대의 자체 판단.
        /// 두절이 SelfDirectedDelaySeconds 넘게 이어지고 이동을 마치면 고수로 전환한다.
        /// 피해가 쌓이면 지나온 길을 되짚어 후퇴(1단계), 철수(2단계)한다.
        /// 경로 선택·협동·경계 방향 판단은 하지 않는다.
        /// </summary>
        private void UpdateSelfDirected()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.IsDestroyed || platoon.IsBroken || !UsesCommandNetwork(platoon))
                {
                    continue;
                }

                if (platoon.Comms != CommsState.Cut)
                {
                    if (platoon.IsSelfDirected)
                    {
                        platoon.IsSelfDirected = false;
                        platoon.SelfDirectedStage = 0;
                        Report(platoon, $"{platoon.Name} 연락 회복 — 지휘 복귀");
                    }

                    continue;
                }

                if (!platoon.IsSelfDirected)
                {
                    bool longEnough = ElapsedSeconds - platoon.CommsCutSinceSeconds >= Rules.SelfDirectedDelaySeconds;
                    if (longEnough && !platoon.IsMoving)
                    {
                        platoon.IsSelfDirected = true;
                        platoon.SelfDirectedStage = 0;
                        Report(platoon, $"{platoon.Name} 연락 두절 지속 — 자체 판단 모드(고수)");
                    }

                    continue;
                }

                float strengthRatio = (float)platoon.Effective / platoon.InitialStrength;
                if (platoon.SelfDirectedStage < 2 && strengthRatio <= Rules.SelfDirectedWithdrawStrength)
                {
                    platoon.SelfDirectedStage = 2;
                    StartTrailRetreat(platoon, float.MaxValue);
                    Report(platoon, $"{platoon.Name} 자체 판단: 피해 과다 — 왔던 길로 철수");
                }
                else if (platoon.SelfDirectedStage < 1 && strengthRatio <= Rules.SelfDirectedRetreatStrength)
                {
                    platoon.SelfDirectedStage = 1;
                    StartTrailRetreat(platoon, Rules.SelfDirectedRetreatDistance);
                    Report(platoon, $"{platoon.Name} 자체 판단: 피해 누적 — 왔던 길로 후퇴");
                }
            }
        }

        /// <summary>
        /// 지나온 경로를 거꾸로 최대 maxDistance만큼 되짚는 경로를 주고, 되짚을 구간은 경로 기록에서 잘라낸다.
        /// </summary>
        private void StartTrailRetreat(Platoon platoon, float maxDistance)
        {
            var path = new List<Waypoint>();
            Vec2 previous = platoon.Position;
            float travelled = 0f;
            int keepCount = platoon.Trail.Count;

            for (int i = platoon.Trail.Count - 1; i >= 0 && travelled < maxDistance; i--)
            {
                Vec2 point = platoon.Trail[i];
                float segment = Vec2.Distance(previous, point);
                keepCount = i + 1;
                if (segment < 1f)
                {
                    continue;
                }

                if (travelled + segment > maxDistance)
                {
                    // 기록점 i와 그 앞 사이에서 멈춘다. 기록점 i는 아직 되짚지 않았으므로 남긴다.
                    point = Vec2.Lerp(previous, point, (maxDistance - travelled) / segment);
                    path.Add(new Waypoint(point, MoveMode.Fast, ContactBehavior.PushThrough));
                    platoon.SetPath(path, false);
                    platoon.TruncateTrail(i + 1, point);
                    return;
                }

                path.Add(new Waypoint(point, MoveMode.Fast, ContactBehavior.PushThrough));
                travelled += segment;
                previous = point;
            }

            if (path.Count > 0)
            {
                platoon.SetPath(path, false);
                platoon.TruncateTrail(keepCount, previous);
            }
        }
    }
}
