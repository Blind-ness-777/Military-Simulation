using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 시뮬레이션 본체. 실제 상태(진실)를 갖고, 고정 1초 틱으로 진행한다.
    /// 틱 순서: 이동 → 탐지(진영 원장 갱신) → 교전(동시 사격) → 제압·사기 → 접촉 시 행동 → 인원 검산.
    /// 명령 지연과 본부 분대는 다음 단계에서 이 순서에 끼워 넣는다.
    /// </summary>
    public class Simulation
    {
        /// <summary>틱 길이(게임 시간 초). 기획서 결정 사항으로 1초 고정.</summary>
        public const int TickSeconds = 1;

        private static readonly Side[] AllSides = { Side.Blue, Side.Red };

        private readonly List<Platoon> _platoons = new List<Platoon>();
        private readonly Dictionary<int, Platoon> _platoonsById = new Dictionary<int, Platoon>();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly Dictionary<Side, SideIntel> _intel = new Dictionary<Side, SideIntel>();
        private readonly Dictionary<int, Platoon> _observedThisTick = new Dictionary<int, Platoon>();
        private readonly List<PendingFire> _pendingFire = new List<PendingFire>();
        private readonly DeterministicRandom _random;
        private int _nextPlatoonId = 1;

        public Simulation(MapData map, RulesConfig rules, int seed, int startTimeOfDaySeconds)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            StartTimeOfDaySeconds = startTimeOfDaySeconds;
            _random = new DeterministicRandom(seed);

            foreach (Side side in AllSides)
            {
                _intel.Add(side, new SideIntel(side));
            }
        }

        public MapData Map { get; }
        public RulesConfig Rules { get; }
        public int StartTimeOfDaySeconds { get; }
        public int ElapsedSeconds { get; private set; }
        public IReadOnlyList<Platoon> Platoons => _platoons;
        public IReadOnlyList<SimEvent> Events => _events;

        public Platoon AddPlatoon(string name, Side side, Vec2 position, float facingDegrees, int strength = 30)
        {
            if (strength <= 0) throw new ArgumentOutOfRangeException(nameof(strength));

            var platoon = new Platoon(
                _nextPlatoonId++, name, side, Map.ClampInside(position), facingDegrees, strength, Rules.InitialMorale);
            _platoons.Add(platoon);
            _platoonsById.Add(platoon.Id, platoon);
            return platoon;
        }

        public SideIntel GetIntel(Side side) => _intel[side];

        /// <summary>경과 시간을 "07:14:30" 형식의 시각으로.</summary>
        public string FormatClock(int elapsedSeconds)
        {
            int total = StartTimeOfDaySeconds + elapsedSeconds;
            int hours = total / 3600 % 24;
            int minutes = total / 60 % 60;
            int seconds = total % 60;
            return $"{hours:00}:{minutes:00}:{seconds:00}";
        }

        public void OrderMove(
            Platoon platoon,
            IReadOnlyList<Vec2> points,
            MoveMode moveMode,
            bool append,
            ContactBehavior contactBehavior = ContactBehavior.TakeCover)
        {
            if (points == null || points.Count == 0 || !CanTakeOrders(platoon))
            {
                return;
            }

            var waypoints = new List<Waypoint>(points.Count);
            foreach (Vec2 point in points)
            {
                waypoints.Add(new Waypoint(Map.ClampInside(point), moveMode, contactBehavior));
            }

            platoon.SetPath(waypoints, append);
            Vec2 destination = waypoints[waypoints.Count - 1].Position;
            string verb = append ? "경로 추가" : "이동";
            Log(platoon.Side,
                $"명령: {platoon.Name} {verb} → {Map.GetGridReference(destination)} ({DisplayNames.Of(moveMode)}, 접촉 시 {DisplayNames.Of(contactBehavior)})");
        }

        public void OrderWatch(Platoon platoon, float bearingDegrees, float arcDegrees)
        {
            if (!CanTakeOrders(platoon))
            {
                return;
            }

            platoon.SetWatch(bearingDegrees, arcDegrees);
            Log(platoon.Side, $"명령: {platoon.Name} 경계 방위 {platoon.WatchBearingDegrees:000} (폭 {arcDegrees:0}도)");
        }

        public void OrderHold(Platoon platoon)
        {
            if (!CanTakeOrders(platoon))
            {
                return;
            }

            platoon.Hold();
            Log(platoon.Side, $"명령: {platoon.Name} 정지");
        }

        public void OrderFirePolicy(Platoon platoon, FirePolicy firePolicy)
        {
            if (!CanTakeOrders(platoon))
            {
                return;
            }

            platoon.FirePolicy = firePolicy;
            Log(platoon.Side, $"명령: {platoon.Name} 사격 방침 {DisplayNames.Of(firePolicy)}");
        }

        public void Tick()
        {
            ElapsedSeconds += TickSeconds;
            UpdateMovement();
            UpdateDetection();
            UpdateCombat();
            UpdateSuppressionAndMorale();
            UpdateContactReactions();
            VerifyStrengthLedger();
        }

        /// <summary>
        /// 관측자가 표적을 이번 틱에 새로 발견할 확률. 시야선·사거리는 따로 검사한다.
        /// 분당 확률 p를 1 - (1 - p)^(틱/60)으로 환산해서, 틱 길이가 바뀌어도 결과 분포가 같다.
        /// </summary>
        public double GetDetectionChancePerTick(Platoon observer, Platoon target)
        {
            float distance = Vec2.Distance(observer.Position, target.Position);
            if (distance > Rules.MaxVisualRange)
            {
                return 0.0;
            }

            float rangeFactor = 1f - distance / Rules.MaxVisualRange;
            float bearingToTarget = Bearing.Between(observer.Position, target.Position);
            double perMinute = Rules.BaseDetectionPerMinute
                * rangeFactor
                * GetTargetMultiplier(target)
                * GetObserverMultiplier(observer, bearingToTarget);

            return PerMinuteToPerTick(perMinute);
        }

        /// <summary>
        /// 사수 1명이 이번 틱에 표적 1명을 무력화할 확률. 사격 가능 여부(시야선·사격 방침)는 따로 검사한다.
        /// </summary>
        public double GetCasualtyChancePerShooterPerTick(Platoon shooter, Platoon target)
        {
            float distance = Vec2.Distance(shooter.Position, target.Position);
            if (distance > Rules.FireRange)
            {
                return 0.0;
            }

            float rangeFactor = 1f - distance / Rules.FireRange;
            float bearingToTarget = Bearing.Between(shooter.Position, target.Position);
            double perMinute = Rules.BaseCasualtyPerShooterPerMinute
                * rangeFactor
                * GetTargetExposure(target)
                * GetShooterMultiplier(shooter, bearingToTarget);

            return PerMinuteToPerTick(perMinute);
        }

        /// <summary>사거리 안이고 시야선이 열려 있는지.</summary>
        public bool CanObserve(Platoon observer, Platoon target)
        {
            if (Vec2.Distance(observer.Position, target.Position) > Rules.MaxVisualRange)
            {
                return false;
            }

            return LineOfSight.HasLineOfSight(
                Map,
                observer.Position,
                Rules.EyeHeight,
                target.Position,
                Rules.TargetHeight,
                Rules.ForestCanopyHeight);
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

        private void UpdateMovement()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (!platoon.IsMoving || platoon.IsDestroyed)
                {
                    continue;
                }

                float remaining = GetMoveSpeed(platoon) * TickSeconds;
                while (platoon.IsMoving && remaining > 0f)
                {
                    Vec2 target = platoon.Waypoints[0].Position;
                    Vec2 toTarget = target - platoon.Position;
                    float distance = toTarget.Length;

                    if (distance > 1e-4f)
                    {
                        platoon.FacingDegrees = Bearing.Between(platoon.Position, target);
                    }

                    if (distance <= remaining)
                    {
                        platoon.Position = target;
                        remaining -= distance;
                        platoon.RemoveReachedWaypoint();

                        if (!platoon.IsMoving)
                        {
                            Log(platoon.Side, $"{platoon.Name} {Map.GetGridReference(target)} 도착");
                        }
                    }
                    else
                    {
                        platoon.Position += toTarget.Normalized * remaining;
                        remaining = 0f;
                    }
                }
            }
        }

        private void UpdateDetection()
        {
            foreach (Side side in AllSides)
            {
                SideIntel intel = _intel[side];
                _observedThisTick.Clear();

                foreach (Platoon observer in _platoons)
                {
                    if (observer.Side != side || observer.IsDestroyed)
                    {
                        continue;
                    }

                    foreach (Platoon target in _platoons)
                    {
                        if (target.Side == side || target.IsDestroyed || _observedThisTick.ContainsKey(target.Id))
                        {
                            continue;
                        }

                        if (!CanObserve(observer, target))
                        {
                            continue;
                        }

                        // 이미 확인 중인 접촉은 시야선이 유지되는 한 계속 추적한다(재판정 없음).
                        bool alreadyTracked = intel.TryGetContact(target.Id, out Contact existing)
                            && existing.Confidence == ContactConfidence.Confirmed;

                        if (alreadyTracked || _random.NextDouble() < GetDetectionChancePerTick(observer, target))
                        {
                            _observedThisTick.Add(target.Id, observer);
                        }
                    }
                }

                ApplyObservations(intel);
                MarkLostContacts(intel);
            }
        }

        private void ApplyObservations(SideIntel intel)
        {
            // 로그 순서가 결정론적이도록 Dictionary 순회 대신 부대 목록 순서로 처리한다.
            foreach (Platoon target in _platoons)
            {
                if (!_observedThisTick.TryGetValue(target.Id, out Platoon observer))
                {
                    continue;
                }

                bool isNew = !intel.TryGetContact(target.Id, out Contact contact);
                if (isNew)
                {
                    contact = intel.AddContact(target.Id);
                }

                bool wasConfirmed = !isNew && contact.Confidence == ContactConfidence.Confirmed;
                contact.LastKnownPosition = target.Position;
                contact.LastSeenSeconds = ElapsedSeconds;
                contact.Confidence = ContactConfidence.Confirmed;

                if (!wasConfirmed)
                {
                    float distance = Vec2.Distance(observer.Position, target.Position);
                    float bearing = Bearing.Between(observer.Position, target.Position);
                    string kind = isNew ? "적 발견" : "적 재발견";
                    Log(intel.Side,
                        $"{observer.Name}: {kind} — 접촉 {contact.ContactNumber}, 방위 {bearing:000} 약 {RoundDistance(distance)}m ({Map.GetGridReference(target.Position)})");
                }
            }
        }

        private void MarkLostContacts(SideIntel intel)
        {
            foreach (Contact contact in intel.Contacts)
            {
                if (contact.Confidence != ContactConfidence.Confirmed)
                {
                    continue;
                }

                if (ElapsedSeconds - contact.LastSeenSeconds >= Rules.ContactLostSeconds)
                {
                    contact.Confidence = ContactConfidence.Lost;
                    Log(intel.Side,
                        $"접촉 {contact.ContactNumber} 소실 — 마지막 확인 {Map.GetGridReference(contact.LastKnownPosition)}, {FormatClock(contact.LastSeenSeconds)}");
                }
            }
        }

        /// <summary>
        /// 모든 소대가 틱 시작 시점의 상태로 동시에 사격하고, 결과는 그 뒤에 한꺼번에 적용한다.
        /// 부대 목록 순서 때문에 먼저 쏜 쪽이 유리해지지 않게 하기 위해서다.
        /// </summary>
        private void UpdateCombat()
        {
            _pendingFire.Clear();

            foreach (Platoon shooter in _platoons)
            {
                Platoon target = SelectTarget(shooter);
                LogTargetChange(shooter, target);
                shooter.CurrentTargetId = target != null ? target.Id : 0;
                if (target == null)
                {
                    continue;
                }

                int shooters = GetActiveShooters(shooter);
                double chance = GetCasualtyChancePerShooterPerTick(shooter, target);
                int hits = 0;
                for (int i = 0; i < shooters; i++)
                {
                    if (_random.NextDouble() < chance)
                    {
                        hits++;
                    }
                }

                float distance = Vec2.Distance(shooter.Position, target.Position);
                float suppressionRangeFactor = Math.Max(0.3f, 1f - distance / Rules.FireRange);
                float suppression = shooters * Rules.SuppressionPerShooterPerMinute / 60f * TickSeconds * suppressionRangeFactor;
                _pendingFire.Add(new PendingFire(target.Id, hits, suppression));
            }

            foreach (Platoon target in _platoons)
            {
                int hits = 0;
                float suppression = 0f;
                bool firedUpon = false;
                foreach (PendingFire fire in _pendingFire)
                {
                    if (fire.TargetId != target.Id)
                    {
                        continue;
                    }

                    firedUpon = true;
                    hits += fire.Hits;
                    suppression += fire.Suppression;
                }

                if (!firedUpon)
                {
                    continue;
                }

                target.LastFiredUponSeconds = ElapsedSeconds;
                int casualties = ApplyCasualties(target, hits);
                target.Suppression = Math.Min(100f, target.Suppression + suppression + casualties * Rules.SuppressionPerCasualty);
            }
        }

        private Platoon SelectTarget(Platoon shooter)
        {
            if (shooter.IsDestroyed || shooter.IsBroken || shooter.FirePolicy == FirePolicy.HoldFire)
            {
                return null;
            }

            float maxRange = shooter.FirePolicy == FirePolicy.CloseOnly ? Rules.CloseFireRange : Rules.FireRange;
            Platoon best = null;
            float bestDistance = float.MaxValue;

            // 사격은 자기 진영 정보에서 "확인"된 접촉에만 한다(기획서 5.4).
            foreach (Contact contact in _intel[shooter.Side].Contacts)
            {
                if (contact.Confidence != ContactConfidence.Confirmed)
                {
                    continue;
                }

                Platoon target = _platoonsById[contact.TargetId];
                if (target.IsDestroyed)
                {
                    continue;
                }

                float distance = Vec2.Distance(shooter.Position, target.Position);
                if (distance > maxRange || distance >= bestDistance || !CanObserve(shooter, target))
                {
                    continue;
                }

                best = target;
                bestDistance = distance;
            }

            return best;
        }

        private void LogTargetChange(Platoon shooter, Platoon newTarget)
        {
            int newTargetId = newTarget != null ? newTarget.Id : 0;
            if (newTargetId == shooter.CurrentTargetId)
            {
                return;
            }

            if (newTarget == null)
            {
                Log(shooter.Side, $"{shooter.Name} 사격 중지");
                return;
            }

            _intel[shooter.Side].TryGetContact(newTarget.Id, out Contact contact);
            float distance = Vec2.Distance(shooter.Position, newTarget.Position);
            Log(shooter.Side, $"{shooter.Name} 사격 개시 → 접촉 {contact.ContactNumber} (약 {RoundDistance(distance)}m)");
        }

        private int ApplyCasualties(Platoon target, int hits)
        {
            int casualties = Math.Min(hits, target.Effective);
            if (casualties <= 0)
            {
                return 0;
            }

            int killed = 0;
            for (int i = 0; i < casualties; i++)
            {
                if (_random.NextDouble() < Rules.KilledRatio)
                {
                    killed++;
                }
            }

            int wounded = casualties - killed;
            target.Effective -= casualties;
            target.Killed += killed;
            target.Wounded += wounded;
            target.Morale = Math.Max(0f, target.Morale - casualties * Rules.MoraleLossPerCasualty);

            Log(target.Side,
                $"{target.Name} 피해: 전사 {killed}, 부상 {wounded} (전투 가능 {target.Effective}/{target.InitialStrength})");

            if (target.IsDestroyed)
            {
                target.Hold();
                target.CurrentTargetId = 0;
                Log(target.Side, $"{target.Name} 전투 불능");
            }

            return casualties;
        }

        private void UpdateSuppressionAndMorale()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.IsDestroyed)
                {
                    continue;
                }

                bool underFire = platoon.LastFiredUponSeconds == ElapsedSeconds;
                if (!underFire)
                {
                    platoon.Suppression = Math.Max(0f, platoon.Suppression - Rules.SuppressionDecayPerSecond * TickSeconds);
                }

                UpdatePinned(platoon);

                if (platoon.IsPinned)
                {
                    platoon.Morale = Math.Max(0f, platoon.Morale - Rules.MoraleLossPerSecondPinned * TickSeconds);
                }
                else if (!underFire && platoon.Suppression < Rules.MoraleRecoverySuppressionLimit)
                {
                    platoon.Morale = Math.Min(Rules.InitialMorale, platoon.Morale + Rules.MoraleRecoveryPerSecond * TickSeconds);
                }

                UpdateBroken(platoon, underFire);
            }
        }

        private void UpdatePinned(Platoon platoon)
        {
            bool pinned = platoon.Suppression >= Rules.PinnedSuppression;
            if (pinned == platoon.IsPinned)
            {
                return;
            }

            platoon.IsPinned = pinned;
            Log(platoon.Side, pinned ? $"{platoon.Name} 제압됨 — 이동 불가" : $"{platoon.Name} 제압 해소");
        }

        private void UpdateBroken(Platoon platoon, bool underFire)
        {
            if (!platoon.IsBroken && platoon.Morale <= Rules.BreakMorale)
            {
                platoon.IsBroken = true;
                platoon.CurrentTargetId = 0;
                Vec2 retreatPoint = StartBrokenRetreat(platoon);
                Log(platoon.Side, $"{platoon.Name} 사기 붕괴 — {Map.GetGridReference(retreatPoint)}로 무질서 후퇴, 명령 불가");
            }
            else if (platoon.IsBroken && !platoon.IsMoving && underFire)
            {
                // 후퇴한 곳에서도 계속 사격을 받으면 더 물러난다.
                Vec2 retreatPoint = StartBrokenRetreat(platoon);
                Log(platoon.Side, $"{platoon.Name} 계속 피격 — {Map.GetGridReference(retreatPoint)}로 추가 후퇴");
            }
            else if (platoon.IsBroken && platoon.Morale >= Rules.RallyMorale)
            {
                platoon.IsBroken = false;
                Log(platoon.Side, $"{platoon.Name} 재집결 — 명령 가능");
            }
        }

        private Vec2 StartBrokenRetreat(Platoon platoon)
        {
            Vec2 retreatPoint = GetBrokenRetreatPoint(platoon);
            platoon.SetPath(new[] { new Waypoint(retreatPoint, MoveMode.Fast, ContactBehavior.PushThrough) }, false);
            return retreatPoint;
        }

        /// <summary>
        /// 붕괴한 소대의 후퇴 지점: 자기 진영이 아는 가장 가까운 확인된 적의 반대 방향으로 BrokenRetreatDistance.
        /// 아는 적이 없으면 현재 구간의 출발 지점.
        /// </summary>
        private Vec2 GetBrokenRetreatPoint(Platoon platoon)
        {
            Contact nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (Contact contact in _intel[platoon.Side].Contacts)
            {
                if (contact.Confidence != ContactConfidence.Confirmed)
                {
                    continue;
                }

                float distance = Vec2.Distance(platoon.Position, contact.LastKnownPosition);
                if (distance < nearestDistance)
                {
                    nearest = contact;
                    nearestDistance = distance;
                }
            }

            if (nearest == null)
            {
                return platoon.SegmentStart;
            }

            Vec2 away = (platoon.Position - nearest.LastKnownPosition).Normalized;
            return Map.ClampInside(platoon.Position + away * Rules.BrokenRetreatDistance);
        }

        /// <summary>
        /// 접촉이 "시작될 때" 이동 중이면 현재 구간의 접촉 시 행동을 한 번 실행한다.
        /// 접촉 = 이번 틱에 사격 중이거나, 최근 ContactMemorySeconds 안에 사격을 받음.
        /// </summary>
        private void UpdateContactReactions()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.IsDestroyed || platoon.IsBroken)
                {
                    platoon.WasInContact = false;
                    continue;
                }

                bool inContact = platoon.IsFiring
                    || ElapsedSeconds - platoon.LastFiredUponSeconds <= Rules.ContactMemorySeconds;

                if (inContact && !platoon.WasInContact && platoon.IsMoving)
                {
                    ApplyContactBehavior(platoon);
                }

                platoon.WasInContact = inContact;
            }
        }

        private void ApplyContactBehavior(Platoon platoon)
        {
            switch (platoon.CurrentContactBehavior)
            {
                case ContactBehavior.TakeCover:
                    platoon.Hold();
                    Log(platoon.Side, $"{platoon.Name} 접촉 — 엄폐 후 응사");
                    break;

                case ContactBehavior.PushThrough:
                    Log(platoon.Side, $"{platoon.Name} 접촉 — 돌파 계속");
                    break;

                case ContactBehavior.FallBack:
                    Vec2 fallBackPoint = platoon.SegmentStart;
                    platoon.SetPath(
                        new[] { new Waypoint(fallBackPoint, MoveMode.Fast, ContactBehavior.PushThrough) },
                        false);
                    Log(platoon.Side, $"{platoon.Name} 접촉 — {Map.GetGridReference(fallBackPoint)}로 후퇴");
                    break;
            }
        }

        /// <summary>인원 검산. 맞지 않으면 엔진 버그이므로 즉시 멈춘다(WMS 마감 검산과 같은 역할).</summary>
        private void VerifyStrengthLedger()
        {
            foreach (Platoon platoon in _platoons)
            {
                int total = platoon.Effective + platoon.Wounded + platoon.Killed;
                if (total != platoon.InitialStrength || platoon.Effective < 0)
                {
                    throw new InvalidOperationException(
                        $"인원 검산 실패: {platoon.Name} 전투 가능 {platoon.Effective} + 부상 {platoon.Wounded} + 전사 {platoon.Killed} ≠ {platoon.InitialStrength}");
                }
            }
        }

        private float GetMoveSpeed(Platoon platoon)
        {
            // 붕괴한 부대는 제압 여부와 상관없이 후퇴한다(공황 후퇴).
            if (platoon.IsPinned && !platoon.IsBroken)
            {
                return 0f;
            }

            float speed;
            switch (platoon.CurrentMoveMode)
            {
                case MoveMode.Fast:
                    speed = Rules.FastSpeed;
                    break;
                case MoveMode.Stealth:
                    speed = Rules.StealthSpeed;
                    break;
                default:
                    speed = Rules.NormalSpeed;
                    break;
            }

            switch (Map.GetTerrainAt(platoon.Position))
            {
                case TerrainType.Forest:
                    speed *= Rules.ForestSpeedMultiplier;
                    break;
                case TerrainType.Road:
                    speed *= Rules.RoadSpeedMultiplier;
                    break;
            }

            return speed * (1f - platoon.Suppression / 100f * Rules.SuppressionSpeedReduction);
        }

        private float GetTargetMultiplier(Platoon target)
        {
            float multiplier;
            if (!target.IsMoving)
            {
                multiplier = Rules.TargetStationaryMultiplier;
            }
            else
            {
                switch (target.CurrentMoveMode)
                {
                    case MoveMode.Fast:
                        multiplier = Rules.TargetFastMultiplier;
                        break;
                    case MoveMode.Stealth:
                        multiplier = Rules.TargetStealthMultiplier;
                        break;
                    default:
                        multiplier = Rules.TargetNormalMultiplier;
                        break;
                }
            }

            if (target.IsFiring)
            {
                multiplier *= Rules.TargetFiringMultiplier;
            }

            if (Map.GetTerrainAt(target.Position) == TerrainType.Forest)
            {
                multiplier *= Rules.TargetInForestMultiplier;
            }

            return multiplier;
        }

        private float GetObserverMultiplier(Platoon observer, float bearingToTarget)
        {
            if (observer.IsMoving)
            {
                return Rules.ObserverMovingMultiplier;
            }

            if (observer.IsWatching)
            {
                return observer.IsInWatchArc(bearingToTarget)
                    ? Rules.ObserverWatchInArcMultiplier
                    : Rules.ObserverWatchOutOfArcMultiplier;
            }

            return Rules.ObserverStationaryMultiplier;
        }

        private float GetTargetExposure(Platoon target)
        {
            float exposure;
            if (!target.IsMoving)
            {
                exposure = Rules.TargetExposureStationary;
            }
            else
            {
                switch (target.CurrentMoveMode)
                {
                    case MoveMode.Fast:
                        exposure = Rules.TargetExposureFast;
                        break;
                    case MoveMode.Stealth:
                        exposure = Rules.TargetExposureStealth;
                        break;
                    default:
                        exposure = Rules.TargetExposureNormal;
                        break;
                }
            }

            if (Map.GetTerrainAt(target.Position) == TerrainType.Forest)
            {
                exposure *= Rules.TargetExposureInForest;
            }

            return exposure;
        }

        private float GetShooterMultiplier(Platoon shooter, float bearingToTarget)
        {
            if (shooter.IsMoving)
            {
                return Rules.ShooterMovingMultiplier;
            }

            return shooter.IsInWatchArc(bearingToTarget) ? Rules.ShooterWatchInArcMultiplier : 1f;
        }

        /// <summary>제압된 만큼 사격에 참여하는 인원이 줄어든다.</summary>
        private int GetActiveShooters(Platoon shooter)
        {
            float reduction = shooter.Suppression / 100f * Rules.SuppressionFireReduction;
            return (int)Math.Round(shooter.Effective * (1f - reduction));
        }

        private static double PerMinuteToPerTick(double perMinute)
        {
            perMinute = Math.Min(Math.Max(perMinute, 0.0), 0.999);
            return 1.0 - Math.Pow(1.0 - perMinute, TickSeconds / 60.0);
        }

        private void Log(Side side, string text)
        {
            _events.Add(new SimEvent(ElapsedSeconds, side, text));
        }

        private static int RoundDistance(float distance) => (int)(Math.Round(distance / 10.0) * 10);

        private readonly struct PendingFire
        {
            public readonly int TargetId;
            public readonly int Hits;
            public readonly float Suppression;

            public PendingFire(int targetId, int hits, float suppression)
            {
                TargetId = targetId;
                Hits = hits;
                Suppression = suppression;
            }
        }
    }
}
