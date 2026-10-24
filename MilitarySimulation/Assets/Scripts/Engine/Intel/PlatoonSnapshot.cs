using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 아군 소대가 본부에 보고한 시점의 상태. 통신이 끊긴 소대는 화면에 이 값(마지막 보고)으로 그린다.
    /// 보고 시점에 값을 복사해 두므로 이후 소대가 움직여도 바뀌지 않는다.
    /// </summary>
    public class PlatoonSnapshot
    {
        internal PlatoonSnapshot(Platoon platoon)
        {
            Position = platoon.Position;
            FacingDegrees = platoon.FacingDegrees;
            Effective = platoon.Effective;
            InitialStrength = platoon.InitialStrength;
            Morale = platoon.Morale;
            Suppression = platoon.Suppression;
            IsPinned = platoon.IsPinned;
            IsBroken = platoon.IsBroken;
            IsDestroyed = platoon.IsDestroyed;
            IsFiring = platoon.IsFiring;
            IsMoving = platoon.IsMoving;
            CurrentMoveMode = platoon.CurrentMoveMode;
            IsWatching = platoon.IsWatching;
            WatchBearingDegrees = platoon.WatchBearingDegrees;
            WatchArcDegrees = platoon.WatchArcDegrees;
            IsSelfDirected = platoon.IsSelfDirected;
            Waypoints = new List<Waypoint>(platoon.Waypoints);
        }

        public Vec2 Position { get; }
        public float FacingDegrees { get; }
        public int Effective { get; }
        public int InitialStrength { get; }
        public float Morale { get; }
        public float Suppression { get; }
        public bool IsPinned { get; }
        public bool IsBroken { get; }
        public bool IsDestroyed { get; }
        public bool IsFiring { get; }
        public bool IsMoving { get; }
        public MoveMode CurrentMoveMode { get; }
        public bool IsWatching { get; }
        public float WatchBearingDegrees { get; }
        public float WatchArcDegrees { get; }
        public bool IsSelfDirected { get; }

        /// <summary>보고 시점에 수행 중이던 경로. 플레이어가 내린 명령 경로와 같다(자체 판단 중이 아니라면).</summary>
        public IReadOnlyList<Waypoint> Waypoints { get; }
    }

    /// <summary>본부 상황판에 있는 아군 소대의 마지막 보고.</summary>
    public class FriendlyReport
    {
        internal FriendlyReport(PlatoonSnapshot snapshot, int reportedAtSeconds)
        {
            Snapshot = snapshot;
            ReportedAtSeconds = reportedAtSeconds;
        }

        public PlatoonSnapshot Snapshot { get; }

        /// <summary>이 상태가 관측된 시각(경과 초). 보고가 늦게 도착해도 관측 시각을 유지한다.</summary>
        public int ReportedAtSeconds { get; }
    }
}
