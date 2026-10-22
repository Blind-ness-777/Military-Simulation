namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 사건 로그 한 줄. Side는 이 사건을 "아는" 진영이다.
    /// 플레이어 화면은 자기 진영의 사건만 보여주고, 전체 로그는 AAR·디버그용이다.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly int ElapsedSeconds;
        public readonly Side Side;
        public readonly string Text;

        public SimEvent(int elapsedSeconds, Side side, string text)
        {
            ElapsedSeconds = elapsedSeconds;
            Side = side;
            Text = text;
        }
    }
}
