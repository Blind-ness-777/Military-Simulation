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

        internal Platoon(int id, string name, Side side, Vec2 position, float facingDegrees)
        {
            Id = id;
            Name = name;
            Side = side;
            Position = position;
            FacingDegrees = facingDegrees;
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

        public bool IsWatching { get; private set; }
        public float WatchBearingDegrees { get; private set; }
        public float WatchArcDegrees { get; private set; }

        /// <summary>경계 중이고, 주어진 방위가 경계 부채꼴 안에 있는지.</summary>
        public bool IsInWatchArc(float bearingDegrees)
        {
            return IsWatching && Bearing.Difference(WatchBearingDegrees, bearingDegrees) <= WatchArcDegrees * 0.5f;
        }

        internal void SetPath(IEnumerable<Waypoint> waypoints, bool append)
        {
            if (!append)
            {
                _waypoints.Clear();
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
            _waypoints.RemoveAt(0);
        }
    }
}
