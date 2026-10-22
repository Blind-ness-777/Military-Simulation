namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 접촉 정보의 확신 단계.
    /// 기획서 5.4의 "추정", "소리만"은 보고·청음 시스템을 넣을 때 추가한다.
    /// </summary>
    public enum ContactConfidence
    {
        /// <summary>현재 시야선이 있고 위치가 확인됨.</summary>
        Confirmed,

        /// <summary>놓침. 마지막 확인 위치와 시각만 남음.</summary>
        Lost
    }
}
