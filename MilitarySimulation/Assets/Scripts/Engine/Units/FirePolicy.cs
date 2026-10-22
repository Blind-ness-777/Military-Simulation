namespace MilitarySimulation.Engine
{
    /// <summary>사격 방침(기획서 4.2).</summary>
    public enum FirePolicy
    {
        /// <summary>사거리 안의 확인된 적에게 사격한다.</summary>
        Free,

        /// <summary>근거리(RulesConfig.CloseFireRange) 안의 적에게만 사격한다.</summary>
        CloseOnly,

        /// <summary>사격하지 않는다. 사격을 받아도 응사하지 않는다(매복·은밀 정찰용).</summary>
        HoldFire
    }
}
