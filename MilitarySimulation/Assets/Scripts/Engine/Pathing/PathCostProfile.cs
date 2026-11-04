namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 격자 경로 탐색의 이동 방식별 비용 가중치. 한 칸을 지날 때의 비용 =
    /// 거리 × (시간 가중치 ÷ 지형 속도 배율 + 개활지 가중치(숲이 아니면) + 노출 가중치 × 노출)
    /// + 경사 가중치 × 높이 변화(m).
    /// 은밀 = 노출·개활지를 크게 피함, 일반 = 균형, 신속 = 거리·경사 위주(설계 2회차).
    /// </summary>
    public class PathCostProfile
    {
        /// <summary>걸리는 시간에 대한 가중치(1이면 평지 개활지 1m = 비용 1).</summary>
        public float TimeWeight = 1f;

        /// <summary>오르내린 높이 1m당 비용.</summary>
        public float SlopeWeight = 1f;

        /// <summary>노출 1(확실한 관측자 하나에게 보임)인 칸 1m당 비용.</summary>
        public float ExposureWeight = 1f;

        /// <summary>숲이 아닌 칸 1m당 추가 비용(엄폐 없는 땅을 꺼리는 정도).</summary>
        public float OpenGroundWeight;

        /// <summary>
        /// 지형별 속도 배율(보병 기준, UnitType의 숲·도로 배율과 같은 값). 경로 비용용이라 실제 이동 속도와는 별개로 둔다.
        /// 병과마다 다른 경로가 필요해지면(차량) 병과에서 읽도록 바꾼다.
        /// </summary>
        public float ForestSpeedFactor = 0.6f;
        public float RoadSpeedFactor = 1.2f;

        /// <summary>
        /// 경로 펴기 허용 폭(거리 기준 비율). 직선으로 바꾼 구간의 비용이 원래 경로 비용 + (구간 길이 × 1m당 최소 비용 × 이 값)
        /// 이하일 때만 편다. 은밀 경로가 펴지면서 개활지를 가로지르지 않게 하는 장치다.
        /// </summary>
        public float StraightenTolerance = 0.05f;

        /// <summary>평지 1m당 가능한 최소 비용(A* 휴리스틱용, 가장 빠른 지형 기준).</summary>
        internal float MinCostPerMeter
        {
            get
            {
                float fastest = RoadSpeedFactor > 1f ? RoadSpeedFactor : 1f;
                return TimeWeight / fastest;
            }
        }

        public static PathCostProfile CreateFast()
        {
            return new PathCostProfile { TimeWeight = 1f, SlopeWeight = 3f, ExposureWeight = 0.3f, OpenGroundWeight = 0f };
        }

        public static PathCostProfile CreateNormal()
        {
            return new PathCostProfile { TimeWeight = 1f, SlopeWeight = 1.5f, ExposureWeight = 2f, OpenGroundWeight = 0.2f };
        }

        public static PathCostProfile CreateStealth()
        {
            return new PathCostProfile { TimeWeight = 0.6f, SlopeWeight = 1f, ExposureWeight = 6f, OpenGroundWeight = 1f };
        }
    }
}
