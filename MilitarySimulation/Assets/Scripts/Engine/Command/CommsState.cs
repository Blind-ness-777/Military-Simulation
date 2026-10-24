namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 본부와 부대 사이의 통신 상태. 현대전 기준으로 평시(정상)는 지연이 거의 없고,
    /// 지형 음영·적 재밍·통신 수단 손실이 있을 때만 저하되거나 끊긴다.
    /// </summary>
    public enum CommsState
    {
        /// <summary>본부와 목소리가 닿는 거리. 무전이 없어도 지휘 가능.</summary>
        Direct,

        Normal,

        /// <summary>낮은 지형 음영, 약한 재밍. 명령이 늦게 도착한다.</summary>
        Degraded,

        /// <summary>명령이 전달되지 않는다. 발신한 명령은 회복될 때까지 보류된다.</summary>
        Cut
    }
}
