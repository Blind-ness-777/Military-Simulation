using System;
using System.Collections.Generic;
using System.Text;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 규칙 기반 적 지휘관(데모 1단계: 방어하는 적). 기획서 6장.
    ///
    /// 안개 규칙(반칙 금지):
    /// - 적 정보는 자기 진영 상황판(SideIntel)의 접촉만 쓴다. Contact.TargetId로 실제 부대를 찾아보지 않는다.
    /// - 자기 소대 상태는 플레이어 화면과 같은 규칙으로 안다. 실시간 보고 중이면 현재 상태, 아니면 마지막 보고.
    /// - 명령은 플레이어와 같은 공개 명령(OrderMove·OrderWatch)으로 내려서 통신 지연을 똑같이 탄다.
    /// - 본부가 없거나 전멸하면 아무것도 하지 않는다(남은 소대는 연락 두절 규칙대로 움직인다).
    ///
    /// 판단 흐름: 결정 지점 감지 → 판단 소요 시간 대기 → 진지별 상황 평가 → 후보(행동 × 진지) 점수
    /// → 가장 높은 후보 실행 → 기동 추적(도착하면 경계로 마무리).
    /// 점수 = (1 - 공격성) × 신중 점수 + 공격성 × 공격 점수 + 무작위 + (직전과 같은 후보면 유지 보너스).
    /// 점수 공식의 숫자는 QA 튜닝을 시작할 때 데이터로 뺀다. 그전까지는 아래 상수로 둔다.
    /// </summary>
    public class EnemyCommander
    {
        private const int NoDecision = -1;
        private const string ReviewTrigger = "정기 점검";

        /// <summary>이 시간 안에 본 접촉만 위협으로 센다(놓친 접촉 포함).</summary>
        private const int ThreatMemorySeconds = 120;

        /// <summary>진지 위협 반경에 이만큼 더한 거리 안의 아군 소대를 "근처 아군"으로 센다.</summary>
        private const float NearbyMargin = 150f;

        /// <summary>목적지와 이 거리 안이면 도착으로 본다.</summary>
        private const float ArrivalDistance = 15f;

        /// <summary>진지 경계 방향과 위협 방향이 이보다 차이 날 때만 경계 방향을 바꾸는 명령을 낸다.</summary>
        private const float RefaceThresholdDegrees = 30f;

        // 상황 보정(기획서 6.4). 적장 진영 정보 기준이라 틀릴 수 있다.
        private const float SuperiorityAggressionBonus = 0.10f;
        private const float InferiorityAggressionPenalty = 0.10f;
        private const float CasualtyAggressionPenalty = 0.15f;
        private const float CasualtyCautionStrength = 0.7f;

        /// <summary>소대 전투 가능 비율이 이 값 아래로 처음 떨어질 때 결정 지점이 된다.</summary>
        private static readonly float[] StrengthBandLimits = { 0.7f, 0.5f };

        private readonly DeterministicRandom _random;
        private readonly List<CommanderDecision> _decisions = new List<CommanderDecision>();
        private readonly List<Maneuver> _maneuvers = new List<Maneuver>();
        private readonly HashSet<DefensivePosition> _abandonedPositions = new HashSet<DefensivePosition>();
        private readonly Dictionary<DefensivePosition, int> _threatCounts = new Dictionary<DefensivePosition, int>();
        private readonly Dictionary<int, int> _strengthBands = new Dictionary<int, int>();
        private readonly HashSet<int> _knownBroken = new HashSet<int>();
        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly StringBuilder _textBuilder = new StringBuilder();

        private bool _started;
        private int _knownContactCount;
        private int _nextReviewSeconds;
        private int _decisionDueSeconds = NoDecision;
        private string _pendingTrigger;
        private DefensivePosition _reservePosition;
        private bool _hasLastChoice;
        private CommanderAction _lastAction;
        private DefensivePosition _lastPosition;

        /// <param name="seed">
        /// 지휘관 전용 난수 시드. 시뮬레이션 난수와 흐름을 나눠서, 지휘관이 굴린 무작위가 탐지·교전 판정 순서를 바꾸지 않게 한다.
        /// </param>
        public EnemyCommander(Side side, CommanderPersonality personality, CommanderSkill skill, DefensePlan plan, int seed)
        {
            Side = side;
            Personality = personality ?? throw new ArgumentNullException(nameof(personality));
            Skill = skill ?? throw new ArgumentNullException(nameof(skill));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _random = new DeterministicRandom(seed);
        }

        public Side Side { get; }
        public CommanderPersonality Personality { get; }
        public CommanderSkill Skill { get; }
        public DefensePlan Plan { get; }

        /// <summary>판단 기록(시간 순).</summary>
        public IReadOnlyList<CommanderDecision> Decisions => _decisions;

        /// <summary>예비대를 이미 투입했는지. 데모 1단계에서 예비대는 한 번만 투입한다.</summary>
        public bool IsReserveCommitted { get; private set; }

        /// <summary>포기(후퇴)한 진지인지.</summary>
        public bool IsAbandoned(DefensivePosition position) => _abandonedPositions.Contains(position);

        /// <summary>Simulation.Tick에서 보고 도착 직후 매 틱 호출된다.</summary>
        internal void Update(Simulation simulation)
        {
            if (!simulation.TryGetHeadquarters(Side, out Platoon headquarters) || headquarters.IsDestroyed)
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                _nextReviewSeconds = simulation.ElapsedSeconds + Skill.ReviewIntervalSeconds;
            }

            UpdateManeuvers(simulation);
            DetectDecisionPoints(simulation);

            if (_decisionDueSeconds != NoDecision && simulation.ElapsedSeconds >= _decisionDueSeconds)
            {
                string trigger = _pendingTrigger;
                _decisionDueSeconds = NoDecision;
                _pendingTrigger = null;
                _nextReviewSeconds = simulation.ElapsedSeconds + Skill.ReviewIntervalSeconds;
                Decide(simulation, trigger);
            }
        }

        /// <summary>본부가 아는 이 소대의 상태. 플레이어 화면과 같은 규칙: 실시간 보고 중이면 현재, 아니면 마지막 보고.</summary>
        private static PlatoonSnapshot GetKnownState(Simulation simulation, Platoon platoon)
        {
            if (simulation.IsReportingLive(platoon))
            {
                return new PlatoonSnapshot(platoon);
            }

            return simulation.TryGetLastReport(platoon, out FriendlyReport report) ? report.Snapshot : null;
        }

        private static bool IsCombatCapable(PlatoonSnapshot state)
        {
            return state != null && !state.IsDestroyed && !state.IsBroken;
        }

        private void DetectDecisionPoints(Simulation simulation)
        {
            SideIntel intel = simulation.GetIntel(Side);
            if (intel.Contacts.Count > _knownContactCount)
            {
                _knownContactCount = intel.Contacts.Count;
                RequestDecision(simulation, "새 접촉");
            }

            foreach (DefensivePosition position in Plan.Positions)
            {
                if (_abandonedPositions.Contains(position))
                {
                    continue;
                }

                int threats = CountThreats(simulation, position, out _);
                _threatCounts.TryGetValue(position, out int previous);
                _threatCounts[position] = threats;
                if (threats > previous)
                {
                    RequestDecision(simulation, $"{position.Name} 접근");
                }
            }

            foreach (Platoon platoon in GetPlanPlatoons())
            {
                PlatoonSnapshot state = GetKnownState(simulation, platoon);
                if (state == null)
                {
                    continue;
                }

                int band = GetStrengthBand(state);
                _strengthBands.TryGetValue(platoon.Id, out int previousBand);
                if (band > previousBand)
                {
                    _strengthBands[platoon.Id] = band;
                    RequestDecision(simulation, $"{platoon.Name} 피해");
                }

                if (state.IsBroken && _knownBroken.Add(platoon.Id))
                {
                    RequestDecision(simulation, $"{platoon.Name} 붕괴");
                }
                else if (!state.IsBroken)
                {
                    _knownBroken.Remove(platoon.Id);
                }
            }

            if (simulation.ElapsedSeconds >= _nextReviewSeconds)
            {
                _nextReviewSeconds = simulation.ElapsedSeconds + Skill.ReviewIntervalSeconds;
                RequestDecision(simulation, ReviewTrigger);
            }
        }

        /// <summary>판단 대기 중에 생긴 결정 지점은 합친다(처음 생긴 이유를 남긴다).</summary>
        private void RequestDecision(Simulation simulation, string trigger)
        {
            if (_decisionDueSeconds != NoDecision)
            {
                return;
            }

            _decisionDueSeconds = simulation.ElapsedSeconds + Skill.DecisionDelaySeconds;
            _pendingTrigger = trigger;
        }

        private static int GetStrengthBand(PlatoonSnapshot state)
        {
            float ratio = (float)state.Effective / state.InitialStrength;
            int band = 0;
            foreach (float limit in StrengthBandLimits)
            {
                if (ratio < limit)
                {
                    band++;
                }
            }

            return band;
        }

        private IEnumerable<Platoon> GetPlanPlatoons()
        {
            foreach (DefensivePosition position in Plan.Positions)
            {
                yield return position.Garrison;
            }

            if (Plan.Reserve != null)
            {
                yield return Plan.Reserve;
            }
        }

        /// <summary>진지 위협 반경 안에서 최근 본 접촉 수와, 진지 중심에서 가장 가까운 접촉 위치.</summary>
        private int CountThreats(Simulation simulation, DefensivePosition position, out Vec2 nearestThreat)
        {
            int count = 0;
            float nearestDistance = float.MaxValue;
            nearestThreat = position.Center;
            foreach (Contact contact in simulation.GetIntel(Side).Contacts)
            {
                if (simulation.ElapsedSeconds - contact.LastSeenSeconds > ThreatMemorySeconds)
                {
                    continue;
                }

                float distance = Vec2.Distance(contact.LastKnownPosition, position.Center);
                if (distance > position.ThreatRadius)
                {
                    continue;
                }

                count++;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestThreat = contact.LastKnownPosition;
                }
            }

            return count;
        }

        private int CountRecentContacts(Simulation simulation)
        {
            int count = 0;
            foreach (Contact contact in simulation.GetIntel(Side).Contacts)
            {
                if (simulation.ElapsedSeconds - contact.LastSeenSeconds <= ThreatMemorySeconds)
                {
                    count++;
                }
            }

            return count;
        }

        private void Decide(Simulation simulation, string trigger)
        {
            bool reserveAvailable = !IsReserveCommitted
                && Plan.Reserve != null
                && IsCombatCapable(GetKnownState(simulation, Plan.Reserve));
            float aggression = GetSituationalAggression(simulation);

            _candidates.Clear();
            foreach (DefensivePosition position in Plan.Positions)
            {
                if (_abandonedPositions.Contains(position))
                {
                    continue;
                }

                Assessment assessment = Assess(simulation, position);
                if (assessment.Threats > 0)
                {
                    AddCandidates(assessment, reserveAvailable);
                }
            }

            if (_candidates.Count == 0)
            {
                return;
            }

            int bestIndex = 0;
            for (int i = 0; i < _candidates.Count; i++)
            {
                Candidate candidate = _candidates[i];
                float score = (1f - aggression) * candidate.CautionScore + aggression * candidate.AggressionScore;
                score += (float)(_random.NextDouble() * 2.0 - 1.0) * Skill.DecisionNoise;
                if (_hasLastChoice && candidate.Action == _lastAction && candidate.Assessment.Position == _lastPosition)
                {
                    score += Skill.CommitmentBonus;
                }

                candidate.Score = score;
                _candidates[i] = candidate;
                if (score > _candidates[bestIndex].Score)
                {
                    bestIndex = i;
                }
            }

            Candidate best = _candidates[bestIndex];
            bool unchangedReview = trigger == ReviewTrigger
                && _hasLastChoice
                && best.Action == _lastAction
                && best.Assessment.Position == _lastPosition;
            _hasLastChoice = true;
            _lastAction = best.Action;
            _lastPosition = best.Assessment.Position;

            string candidateSummary = FormatCandidates();
            var decision = new CommanderDecision(
                simulation.ElapsedSeconds, trigger, best.Action, best.Assessment.Position, aggression, candidateSummary);
            _decisions.Add(decision);

            // 정기 점검에서 판단이 그대로면 로그는 남기지 않는다(판단 기록에는 남는다). 로그가 같은 줄로 덮이지 않게.
            if (!unchangedReview)
            {
                simulation.LogCommander(Side,
                    $"지휘관 판단({trigger}): {DisplayNames.Of(best.Action)} → {best.Assessment.Position.Name} [{candidateSummary}] (공격성 {aggression:0.00})");
            }

            Execute(simulation, best);

            // 고른 행동과 상관없이, 위협받는 다른 진지는 경계 방향을 위협 쪽으로 돌린다(기본 고수).
            foreach (Candidate candidate in _candidates)
            {
                Assessment other = candidate.Assessment;
                if (candidate.Action == CommanderAction.Hold
                    && other.Position != best.Assessment.Position
                    && !_abandonedPositions.Contains(other.Position))
                {
                    FaceThreat(simulation, other);
                }
            }
        }

        /// <summary>
        /// 기본 공격성에 상황 보정을 더한 그 순간의 공격성.
        /// 병력 우세·열세는 "싸울 수 있는 아군 소대 수 vs 최근 본 접촉 수"로 판단한다(적 규모는 모른다).
        /// </summary>
        private float GetSituationalAggression(Simulation simulation)
        {
            int friendly = 0;
            int effective = 0;
            int initial = 0;
            foreach (Platoon platoon in GetPlanPlatoons())
            {
                PlatoonSnapshot state = GetKnownState(simulation, platoon);
                if (state == null)
                {
                    continue;
                }

                effective += state.Effective;
                initial += state.InitialStrength;
                if (IsCombatCapable(state))
                {
                    friendly++;
                }
            }

            float aggression = Personality.Aggression;
            int contacts = CountRecentContacts(simulation);
            if (friendly > contacts)
            {
                aggression += SuperiorityAggressionBonus;
            }
            else if (friendly < contacts)
            {
                aggression -= InferiorityAggressionPenalty;
            }

            if (initial > 0 && (float)effective / initial < CasualtyCautionStrength)
            {
                aggression -= CasualtyAggressionPenalty;
            }

            return Math.Min(1f, Math.Max(0f, aggression));
        }

        private Assessment Assess(Simulation simulation, DefensivePosition position)
        {
            var assessment = new Assessment { Position = position };
            assessment.Threats = CountThreats(simulation, position, out assessment.NearestThreat);

            PlatoonSnapshot garrison = GetKnownState(simulation, position.Garrison);
            assessment.GarrisonState = garrison;
            assessment.GarrisonLost = garrison != null && !IsCombatCapable(garrison);
            assessment.Damage = garrison == null
                ? 0f
                : assessment.GarrisonLost ? 1f : 1f - (float)garrison.Effective / garrison.InitialStrength;

            // 예비대는 따로 센다(역습에 쓸 병력이므로 "이미 근처에 있는 아군"에 넣지 않는다).
            float nearbyRange = position.ThreatRadius + NearbyMargin;
            foreach (DefensivePosition other in Plan.Positions)
            {
                PlatoonSnapshot state = GetKnownState(simulation, other.Garrison);
                if (IsCombatCapable(state) && Vec2.Distance(state.Position, position.Center) <= nearbyRange)
                {
                    assessment.FriendlyNearby++;
                }
            }

            return assessment;
        }

        /// <summary>
        /// 후보와 신중·공격 점수(0~100 근처). 실행할 수 없거나 말이 안 되는 후보는 아예 넣지 않는다
        /// — 무작위가 커도 "명백히 나쁜 행동"이 뽑히지 않게(기획서 6.2).
        /// </summary>
        private void AddCandidates(Assessment assessment, bool reserveAvailable)
        {
            int threats = assessment.Threats;
            float damage = assessment.Damage;

            if (!assessment.GarrisonLost)
            {
                _candidates.Add(new Candidate(CommanderAction.Hold, assessment,
                    70f - 15f * (threats - 1) - 40f * damage,
                    40f - 20f * damage));

                _candidates.Add(new Candidate(CommanderAction.FallBack, assessment,
                    20f + 60f * damage + 10f * Math.Max(0, threats - assessment.FriendlyNearby),
                    10f + 30f * damage));
            }

            if (!reserveAvailable)
            {
                return;
            }

            _candidates.Add(new Candidate(CommanderAction.Reinforce, assessment,
                30f + 15f * threats - 20f * damage,
                45f + 10f * threats));

            float localRatio = (float)(assessment.FriendlyNearby + 1) / threats;
            if (localRatio > 1f)
            {
                _candidates.Add(new Candidate(CommanderAction.Counterattack, assessment,
                    15f,
                    45f + 30f * Math.Min(localRatio - 1f, 1.5f)));
            }
        }

        private string FormatCandidates()
        {
            _textBuilder.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                Candidate candidate = _candidates[i];
                if (i > 0)
                {
                    _textBuilder.Append(" / ");
                }

                _textBuilder.Append(DisplayNames.Of(candidate.Action))
                    .Append(' ')
                    .Append(candidate.Assessment.Position.Name)
                    .Append(' ')
                    .Append(Math.Round(candidate.Score).ToString("0"));
            }

            return _textBuilder.ToString();
        }

        private void Execute(Simulation simulation, Candidate candidate)
        {
            Assessment assessment = candidate.Assessment;
            DefensivePosition position = assessment.Position;
            float threatBearing = Bearing.Between(position.Center, assessment.NearestThreat);

            switch (candidate.Action)
            {
                case CommanderAction.Hold:
                    FaceThreat(simulation, assessment);
                    break;

                case CommanderAction.Reinforce:
                    CommitReserve(position);
                    StartManeuver(simulation, Plan.Reserve, position.Center, MoveMode.Normal, ContactBehavior.TakeCover,
                        threatBearing, position.WatchArcDegrees);
                    break;

                case CommanderAction.Counterattack:
                    CommitReserve(position);
                    StartManeuver(simulation, Plan.Reserve, assessment.NearestThreat, MoveMode.Normal, ContactBehavior.PushThrough,
                        threatBearing, position.WatchArcDegrees);
                    break;

                case CommanderAction.FallBack:
                    _abandonedPositions.Add(position);
                    float backBearing = Bearing.Between(position.FallbackPoint, position.Center);
                    StartManeuver(simulation, position.Garrison, position.FallbackPoint, MoveMode.Fast, ContactBehavior.PushThrough,
                        backBearing, position.WatchArcDegrees);

                    // 이 진지로 보낸 예비대도 함께 뺀다.
                    if (_reservePosition == position && IsCombatCapable(GetKnownState(simulation, Plan.Reserve)))
                    {
                        StartManeuver(simulation, Plan.Reserve, position.FallbackPoint, MoveMode.Fast, ContactBehavior.PushThrough,
                            backBearing, position.WatchArcDegrees);
                    }

                    break;
            }
        }

        private void CommitReserve(DefensivePosition position)
        {
            IsReserveCommitted = true;
            _reservePosition = position;
        }

        /// <summary>진지 소대의 경계 방향을 가장 가까운 위협 쪽으로. 차이가 작으면 명령을 내지 않는다.</summary>
        private void FaceThreat(Simulation simulation, Assessment assessment)
        {
            PlatoonSnapshot state = assessment.GarrisonState;
            if (!IsCombatCapable(state) || state.IsMoving || IsManeuvering(assessment.Position.Garrison))
            {
                return;
            }

            float bearing = Bearing.Between(assessment.Position.Center, assessment.NearestThreat);
            if (state.IsWatching && Bearing.Difference(state.WatchBearingDegrees, bearing) <= RefaceThresholdDegrees)
            {
                return;
            }

            simulation.OrderWatch(assessment.Position.Garrison, bearing, assessment.Position.WatchArcDegrees);
        }

        private bool IsManeuvering(Platoon platoon)
        {
            foreach (Maneuver maneuver in _maneuvers)
            {
                if (maneuver.Platoon == platoon)
                {
                    return true;
                }
            }

            return false;
        }

        private void StartManeuver(
            Simulation simulation,
            Platoon platoon,
            Vec2 destination,
            MoveMode moveMode,
            ContactBehavior contactBehavior,
            float arrivalWatchBearing,
            float arrivalWatchArc)
        {
            _maneuvers.RemoveAll(existing => existing.Platoon == platoon);
            simulation.OrderMove(platoon, new[] { destination }, moveMode, false, contactBehavior);
            _maneuvers.Add(new Maneuver
            {
                Platoon = platoon,
                Destination = destination,
                ArrivalWatchBearing = arrivalWatchBearing,
                ArrivalWatchArc = arrivalWatchArc
            });
        }

        /// <summary>
        /// 진행 중인 기동을 보고로 추적한다. 움직이다 멈췄거나(도착·접촉 후 엄폐) 목적지에 있으면 경계로 마무리하고 다시 판단한다.
        /// 명령이 아직 도착하지 않아 출발 전인 동안은 기다린다.
        /// </summary>
        private void UpdateManeuvers(Simulation simulation)
        {
            for (int i = _maneuvers.Count - 1; i >= 0; i--)
            {
                Maneuver maneuver = _maneuvers[i];
                PlatoonSnapshot state = GetKnownState(simulation, maneuver.Platoon);
                if (state == null)
                {
                    continue;
                }

                if (!IsCombatCapable(state))
                {
                    _maneuvers.RemoveAt(i);
                    continue;
                }

                if (state.IsMoving)
                {
                    maneuver.SawMoving = true;
                    continue;
                }

                bool arrived = Vec2.Distance(state.Position, maneuver.Destination) <= ArrivalDistance;
                if (!maneuver.SawMoving && !arrived)
                {
                    continue;
                }

                _maneuvers.RemoveAt(i);
                simulation.OrderWatch(maneuver.Platoon, maneuver.ArrivalWatchBearing, maneuver.ArrivalWatchArc);
                RequestDecision(simulation, $"{maneuver.Platoon.Name} 기동 완료");
            }
        }

        private sealed class Assessment
        {
            public DefensivePosition Position;
            public int Threats;
            public Vec2 NearestThreat;
            public PlatoonSnapshot GarrisonState;
            public bool GarrisonLost;

            /// <summary>진지 소대의 피해 비율(0~1). 전투 불능·붕괴면 1.</summary>
            public float Damage;

            /// <summary>진지 근처의 싸울 수 있는 진지 소대 수(자기 진지 포함, 예비대 제외).</summary>
            public int FriendlyNearby;
        }

        private struct Candidate
        {
            public readonly CommanderAction Action;
            public readonly Assessment Assessment;
            public readonly float CautionScore;
            public readonly float AggressionScore;
            public float Score;

            public Candidate(CommanderAction action, Assessment assessment, float cautionScore, float aggressionScore)
            {
                Action = action;
                Assessment = assessment;
                CautionScore = cautionScore;
                AggressionScore = aggressionScore;
                Score = 0f;
            }
        }

        private sealed class Maneuver
        {
            public Platoon Platoon;
            public Vec2 Destination;
            public float ArrivalWatchBearing;
            public float ArrivalWatchArc;

            /// <summary>명령이 도착해 움직이기 시작한 것을 보고로 확인했는지.</summary>
            public bool SawMoving;
        }
    }
}
