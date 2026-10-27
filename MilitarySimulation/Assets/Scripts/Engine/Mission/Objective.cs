namespace MilitarySimulation.Engine
{
    public enum ObjectiveKind
    {
        /// <summary>구역을 적 없이 HoldSeconds 동안 유지하면 완료.</summary>
        SeizeZone,

        /// <summary>지켜야 하는 구역. 적이 HoldSeconds 동안 확보하면 실패. 스스로 완료되지는 않는다.</summary>
        HoldZone,

        /// <summary>목표 반경 안에서 WorkSeconds 동안 작업(폭파 준비)하면 완료.</summary>
        DestroyTarget,

        /// <summary>살아 있는 아군 소대가 전부 구역 안에 들어오면 완료.</summary>
        Exfiltrate
    }

    public enum ObjectiveState
    {
        /// <summary>앞 단계가 끝나지 않아 아직 시작하지 않음.</summary>
        Waiting,

        Active,
        Complete,
        Failed
    }

    /// <summary>
    /// 임무 목표 하나(시나리오 데이터). 판정은 Simulation이 매 틱 한다(Simulation.Mission.cs).
    /// </summary>
    public class Objective
    {
        public Objective(ObjectiveKind kind, string name, Vec2 center, float radius)
        {
            Kind = kind;
            Name = name;
            Center = center;
            Radius = radius;
        }

        public ObjectiveKind Kind { get; }

        /// <summary>화면·로그에 쓰는 이름(예: "적 진지", "통신 시설").</summary>
        public string Name { get; }

        public Vec2 Center { get; }
        public float Radius { get; }

        /// <summary>확보·방어 판정에 필요한 연속 유지 시간(초).</summary>
        public int HoldSeconds { get; set; } = 60;

        /// <summary>폭파 작업에 필요한 시간(초).</summary>
        public int WorkSeconds { get; set; } = 90;

        public ObjectiveState State { get; internal set; } = ObjectiveState.Waiting;

        /// <summary>
        /// 확보·작업이 연속으로 이어진 시간(초). 끊기면 0으로 돌아간다(폭파 작업은 멈췄다가 이어서 한다).
        /// 지역 방어는 적의 확보 진행 시간이다.
        /// </summary>
        public int ProgressSeconds { get; internal set; }

        /// <summary>완료에 필요한 시간. 진행 표시용.</summary>
        public int RequiredSeconds => Kind == ObjectiveKind.DestroyTarget ? WorkSeconds : HoldSeconds;

        public bool Contains(Vec2 position) => Vec2.Distance(Center, position) <= Radius;
    }
}
