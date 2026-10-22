namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 경로의 한 지점. MoveMode는 이 지점까지 가는 구간에 적용된다.
    /// 접촉 시 / 도착 시 같은 구간 속성은 다음 단계에서 추가한다.
    /// </summary>
    public readonly struct Waypoint
    {
        public readonly Vec2 Position;
        public readonly MoveMode MoveMode;

        public Waypoint(Vec2 position, MoveMode moveMode)
        {
            Position = position;
            MoveMode = moveMode;
        }
    }
}
