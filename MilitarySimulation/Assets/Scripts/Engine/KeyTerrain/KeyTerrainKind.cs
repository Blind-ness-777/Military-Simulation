namespace MilitarySimulation.Engine
{
    /// <summary>요지의 종류(시나리오 데이터). 지휘관이 요지를 어떤 역할에 쓸 수 있는지 가늠하는 데 쓴다.</summary>
    public enum KeyTerrainKind
    {
        /// <summary>고지. 주변을 내려다본다.</summary>
        Hill,

        /// <summary>숲 가장자리. 숨은 채 바깥을 본다.</summary>
        ForestEdge,

        /// <summary>길목. 지나갈 수밖에 없는 좁은 곳.</summary>
        Chokepoint,

        /// <summary>대기 지점 후보. 모여서 기다리기 좋은 가려진 곳.</summary>
        StagingArea,

        /// <summary>사격 진지 후보. 목표나 접근로를 쏠 수 있는 곳.</summary>
        FirePosition
    }
}
