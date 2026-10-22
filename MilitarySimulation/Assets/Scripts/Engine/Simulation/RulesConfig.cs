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

        /// <summary>사격 중인 부대는 총성과 섬광으로 위치가 드러난다.</summary>
        public float TargetFiringMultiplier = 3f;

        // 관측자 상태 보정 (기획서 4.4 "탐지하기")
        public float ObserverMovingMultiplier = 0.5f;
        public float ObserverStationaryMultiplier = 1f;
        public float ObserverWatchInArcMultiplier = 1.8f;
        public float ObserverWatchOutOfArcMultiplier = 0.5f;

        /// <summary>시야선이 끊긴 확인 접촉이 "소실"로 바뀌기까지의 시간(초).</summary>
        public int ContactLostSeconds = 30;

        // 교전: 사거리 (m)
        public float FireRange = 400f;
        public float CloseFireRange = 150f;

        /// <summary>
        /// 사수 1명이 분당 적 1명을 무력화할 확률(사거리 0, 개활지, 정지 표적 보정 1 기준).
        /// 사거리에 따라 선형으로 줄어든다.
        /// </summary>
        public float BaseCasualtyPerShooterPerMinute = 0.12f;

        // 교전: 표적 노출 보정 (움직이는 표적이 더 잘 맞는다)
        public float TargetExposureFast = 1.5f;
        public float TargetExposureNormal = 1.3f;
        public float TargetExposureStealth = 0.9f;
        public float TargetExposureStationary = 0.7f;
        public float TargetExposureInForest = 0.5f;

        // 교전: 사수 상태 보정 (기획서 4.4 "사격 정확도")
        public float ShooterMovingMultiplier = 0.3f;
        public float ShooterWatchInArcMultiplier = 1.2f;

        /// <summary>사상자 중 전사 비율. 나머지는 부상(전투 이탈).</summary>
        public float KilledRatio = 0.3f;

        // 제압 (0~100)
        public float SuppressionPerShooterPerMinute = 6f;
        public float SuppressionPerCasualty = 8f;
        public float SuppressionDecayPerSecond = 1.5f;

        /// <summary>제압 100일 때 사격 인원이 줄어드는 비율.</summary>
        public float SuppressionFireReduction = 0.7f;

        /// <summary>제압 100일 때 이동 속도가 줄어드는 비율.</summary>
        public float SuppressionSpeedReduction = 0.7f;

        /// <summary>이 값 이상이면 움직이지 못한다(제압됨).</summary>
        public float PinnedSuppression = 80f;

        // 사기 (0~100)
        public float InitialMorale = 100f;
        public float MoraleLossPerCasualty = 3f;
        public float MoraleLossPerSecondPinned = 0.2f;
        public float MoraleRecoveryPerSecond = 0.05f;

        /// <summary>사기 회복은 제압이 이 값 미만이고 사격을 받지 않을 때만 일어난다.</summary>
        public float MoraleRecoverySuppressionLimit = 20f;

        public float BreakMorale = 25f;
        public float RallyMorale = 45f;

        /// <summary>사기 붕괴 시 적 반대 방향으로 물러나는 거리(m).</summary>
        public float BrokenRetreatDistance = 150f;

        /// <summary>마지막 피격 후 이 시간(초) 동안은 접촉 상태로 본다.</summary>
        public int ContactMemorySeconds = 5;
    }
}
