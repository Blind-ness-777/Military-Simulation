namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 병과 데이터. 부대마다 달라지는 수치(이동·관측·화력·통신 장비)를 담는다.
    /// 모든 부대에 똑같이 적용되는 판정 규칙(탐지 보정, 제압·사기, 통신 판정)은 RulesConfig에 있다.
    /// 소대 하나는 병과 하나로 본다. 소대 안의 세부 구성(의무병·저격병 등)은 아직 다루지 않는다.
    /// RulesConfig처럼 값을 직접 바꿔 조정한다. 같은 인스턴스를 쓰는 부대는 모두 함께 바뀐다.
    /// </summary>
    public class UnitType
    {
        public UnitType(string name)
        {
            Name = name;
        }

        /// <summary>표시용 병과 이름.</summary>
        public string Name { get; }

        /// <summary>AddPlatoon에서 인원을 따로 주지 않았을 때의 인원.</summary>
        public int DefaultStrength = 30;

        public CommsEquipment DefaultCommsEquipment = CommsEquipment.LineOfSightRadio;

        // 이동 속도 (m/s). 일반 1.1 m/s ≈ 시속 4km (기획서 9장 기준).
        public float FastSpeed = 1.8f;
        public float NormalSpeed = 1.1f;
        public float StealthSpeed = 0.5f;

        // 지형별 속도 보정. 병과마다 다르다(차량은 숲에서 훨씬 느리거나 못 들어간다).
        public float ForestSpeedMultiplier = 0.6f;
        public float RoadSpeedMultiplier = 1.2f;

        // 시야선 (m)
        /// <summary>관측할 때의 눈높이.</summary>
        public float EyeHeight = 1.8f;

        /// <summary>관측당할 때의 높이. 높을수록 지형 뒤에 숨기 어렵다.</summary>
        public float TargetHeight = 1.2f;

        /// <summary>센서(육안·광학)로 적을 발견할 수 있는 최대 거리.</summary>
        public float MaxVisualRange = 800f;

        /// <summary>무전·위성통신 안테나 높이. 지형 음영과 하늘 시야 판정에 쓴다.</summary>
        public float AntennaHeight = 2f;

        /// <summary>소대 전체 화력을 하나로 본 화기 프로필.</summary>
        public WeaponProfile Weapon = new WeaponProfile();
    }

    /// <summary>
    /// 소대 화기 프로필. 소총·분대 기관총 등을 합쳐 하나로 본 값이다.
    /// 사거리나 표적이 다른 화기(기관총 분리, 대전차화기, 박격포)를 넣을 때 목록으로 늘린다.
    /// </summary>
    public class WeaponProfile
    {
        /// <summary>최대 사거리(m).</summary>
        public float FireRange = 400f;

        /// <summary>사격 방침 "근거리만"일 때의 사거리(m).</summary>
        public float CloseFireRange = 150f;

        /// <summary>
        /// 사수 1명이 분당 적 1명을 무력화할 확률(사거리 0, 개활지, 정지 표적 보정 1 기준).
        /// 사거리에 따라 선형으로 줄어든다.
        /// </summary>
        public float CasualtyPerShooterPerMinute = 0.12f;
    }
}
