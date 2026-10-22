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

        internal Platoon(int id, string name, Side side, Vec2 position, float facingDegrees, int strength, float morale)
        {
            Id = id;
            Name = name;
            Side = side;
            Position = position;
            FacingDegrees = facingDegrees;
            SegmentStart = position;
            InitialStrength = strength;
            Effective = strength;
            Morale = morale;
        }

        public int Id { get; }
        public string Name { get; }
        public Side Side { get; }
        public Vec2 Position { get; internal set; }

        /// <summary>마지막으로 향한 방향(방위각). 정지해도 유지된다.</summary>
        public float FacingDegrees { get; internal set; }

        public IReadOnlyList<Waypoint> Waypoints => _waypoints;
        public bool IsMoving => _waypoints.Count > 0;

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

        /// <summary>경계 중이고, 주어진 방위가 경계 부채꼴 안에 있는지.</summary>
        public bool IsInWatchArc(float bearingDegrees)
        {
            return IsWatching && Bearing.Difference(WatchBearingDegrees, bearingDegrees) <= WatchArcDegrees * 0.5f;
        }

        internal void SetPath(IEnumerable<Waypoint> waypoints, bool append)
        {
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
            _waypoints.Clear();
            IsWatching = true;
            WatchBearingDegrees = Bearing.Normalize(bearingDegrees);
            WatchArcDegrees = arcDegrees;
            FacingDegrees = WatchBearingDegrees;
        }

        internal void Hold()
        {
            _waypoints.Clear();
            IsWatching = false;
        }

        internal void RemoveReachedWaypoint()
        {
            SegmentStart = _waypoints[0].Position;
            _waypoints.RemoveAt(0);
        }
    }
}
