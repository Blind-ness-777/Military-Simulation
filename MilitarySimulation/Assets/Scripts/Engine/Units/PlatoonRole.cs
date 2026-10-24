namespace MilitarySimulation.Engine
{
    public enum PlatoonRole
    {
        /// <summary>일반 전투 부대.</summary>
        Line,

        /// <summary>
        /// 본부 분대. 진영당 하나. 플레이어(또는 적 지휘관)가 이 부대에 있다고 본다.
        /// 본부가 있는 진영은 다른 부대에 내리는 명령이 통신을 거쳐 전달된다.
        /// </summary>
        Headquarters
    }
}
