namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 경로의 한 지점. MoveMode와 ContactBehavior는 이 지점까지 가는 구간에 적용된다.
    /// 도착 시 행동 같은 구간 속성은 이후 단계에서 추가한다.
    /// </summary>
    public readonly struct Waypoint
    {
        public readonly Vec2 Position;
        public readonly MoveMode MoveMode;
        public readonly ContactBehavior ContactBehavior;

        public Waypoint(Vec2 position, MoveMode moveMode, ContactBehavior contactBehavior)
        {
            Position = position;
            MoveMode = moveMode;
            ContactBehavior = contactBehavior;
        }
    }
}
