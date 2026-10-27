namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 적 지휘관의 실력("얼마나 잘하나") = 난이도 손잡이(기획서 6.6). 기본값은 "보통" 기준.
    /// 내다보기·협동·정찰 손잡이는 이후 단계에서 추가한다.
    /// </summary>
    public class CommanderSkill
    {
        /// <summary>후보 점수에 더하는 무작위의 최대 폭(±). 클수록 가끔 최선이 아닌 행동을 고른다.</summary>
        public float DecisionNoise { get; set; } = 8f;

        /// <summary>결정 지점이 생긴 뒤 판단을 내리기까지 걸리는 시간(초). 명령의 통신 지연과는 별개다.</summary>
        public int DecisionDelaySeconds { get; set; } = 10;

        /// <summary>사건이 없어도 상황을 다시 점검하는 간격(초).</summary>
        public int ReviewIntervalSeconds { get; set; } = 60;

        /// <summary>직전에 고른 것과 같은 후보에 더하는 점수. 판단이 쉽게 뒤집히지 않게 한다(기획서 6.3).</summary>
        public float CommitmentBonus { get; set; } = 15f;
    }
}
