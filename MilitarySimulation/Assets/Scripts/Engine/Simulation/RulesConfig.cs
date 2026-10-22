namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 규칙 수치. 코드가 아니라 이 값들을 바꿔서 밸런스를 맞춘다.
    /// 지금은 기본값을 코드에 두고, 시나리오 데이터 파일은 이후 단계에서 붙인다.
    /// 모든 값은 콘솔/테스트에서 조정할 초기 추정치다.
    /// </summary>
    public class RulesConfig
    {
        // 이동 속도 (m/s). 일반 1.1 m/s ≈ 시속 4km (기획서 9장 기준).
        public float FastSpeed = 1.8f;
        public float NormalSpeed = 1.1f;
        public float StealthSpeed = 0.5f;
        public float ForestSpeedMultiplier = 0.6f;
        public float RoadSpeedMultiplier = 1.2f;

        // 시야선 (m)
        public float EyeHeight = 1.8f;
        public float TargetHeight = 1.2f;
        public float ForestCanopyHeight = 12f;

        // 탐지. 분당 확률로 정의하고 틱 길이에 맞게 환산한다.
        public float MaxVisualRange = 800f;
        public float BaseDetectionPerMinute = 0.6f;

        // 표적 상태 보정 (기획서 4.4 "탐지되기")
        public float TargetFastMultiplier = 3f;
        public float TargetNormalMultiplier = 2f;
        public float TargetStealthMultiplier = 1.2f;
        public float TargetStationaryMultiplier = 1f;
        public float TargetInForestMultiplier = 0.4f;

        // 관측자 상태 보정 (기획서 4.4 "탐지하기")
        public float ObserverMovingMultiplier = 0.5f;
        public float ObserverStationaryMultiplier = 1f;
        public float ObserverWatchInArcMultiplier = 1.8f;
        public float ObserverWatchOutOfArcMultiplier = 0.5f;

        /// <summary>시야선이 끊긴 확인 접촉이 "소실"로 바뀌기까지의 시간(초).</summary>
        public int ContactLostSeconds = 30;
    }
}
