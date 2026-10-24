using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 소대 → 본부 보고 전달. 본부 상황판(SideIntel)과 플레이어 로그는 보고가 도착한 것만 반영한다.
    /// 정상·직접 통신: 즉시. 저하: DegradedReportDelaySeconds 뒤. 두절: 회복될 때까지 보류.
    /// 본부가 없는 진영과 본부 자신은 항상 즉시 반영한다.
    /// </summary>
    public partial class Simulation
    {
        private readonly List<ReportEntry> _pendingReports = new List<ReportEntry>();

        /// <summary>
        /// 본부가 이 소대의 상태를 실시간으로 아는지. false면 화면에는 마지막 보고(TryGetLastReport)를 그려야 한다.
        /// </summary>
        public bool IsReportingLive(Platoon platoon)
        {
            return !UsesCommandNetwork(platoon)
                || platoon.Comms == CommsState.Direct
                || platoon.Comms == CommsState.Normal;
        }

        /// <summary>본부 상황판에 있는 이 소대의 마지막 보고. 실시간 보고 중이던 소대는 없을 수 있다.</summary>
        public bool TryGetLastReport(Platoon platoon, out FriendlyReport report)
        {
            return _intel[platoon.Side].TryGetFriendlyReport(platoon.Id, out report);
        }

        /// <summary>본부 상황판의 확인된 접촉을 이 소대가 받아 쓸 수 있는지(표적 정보 공유).</summary>
        private bool CanUseSideIntel(Platoon platoon)
        {
            return platoon.Comms != CommsState.Cut;
        }

        /// <summary>
        /// 이 소대가 표적을 "확인된 적"으로 알고 있는지. 자기가 추적 중이거나, 통신이 되면 본부 상황판의 확인된 접촉.
        /// </summary>
        private bool TryGetKnownEnemyPosition(Platoon platoon, Platoon target, out Vec2 position)
        {
            if (platoon.KnownEnemies.TryGetValue(target.Id, out LocalContact local) && local.IsTracking)
            {
                position = local.LastKnownPosition;
                return true;
            }

            if (CanUseSideIntel(platoon)
                && _intel[platoon.Side].TryGetContact(target.Id, out Contact contact)
                && contact.Confidence == ContactConfidence.Confirmed)
            {
                position = contact.LastKnownPosition;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>소대가 겪은 사건을 본부에 보고한다. 본부 쪽 로그(통신 상태, 명령 발신)는 Log를 그대로 쓴다.</summary>
        private void Report(Platoon platoon, string text)
        {
            if (IsReportingLive(platoon))
            {
                Log(platoon.Side, text);
                return;
            }

            Enqueue(new ReportEntry(platoon, ReportKind.Event, ElapsedSeconds) { Text = text });
        }

        private void ReportSighting(Platoon observer, Platoon target, bool isFirstSighting)
        {
            if (IsReportingLive(observer))
            {
                ApplySighting(observer, target.Id, target.Position, observer.Position, ElapsedSeconds);
                return;
            }

            if (observer.Comms == CommsState.Cut)
            {
                // 두절 중에는 표적마다 가장 최근 관측 하나만 쌓아 둔다. 목록 위치(처음 본 순서)는 유지한다.
                foreach (ReportEntry held in _pendingReports)
                {
                    if (held.Platoon == observer && held.Kind == ReportKind.Sighting
                        && held.TargetId == target.Id && held.IsWaitingForComms)
                    {
                        held.ObservedAtSeconds = ElapsedSeconds;
                        held.TargetPosition = target.Position;
                        held.ObserverPosition = observer.Position;
                        return;
                    }
                }
            }
            else if (!isFirstSighting && ElapsedSeconds % Rules.ReportSampleIntervalSeconds != 0)
            {
                // 저하 중 추적 갱신은 일정 간격으로만 보낸다.
                return;
            }

            Enqueue(new ReportEntry(observer, ReportKind.Sighting, ElapsedSeconds)
            {
                TargetId = target.Id,
                TargetPosition = target.Position,
                ObserverPosition = observer.Position
            });
        }

        /// <summary>통신 저하 중인 소대의 상태를 보낸다. 실시간이면 필요 없고, 두절이면 보낼 수 없다.</summary>
        private void ReportStatus(Platoon platoon)
        {
            if (!UsesCommandNetwork(platoon) || platoon.Comms != CommsState.Degraded)
            {
                return;
            }

            Enqueue(new ReportEntry(platoon, ReportKind.Status, ElapsedSeconds) { Snapshot = new PlatoonSnapshot(platoon) });
        }

        private void QueueStatusReports()
        {
            if (ElapsedSeconds % Rules.ReportSampleIntervalSeconds != 0)
            {
                return;
            }

            foreach (Platoon platoon in _platoons)
            {
                if (!platoon.IsDestroyed)
                {
                    ReportStatus(platoon);
                }
            }
        }

        private void Enqueue(ReportEntry entry)
        {
            entry.DeliverAtSeconds = GetReportDeliveryTime(entry.Platoon.Comms);
            _pendingReports.Add(entry);
        }

        private int GetReportDeliveryTime(CommsState comms)
        {
            switch (comms)
            {
                case CommsState.Direct:
                case CommsState.Normal:
                    return ElapsedSeconds;
                case CommsState.Degraded:
                    return ElapsedSeconds + Rules.DegradedReportDelaySeconds;
                default:
                    return ReportEntry.WaitingForComms;
            }
        }

        /// <summary>
        /// 소대가 실시간 보고를 멈추는 순간(저하·두절)의 상태를 상황판에 남긴다. 화면은 이 위치에서 소대를 놓친다.
        /// </summary>
        private void OnReportingLiveLost(Platoon platoon)
        {
            _intel[platoon.Side].SetFriendlyReport(platoon.Id, new FriendlyReport(new PlatoonSnapshot(platoon), ElapsedSeconds));
        }

        /// <summary>두절에서 회복되면 쌓아 둔 보고를 지금 통신 상태에 맞춰 보낸다(쌓인 순서대로).</summary>
        private void ReleaseHeldReports(Platoon platoon)
        {
            foreach (ReportEntry entry in _pendingReports)
            {
                if (entry.Platoon == platoon && entry.IsWaitingForComms)
                {
                    entry.DeliverAtSeconds = GetReportDeliveryTime(platoon.Comms);
                }
            }
        }

        private void DeliverReports()
        {
            for (int i = 0; i < _pendingReports.Count; i++)
            {
                ReportEntry entry = _pendingReports[i];
                if (entry.IsWaitingForComms || entry.DeliverAtSeconds > ElapsedSeconds)
                {
                    continue;
                }

                _pendingReports.RemoveAt(i);
                i--;
                Deliver(entry);
            }
        }

        private void Deliver(ReportEntry entry)
        {
            Platoon platoon = entry.Platoon;
            switch (entry.Kind)
            {
                case ReportKind.Event:
                    Log(platoon.Side, entry.Text + FormatObservedSuffix(entry.ObservedAtSeconds));
                    break;

                case ReportKind.Sighting:
                    ApplySighting(platoon, entry.TargetId, entry.TargetPosition, entry.ObserverPosition, entry.ObservedAtSeconds);
                    break;

                case ReportKind.Status:
                    SideIntel intel = _intel[platoon.Side];
                    bool isNewer = !intel.TryGetFriendlyReport(platoon.Id, out FriendlyReport existing)
                        || existing.ReportedAtSeconds < entry.ObservedAtSeconds;
                    if (isNewer)
                    {
                        intel.SetFriendlyReport(platoon.Id, new FriendlyReport(entry.Snapshot, entry.ObservedAtSeconds));
                    }

                    break;
            }
        }

        /// <summary>
        /// 관측 보고를 진영 상황판에 반영한다. 이미 더 새 정보가 있으면 버린다.
        /// 도착했을 때 이미 ContactLostSeconds보다 오래된 관측이면 "소실"로 등록한다(쏠 수 없다).
        /// </summary>
        private void ApplySighting(Platoon observer, int targetId, Vec2 targetPosition, Vec2 observerPosition, int observedAtSeconds)
        {
            SideIntel intel = _intel[observer.Side];
            bool isNew = !intel.TryGetContact(targetId, out Contact contact);
            if (!isNew && observedAtSeconds <= contact.LastSeenSeconds)
            {
                return;
            }

            if (isNew)
            {
                contact = intel.AddContact(targetId);
            }

            bool wasConfirmed = !isNew && contact.Confidence == ContactConfidence.Confirmed;
            bool isStale = ElapsedSeconds - observedAtSeconds >= Rules.ContactLostSeconds;
            contact.LastKnownPosition = targetPosition;
            contact.LastSeenSeconds = observedAtSeconds;
            contact.Confidence = isStale ? ContactConfidence.Lost : ContactConfidence.Confirmed;

            if (wasConfirmed || (isStale && !isNew))
            {
                return;
            }

            float distance = Vec2.Distance(observerPosition, targetPosition);
            float bearing = Bearing.Between(observerPosition, targetPosition);
            string kind = isNew ? "적 발견" : "적 재발견";
            Log(intel.Side,
                $"{observer.Name}: {kind} — 접촉 {contact.ContactNumber}, 방위 {bearing:000} 약 {RoundDistance(distance)}m ({Map.GetGridReference(targetPosition)})"
                + FormatObservedSuffix(observedAtSeconds));
        }

        /// <summary>늦게 도착한 보고에는 관측 시각을 붙인다. 로그 시각은 도착 시각이다.</summary>
        private string FormatObservedSuffix(int observedAtSeconds)
        {
            return observedAtSeconds == ElapsedSeconds ? string.Empty : $" (관측 {FormatClock(observedAtSeconds)})";
        }
    }
}
