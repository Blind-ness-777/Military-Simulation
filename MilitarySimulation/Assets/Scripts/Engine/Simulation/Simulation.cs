using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 시뮬레이션 본체. 실제 상태(진실)를 갖고, 고정 1초 틱으로 진행한다.
    /// 틱 순서: 통신 판정 → 명령 도착 → 보고 도착 → 이동 → 탐지(소대 인지 갱신, 보고 발신) → 교전(동시 사격)
    ///          → 제압·사기 → 접촉 시 행동 → 자체 판단 모드 → 상태 보고 → 인원 검산.
    /// 명령·통신·자체 판단은 Simulation.Command.cs, 소대 → 본부 보고는 Simulation.Reports.cs에 있다(같은 클래스를 파일만 나눔).
    /// </summary>
    public partial class Simulation
    {
        /// <summary>틱 길이(게임 시간 초). 기획서 결정 사항으로 1초 고정.</summary>
        public const int TickSeconds = 1;

        private static readonly Side[] AllSides = { Side.Blue, Side.Red };

        private readonly List<Platoon> _platoons = new List<Platoon>();
        private readonly Dictionary<int, Platoon> _platoonsById = new Dictionary<int, Platoon>();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly Dictionary<Side, SideIntel> _intel = new Dictionary<Side, SideIntel>();
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

        /// <summary>
        /// 부대를 추가한다. 병과를 주지 않으면 역할에 맞는 기본 병과(보병 소대 / 본부)를 새로 만들어 쓴다.
        /// 인원을 주지 않으면 병과의 기본 인원.
        /// </summary>
        public Platoon AddPlatoon(
            string name,
            Side side,
            Vec2 position,
            float facingDegrees,
            int? strength = null,
            PlatoonRole role = PlatoonRole.Line,
            UnitType unitType = null)
        {
            if (unitType == null)
            {
                unitType = role == PlatoonRole.Headquarters
                    ? UnitTypes.CreateHeadquarters()
                    : UnitTypes.CreateInfantryPlatoon();
            }

            int initialStrength = strength ?? unitType.DefaultStrength;
            if (initialStrength <= 0) throw new ArgumentOutOfRangeException(nameof(strength));
            if (role == PlatoonRole.Headquarters && TryGetHeadquarters(side, out _))
            {
                throw new InvalidOperationException($"{side} 진영에는 이미 본부가 있습니다.");
            }

            var platoon = new Platoon(
                _nextPlatoonId++,
                name,
                side,
                Map.ClampInside(position),
                facingDegrees,
                initialStrength,
                Rules.InitialMorale,
                role,
                unitType);
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

        public void Tick()
        {
            ElapsedSeconds += TickSeconds;
            UpdateComms();
            DeliverPendingOrders();
            DeliverReports();
            UpdateMovement();
            UpdateDetection();
            UpdateCombat();
            UpdateSuppressionAndMorale();
            UpdateContactReactions();
            UpdateSelfDirected();
            QueueStatusReports();
            VerifyStrengthLedger();
        }

        /// <summary>
        /// 관측자가 표적을 이번 틱에 새로 발견할 확률. 시야선·사거리는 따로 검사한다.
        /// 분당 확률 p를 1 - (1 - p)^(틱/60)으로 환산해서, 틱 길이가 바뀌어도 결과 분포가 같다.
        /// </summary>
        public double GetDetectionChancePerTick(Platoon observer, Platoon target)
        {
            float maxVisualRange = observer.UnitType.MaxVisualRange;
            float distance = Vec2.Distance(observer.Position, target.Position);
            if (distance > maxVisualRange)
            {
                return 0.0;
            }

            float rangeFactor = 1f - distance / maxVisualRange;
            float bearingToTarget = Bearing.Between(observer.Position, target.Position);
            double perMinute = Rules.BaseDetectionPerMinute
                * rangeFactor
                * GetTargetMultiplier(target)
                * GetObserverMultiplier(observer, bearingToTarget);

            // 표적이 직전 틱에 바로 이 관측자를 쏘고 있었다면, 관측자는 사격 방향을 알고 있다.
            if (target.IsFiring && target.CurrentTargetId == observer.Id)
            {
                perMinute *= Rules.ObserverUnderFireFromTargetMultiplier;
            }

            return PerMinuteToPerTick(perMinute);
        }

        /// <summary>
        /// 사수 1명이 이번 틱에 표적 1명을 무력화할 확률. 사격 가능 여부(시야선·사격 방침)는 따로 검사한다.
        /// </summary>
        public double GetCasualtyChancePerShooterPerTick(Platoon shooter, Platoon target)
        {
            WeaponProfile weapon = shooter.UnitType.Weapon;
            float distance = Vec2.Distance(shooter.Position, target.Position);
            if (distance > weapon.FireRange)
            {
                return 0.0;
            }

            float rangeFactor = 1f - distance / weapon.FireRange;
            float bearingToTarget = Bearing.Between(shooter.Position, target.Position);
            double perMinute = weapon.CasualtyPerShooterPerMinute
                * rangeFactor
                * GetTargetExposure(target)
                * GetShooterMultiplier(shooter, bearingToTarget);

            return PerMinuteToPerTick(perMinute);
        }

        /// <summary>관측자의 관측 거리 안이고 시야선이 열려 있는지(관측자 눈높이 → 표적 높이).</summary>
        public bool CanObserve(Platoon observer, Platoon target)
        {
            if (Vec2.Distance(observer.Position, target.Position) > observer.UnitType.MaxVisualRange)
            {
                return false;
            }

            return LineOfSight.HasLineOfSight(
                Map,
                observer.Position,
                observer.UnitType.EyeHeight,
                target.Position,
                target.UnitType.TargetHeight,
                Rules.ForestCanopyHeight);
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
                            Report(platoon, $"{platoon.Name} {Map.GetGridReference(target)} 도착");
                        }
                    }
                    else
                    {
                        platoon.Position += toTarget.Normalized * remaining;
                        remaining = 0f;
                    }
                }

                // 자체 판단으로 지나온 길을 되짚는 동안은 기록하지 않는다(기록하면 다음 후퇴 때 앞으로 되돌아간다).
                bool retracingTrail = platoon.IsSelfDirected && platoon.SelfDirectedStage > 0;
                if (!retracingTrail)
                {
                    platoon.RecordTrail(Rules.TrailSpacing);
                }
            }
        }

        /// <summary>
        /// 소대마다 따로 탐지한다(소대 인지). 이미 추적 중인 표적은 시야선이 유지되는 한 재판정 없이 계속 추적한다.
        /// 본 것은 보고로 본부에 올라가고, 진영 상황판은 보고가 도착했을 때 갱신된다(Simulation.Reports.cs).
        /// </summary>
        private void UpdateDetection()
        {
            foreach (Platoon observer in _platoons)
            {
                if (observer.IsDestroyed)
                {
                    continue;
                }

                foreach (Platoon target in _platoons)
                {
                    if (target.Side == observer.Side || target.IsDestroyed || !CanObserve(observer, target))
                    {
                        continue;
                    }

                    observer.KnownEnemies.TryGetValue(target.Id, out LocalContact local);
                    bool tracking = local != null && local.IsTracking;
                    if (!tracking && _random.NextDouble() >= GetDetectionChancePerTick(observer, target))
                    {
                        continue;
                    }

                    if (local == null)
                    {
                        local = new LocalContact(target.Id);
                        observer.KnownEnemies.Add(target.Id, local);
                    }

                    local.LastKnownPosition = target.Position;
                    local.LastSeenSeconds = ElapsedSeconds;
                    local.IsTracking = true;
                    ReportSighting(observer, target, !tracking);
                }

                LoseLocalContacts(observer);
            }

            foreach (Side side in AllSides)
            {
                MarkLostContacts(_intel[side]);
            }
        }

        private void LoseLocalContacts(Platoon observer)
        {
            foreach (LocalContact local in observer.KnownEnemies.Values)
            {
                if (local.IsTracking && ElapsedSeconds - local.LastSeenSeconds >= Rules.ContactLostSeconds)
                {
                    local.IsTracking = false;
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
                float suppressionRangeFactor = Math.Max(0.3f, 1f - distance / shooter.UnitType.Weapon.FireRange);
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

            WeaponProfile weapon = shooter.UnitType.Weapon;
            float maxRange = shooter.FirePolicy == FirePolicy.CloseOnly ? weapon.CloseFireRange : weapon.FireRange;
            Platoon best = null;
            float bestDistance = float.MaxValue;

            // 사격은 "확인"된 적에게만 한다(기획서 5.4). 자기가 추적 중인 적, 또는 통신이 되면 본부 상황판의 확인된 접촉.
            // 부대 목록 순서로 순회해서 같은 거리면 결과가 결정론적이다.
            foreach (Platoon target in _platoons)
            {
                if (target.Side == shooter.Side || target.IsDestroyed || !TryGetKnownEnemyPosition(shooter, target, out _))
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
                Report(shooter, $"{shooter.Name} 사격 중지");
                return;
            }

            // 발견 보고가 아직 본부에 도착하지 않았으면 상황판 접촉 번호가 없다.
            string targetName = _intel[shooter.Side].TryGetContact(newTarget.Id, out Contact contact)
                ? $"접촉 {contact.ContactNumber}"
                : "적";
            float distance = Vec2.Distance(shooter.Position, newTarget.Position);
            Report(shooter, $"{shooter.Name} 사격 개시 → {targetName} (약 {RoundDistance(distance)}m)");
        }

        private int ApplyCasualties(Platoon target, int hits)
        {
            int casualties = Math.Min(hits, target.Effective);
            if (casualties <= 0)
            {
                return 0;
            }

            if (target.IsHeadquarters && target.CommsEquipment != CommsEquipment.None)
            {
                CheckRadioOperatorLoss(target, casualties);
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

            Report(target,
                $"{target.Name} 피해: 전사 {killed}, 부상 {wounded} (전투 가능 {target.Effective}/{target.InitialStrength})");

            if (target.IsDestroyed)
            {
                target.Hold();
                target.CurrentTargetId = 0;

                // 본부가 바로 알 수 있을 때만 대기 명령을 거둔다. 모르면 명령은 남고, 도착 시점에 버려진다.
                if (IsReportingLive(target))
                {
                    RemovePendingOrders(target);
                }

                Report(target,
                    target.IsHeadquarters ? $"{target.Name} 전투 불능 — 지휘 상실" : $"{target.Name} 전투 불능");

                // 통신 저하 중이면 전투 불능 상태를 마지막 보고로 보낸다(이후 통신 판정에서 빠진다).
                ReportStatus(target);
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
            Report(platoon, pinned ? $"{platoon.Name} 제압됨 — 이동 불가" : $"{platoon.Name} 제압 해소");
        }

        private void UpdateBroken(Platoon platoon, bool underFire)
        {
            if (!platoon.IsBroken && platoon.Morale <= Rules.BreakMorale)
            {
                platoon.IsBroken = true;
                platoon.CurrentTargetId = 0;
                Vec2 retreatPoint = StartBrokenRetreat(platoon);
                Report(platoon, $"{platoon.Name} 사기 붕괴 — {Map.GetGridReference(retreatPoint)}로 무질서 후퇴, 명령 불가");
            }
            else if (platoon.IsBroken && !platoon.IsMoving && underFire)
            {
                // 후퇴한 곳에서도 계속 사격을 받으면 더 물러난다.
                Vec2 retreatPoint = StartBrokenRetreat(platoon);
                Report(platoon, $"{platoon.Name} 계속 피격 — {Map.GetGridReference(retreatPoint)}로 추가 후퇴");
            }
            else if (platoon.IsBroken && platoon.Morale >= Rules.RallyMorale)
            {
                platoon.IsBroken = false;
                Report(platoon, $"{platoon.Name} 재집결 — 명령 가능");
            }
        }

        private Vec2 StartBrokenRetreat(Platoon platoon)
        {
            Vec2 retreatPoint = GetBrokenRetreatPoint(platoon);
            platoon.SetPath(new[] { new Waypoint(retreatPoint, MoveMode.Fast, ContactBehavior.PushThrough) }, false);
            return retreatPoint;
        }

        /// <summary>
        /// 붕괴한 소대의 후퇴 지점: 이 소대가 아는 가장 가까운 확인된 적의 반대 방향으로 BrokenRetreatDistance.
        /// (자기가 추적 중인 적, 통신이 되면 본부 상황판의 확인된 접촉까지.) 아는 적이 없으면 현재 구간의 출발 지점.
        /// </summary>
        private Vec2 GetBrokenRetreatPoint(Platoon platoon)
        {
            bool found = false;
            Vec2 nearest = default;
            float nearestDistance = float.MaxValue;
            foreach (Platoon enemy in _platoons)
            {
                if (enemy.Side == platoon.Side || !TryGetKnownEnemyPosition(platoon, enemy, out Vec2 position))
                {
                    continue;
                }

                float distance = Vec2.Distance(platoon.Position, position);
                if (distance < nearestDistance)
                {
                    found = true;
                    nearest = position;
                    nearestDistance = distance;
                }
            }

            if (!found)
            {
                return platoon.SegmentStart;
            }

            Vec2 away = (platoon.Position - nearest).Normalized;
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
                    Report(platoon, $"{platoon.Name} 접촉 — 엄폐 후 응사");
                    break;

                case ContactBehavior.PushThrough:
                    Report(platoon, $"{platoon.Name} 접촉 — 돌파 계속");
                    break;

                case ContactBehavior.FallBack:
                    Vec2 fallBackPoint = platoon.SegmentStart;
                    platoon.SetPath(
                        new[] { new Waypoint(fallBackPoint, MoveMode.Fast, ContactBehavior.PushThrough) },
                        false);
                    Report(platoon, $"{platoon.Name} 접촉 — {Map.GetGridReference(fallBackPoint)}로 후퇴");
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

            UnitType unitType = platoon.UnitType;
            float speed;
            switch (platoon.CurrentMoveMode)
            {
                case MoveMode.Fast:
                    speed = unitType.FastSpeed;
                    break;
                case MoveMode.Stealth:
                    speed = unitType.StealthSpeed;
                    break;
                default:
                    speed = unitType.NormalSpeed;
                    break;
            }

            switch (Map.GetTerrainAt(platoon.Position))
            {
                case TerrainType.Forest:
                    speed *= unitType.ForestSpeedMultiplier;
                    break;
                case TerrainType.Road:
                    speed *= unitType.RoadSpeedMultiplier;
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
