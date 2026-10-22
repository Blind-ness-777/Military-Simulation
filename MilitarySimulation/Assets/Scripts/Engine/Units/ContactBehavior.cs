namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 이동 구간에서 접촉(사격을 하거나 받기 시작)했을 때의 즉시 반응.
    /// 플레이어 명령이 도착하기 전에 소대가 스스로 하는 행동이다(기획서 4.3, 4.7의 "두 층의 지휘").
    /// </summary>
    public enum ContactBehavior
    {
        /// <summary>그 자리에서 멈추고 엄폐한 뒤 응사한다. 남은 경로는 취소된다.</summary>
        TakeCover,

        /// <summary>멈추지 않고 계속 이동하며 응사한다.</summary>
        PushThrough,

        /// <summary>현재 구간의 출발 지점으로 신속하게 되돌아간다.</summary>
        FallBack
    }
}
