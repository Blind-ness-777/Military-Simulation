namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 경로의 한 지점. MoveMode와 ContactBehavior는 이 지점까지 가는 구간에 적용된다.
    /// Wait와 FirePolicyOnRelease는 이 지점에 도착했을 때 적용된다(계획 명령).
    /// </summary>
    public readonly struct Waypoint
    {
        public readonly Vec2 Position;
        public readonly MoveMode MoveMode;
        public readonly ContactBehavior ContactBehavior;

        /// <summary>도착하면 이 조건이 충족될 때까지 대기한다. null이면 대기 없이 지나간다.</summary>
        public readonly WaitCondition Wait;

        /// <summary>이 지점에서 대기가 풀리는 순간(대기가 없으면 도착하는 순간) 바꿀 사격 방침. null이면 유지.</summary>
        public readonly FirePolicy? FirePolicyOnRelease;

        public Waypoint(Vec2 position, MoveMode moveMode, ContactBehavior contactBehavior)
            : this(position, moveMode, contactBehavior, null, null)
        {
        }

        public Waypoint(
            Vec2 position,
            MoveMode moveMode,
            ContactBehavior contactBehavior,
            WaitCondition wait,
            FirePolicy? firePolicyOnRelease)
        {
            Position = position;
            MoveMode = moveMode;
            ContactBehavior = contactBehavior;
            Wait = wait != null && wait.IsEmpty ? null : wait;
            FirePolicyOnRelease = firePolicyOnRelease;
        }

        public bool HasWait => Wait != null;

        /// <summary>위치만 바꾼 복사본(명령 발신 때 맵 안으로 끌어들이는 용도).</summary>
        public Waypoint WithPosition(Vec2 position)
        {
            return new Waypoint(position, MoveMode, ContactBehavior, Wait, FirePolicyOnRelease);
        }
    }
}
