using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 시뮬레이션 본체. 실제 상태(진실)를 갖고, 고정 1초 틱으로 진행한다.
    /// 틱 순서: 이동 → 탐지(진영 원장 갱신). 교전·피해·명령 지연은 다음 단계에서 이 순서에 끼워 넣는다.
    /// </summary>
    public class Simulation
    {
        /// <summary>틱 길이(게임 시간 초). 기획서 결정 사항으로 1초 고정.</summary>
        public const int TickSeconds = 1;

        private static readonly Side[] AllSides = { Side.Blue, Side.Red };

        private readonly List<Platoon> _platoons = new List<Platoon>();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly Dictionary<Side, SideIntel> _intel = new Dictionary<Side, SideIntel>();
        private readonly Dictionary<int, Platoon> _observedThisTick = new Dictionary<int, Platoon>();
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

        public Platoon AddPlatoon(string name, Side side, Vec2 position, float facingDegrees)
        {
            var platoon = new Platoon(_nextPlatoonId++, name, side, Map.ClampInside(position), facingDegrees);
            _platoons.Add(platoon);
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

        public void OrderMove(Platoon platoon, IReadOnlyList<Vec2> points, MoveMode moveMode, bool append)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            var waypoints = new List<Waypoint>(points.Count);
            foreach (Vec2 point in points)
            {
                waypoints.Add(new Waypoint(Map.ClampInside(point), moveMode));
            }

            platoon.SetPath(waypoints, append);
            Vec2 destination = waypoints[waypoints.Count - 1].Position;
            string verb = append ? "경로 추가" : "이동";
            Log(platoon.Side, $"명령: {platoon.Name} {verb} → {Map.GetGridReference(destination)} ({MoveModeName(moveMode)})");
        }

        public void OrderWatch(Platoon platoon, float bearingDegrees, float arcDegrees)
        {
            platoon.SetWatch(bearingDegrees, arcDegrees);
            Log(platoon.Side, $"명령: {platoon.Name} 경계 방위 {platoon.WatchBearingDegrees:000} (폭 {arcDegrees:0}도)");
        }

        public void OrderHold(Platoon platoon)
        {
            platoon.Hold();
            Log(platoon.Side, $"명령: {platoon.Name} 정지");
        }

        public void Tick()
        {
            ElapsedSeconds += TickSeconds;
            UpdateMovement();
            UpdateDetection();
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

            perMinute = Math.Min(Math.Max(perMinute, 0.0), 0.999);
            return 1.0 - Math.Pow(1.0 - perMinute, TickSeconds / 60.0);
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

        private void UpdateMovement()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (!platoon.IsMoving)
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
                    if (observer.Side != side)
                    {
                        continue;
                    }

                    foreach (Platoon target in _platoons)
                    {
                        if (target.Side == side || _observedThisTick.ContainsKey(target.Id))
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

        private float GetMoveSpeed(Platoon platoon)
        {
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

            return speed;
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

        private void Log(Side side, string text)
        {
            _events.Add(new SimEvent(ElapsedSeconds, side, text));
        }

        private static int RoundDistance(float distance) => (int)(Math.Round(distance / 10.0) * 10);

        private static string MoveModeName(MoveMode moveMode)
        {
            switch (moveMode)
            {
                case MoveMode.Fast:
                    return "신속";
                case MoveMode.Stealth:
                    return "은밀";
                default:
                    return "일반";
            }
        }
    }
}
