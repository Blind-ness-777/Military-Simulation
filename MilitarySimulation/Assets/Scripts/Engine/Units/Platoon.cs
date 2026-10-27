using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 소대. 실제 상태(엔진만 아는 진실)를 담는다.
    /// 상태 변경은 Simulation을 통해서만 일어나도록 setter를 internal로 둔다.
    /// </summary>
    public class Platoon
    {
        private readonly List<Waypoint> _waypoints = new List<Waypoint>();
        private readonly List<Vec2> _trail = new List<Vec2>();
        private readonly HashSet<int> _receivedSignals = new HashSet<int>();
        private readonly HashSet<int> _knownContacts = new HashSet<int>();
        private readonly HashSet<int> _knownArrivals = new HashSet<int>();

        internal Platoon(
            int id,
            string name,
            Side side,
            Vec2 position,
            float facingDegrees,
            int strength,
            float morale,
            PlatoonRole role,
            UnitType unitType)
        {
            Id = id;
            Name = name;
            Side = side;
            UnitType = unitType;
            CommsEquipment = unitType.DefaultCommsEquipment;
            Position = position;
            FacingDegrees = facingDegrees;
            SegmentStart = position;
            InitialStrength = strength;
            Effective = strength;
            Morale = morale;
            Role = role;
            _trail.Add(position);
        }

        public int Id { get; }
        public string Name { get; }
        public Side Side { get; }
        public PlatoonRole Role { get; }

        /// <summary>병과. 이동·관측·화력·통신 장비 수치를 여기서 읽는다.</summary>
        public UnitType UnitType { get; }
        public bool IsHeadquarters => Role == PlatoonRole.Headquarters;
        public Vec2 Position { get; internal set; }

        /// <summary>마지막으로 향한 방향(방위각). 정지해도 유지된다.</summary>
        public float FacingDegrees { get; internal set; }

        public IReadOnlyList<Waypoint> Waypoints => _waypoints;

        /// <summary>남은 경로가 있고 대기 중이 아닌지. 대기 지점에서 조건을 기다리는 동안은 정지로 판정한다.</summary>
        public bool IsMoving => _waypoints.Count > 0 && ActiveWait == null;

        /// <summary>대기 지점에서 기다리는 조건. null이면 대기 중이 아니다. 남은 경로(Waypoints)는 대기가 풀린 뒤 이어서 간다.</summary>
        public WaitCondition ActiveWait { get; private set; }

        public bool IsWaiting => ActiveWait != null;

        /// <summary>지금 대기가 풀리면 바꿀 사격 방침(대기 지점 웨이포인트의 속성).</summary>
        internal FirePolicy? FirePolicyOnRelease { get; private set; }

        /// <summary>
        /// 이 소대가 알고 있는 H시(경과 초). H시 지정·변경은 통신을 타므로, 두절된 소대는 예전 값을 갖고 있을 수 있다.
        /// 새 계획을 받아도 지워지지 않는다(작전 시각은 계획과 따로 맞춰 둔 것).
        /// </summary>
        public int? KnownHHourSeconds { get; internal set; }

        /// <summary>현재 구간의 이동 방식. 이동 중이 아니면 의미가 없다.</summary>
        public MoveMode CurrentMoveMode => IsMoving ? _waypoints[0].MoveMode : MoveMode.Normal;

        /// <summary>현재 구간의 접촉 시 행동. 이동 중이 아니면 의미가 없다.</summary>
        public ContactBehavior CurrentContactBehavior => IsMoving ? _waypoints[0].ContactBehavior : ContactBehavior.TakeCover;

        /// <summary>현재 구간의 출발 지점. "후퇴" 행동이 되돌아갈 곳이다.</summary>
        public Vec2 SegmentStart { get; internal set; }

        public bool IsWatching { get; private set; }
        public float WatchBearingDegrees { get; private set; }
        public float WatchArcDegrees { get; private set; }

        // 인원. 매 틱 끝에 Effective + Wounded + Killed == InitialStrength 를 검산한다.
        public int InitialStrength { get; }
        public int Effective { get; internal set; }
        public int Wounded { get; internal set; }
        public int Killed { get; internal set; }
        public bool IsDestroyed => Effective <= 0;

        /// <summary>사기(0~100). 일정 이하로 떨어지면 붕괴한다.</summary>
        public float Morale { get; internal set; }

        /// <summary>제압(0~100). 사격을 받으면 오르고, 받지 않으면 내려간다.</summary>
        public float Suppression { get; internal set; }

        /// <summary>제압이 심해 움직이지 못하는 상태.</summary>
        public bool IsPinned { get; internal set; }

        /// <summary>사기 붕괴. 사격하지 않고 명령도 받지 않는다. 붕괴 순간 적 반대 방향으로 후퇴한다.</summary>
        public bool IsBroken { get; internal set; }

        public FirePolicy FirePolicy { get; internal set; } = FirePolicy.Free;

        /// <summary>현재 사격 중인 실제 표적의 Id. 0이면 사격하지 않음.</summary>
        public int CurrentTargetId { get; internal set; }

        public bool IsFiring => CurrentTargetId != 0;

        /// <summary>마지막으로 사격을 받은 시각(경과 초).</summary>
        public int LastFiredUponSeconds { get; internal set; } = int.MinValue / 2;

        /// <summary>직전 틱에 접촉 상태였는지. 접촉 시 행동은 접촉이 "시작될 때" 한 번만 발동한다.</summary>
        internal bool WasInContact { get; set; }

        /// <summary>
        /// 가진 통신 수단. 처음 값은 병과의 DefaultCommsEquipment. 본부가 통신병을 잃으면 None이 된다
        /// (통신 장비를 통신병이 운용한다고 본다).
        /// </summary>
        public CommsEquipment CommsEquipment { get; internal set; }

        /// <summary>본부와의 통신 상태. 본부가 없는 진영은 항상 Normal.</summary>
        public CommsState Comms { get; internal set; } = CommsState.Normal;

        /// <summary>통신이 두절된 시각(경과 초). 두절 상태가 아니면 의미가 없다.</summary>
        internal int CommsCutSinceSeconds { get; set; }

        /// <summary>연락 두절이 길어져 스스로 단순 판단(고수·후퇴·철수)으로 움직이는 중인지.</summary>
        public bool IsSelfDirected { get; internal set; }

        /// <summary>자체 판단 모드의 진행 단계. 0 고수, 1 후퇴함, 2 철수 중.</summary>
        internal int SelfDirectedStage { get; set; }

        /// <summary>지나온 경로(일정 간격으로 기록). 자체 판단 모드의 후퇴·철수가 이 길을 되짚는다.</summary>
        public IReadOnlyList<Vec2> Trail => _trail;

        /// <summary>
        /// 이 소대가 직접 본 적(실제 부대 Id → 소대 인지). 통신이 끊겨도 소대는 자기가 본 것으로 사격·후퇴한다.
        /// 순회 순서에 의존하는 처리(로그 등)는 하지 않는다.
        /// </summary>
        internal Dictionary<int, LocalContact> KnownEnemies { get; } = new Dictionary<int, LocalContact>();

        internal void RecordTrail(float spacing)
        {
            if (Vec2.Distance(_trail[_trail.Count - 1], Position) >= spacing)
            {
                _trail.Add(Position);
            }
        }

        /// <summary>
        /// 되짚어 간 구간을 경로 기록에서 잘라낸다. 앞쪽 keepCount개만 남기고 endPoint를 마지막에 붙인다.
        /// 이렇게 해야 다음에 되짚을 때 방금 물러난 길을 따라 다시 앞으로 가지 않는다.
        /// </summary>
        internal void TruncateTrail(int keepCount, Vec2 endPoint)
        {
            if (keepCount < _trail.Count)
            {
                _trail.RemoveRange(keepCount, _trail.Count - keepCount);
            }

            if (_trail.Count == 0 || Vec2.Distance(_trail[_trail.Count - 1], endPoint) > 0.01f)
            {
                _trail.Add(endPoint);
            }
        }

        /// <summary>경계 중이고, 주어진 방위가 경계 부채꼴 안에 있는지.</summary>
        public bool IsInWatchArc(float bearingDegrees)
        {
            return IsWatching && Bearing.Difference(WatchBearingDegrees, bearingDegrees) <= WatchArcDegrees * 0.5f;
        }

        internal void SetPath(IEnumerable<Waypoint> waypoints, bool append)
        {
            if (!append)
            {
                ClearPlan();
            }

            if (!append || _waypoints.Count == 0)
            {
                _waypoints.Clear();
                SegmentStart = Position;
            }

            _waypoints.AddRange(waypoints);
            IsWatching = false;
        }

        internal void SetWatch(float bearingDegrees, float arcDegrees)
        {
            ClearPlan();
            _waypoints.Clear();
            IsWatching = true;
            WatchBearingDegrees = Bearing.Normalize(bearingDegrees);
            WatchArcDegrees = arcDegrees;
            FacingDegrees = WatchBearingDegrees;
        }

        internal void Hold()
        {
            ClearPlan();
            _waypoints.Clear();
            IsWatching = false;
        }

        internal void RemoveReachedWaypoint()
        {
            SegmentStart = _waypoints[0].Position;
            _waypoints.RemoveAt(0);
        }

        /// <summary>대기 지점에 도착해 대기를 시작한다. 남은 경로는 그대로 두고, 대기가 풀리면 이어서 간다.</summary>
        internal void BeginWait(Waypoint reached)
        {
            ActiveWait = reached.Wait;
            FirePolicyOnRelease = reached.FirePolicyOnRelease;
        }

        internal void EndWait()
        {
            ActiveWait = null;
            FirePolicyOnRelease = null;
        }

        internal void ReceiveSignal(int number) => _receivedSignals.Add(number);
        internal bool HasSignal(int number) => _receivedSignals.Contains(number);
        internal void ConsumeSignal(int number) => _receivedSignals.Remove(number);

        internal void LearnPlanEvent(int subjectId, PlanEventKind kind) => GetKnownEvents(kind).Add(subjectId);
        internal bool KnowsPlanEvent(int subjectId, PlanEventKind kind) => GetKnownEvents(kind).Contains(subjectId);
        internal void ConsumePlanEvent(int subjectId, PlanEventKind kind) => GetKnownEvents(kind).Remove(subjectId);

        private HashSet<int> GetKnownEvents(PlanEventKind kind)
        {
            return kind == PlanEventKind.Arrival ? _knownArrivals : _knownContacts;
        }

        /// <summary>
        /// 새 명령(새 경로·경계·정지)을 받으면 대기와 이전 계획에서 받은 신호·통지를 잊는다.
        /// "신호는 계획을 받은 뒤에 온 것만 유효"하게 해서, 예전 신호에 새 계획이 바로 풀려 버리지 않게 한다.
        /// </summary>
        private void ClearPlan()
        {
            EndWait();
            _receivedSignals.Clear();
            _knownContacts.Clear();
            _knownArrivals.Clear();
        }
    }
}
