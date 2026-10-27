using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 적 지휘관의 성향("무엇을 원하나"). 난이도(CommanderSkill)와 따로 둔다 — 난이도를 바꿔도 성향은 그대로다(기획서 6.6).
    /// 데모 1단계는 신중↔공격 한 축. 부성향(조건부 버릇)은 의외성 장치와 함께 2단계에서 추가한다.
    /// </summary>
    public class CommanderPersonality
    {
        /// <param name="aggression">공격성 0~1. 0.35면 신중 65 / 공격 35.</param>
        public CommanderPersonality(float aggression)
        {
            if (aggression < 0f || aggression > 1f) throw new ArgumentOutOfRangeException(nameof(aggression));
            Aggression = aggression;
        }

        /// <summary>기본 공격성(0~1). 판단 순간의 비율은 상황 보정으로 움직인다.</summary>
        public float Aggression { get; }
    }
}
