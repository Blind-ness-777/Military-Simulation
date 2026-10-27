namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 적 지휘관의 판단 기록 한 건. 디버그 로그, 테스트, 이후 AAR("적장은 이때 왜 그랬나")의 재료.
    /// </summary>
    public class CommanderDecision
    {
        internal CommanderDecision(
            int elapsedSeconds,
            string trigger,
            CommanderAction action,
            DefensivePosition position,
            float aggression,
            string candidates)
        {
            ElapsedSeconds = elapsedSeconds;
            Trigger = trigger;
            Action = action;
            Position = position;
            Aggression = aggression;
            Candidates = candidates;
        }

        public int ElapsedSeconds { get; }

        /// <summary>판단을 부른 결정 지점(예: "새 접촉", "1진지 접근", "정기 점검").</summary>
        public string Trigger { get; }

        public CommanderAction Action { get; }

        /// <summary>행동 대상 진지.</summary>
        public DefensivePosition Position { get; }

        /// <summary>상황 보정까지 반영한 그 순간의 공격성(0~1).</summary>
        public float Aggression { get; }

        /// <summary>후보와 최종 점수 요약(예: "고수 1진지 59 / 증원 1진지 48").</summary>
        public string Candidates { get; }
    }
}
