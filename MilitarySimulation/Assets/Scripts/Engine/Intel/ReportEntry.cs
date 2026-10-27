namespace MilitarySimulation.Engine
{
    internal enum ReportKind
    {
        /// <summary>소대 사건(도착, 피해, 사기 붕괴 등) 로그 한 줄.</summary>
        Event,

        /// <summary>적 관측.</summary>
        Sighting,

        /// <summary>소대 자신의 상태(위치·인원 등).</summary>
        Status
    }

    /// <summary>
    /// 소대에서 본부로 올라가는 보고 한 건. 통신이 저하되면 늦게, 두절되면 회복될 때까지 보류된다.
    /// DeliverAtSeconds가 -1이면 보류 중이다.
    /// </summary>
    internal sealed class ReportEntry
    {
        public const int WaitingForComms = -1;

        public ReportEntry(Platoon platoon, ReportKind kind, int observedAtSeconds)
        {
            Platoon = platoon;
            Kind = kind;
            ObservedAtSeconds = observedAtSeconds;
        }

        /// <summary>보고한 소대.</summary>
        public Platoon Platoon { get; }

        public ReportKind Kind { get; }

        /// <summary>보고 내용이 관측된 시각(경과 초). 두절 중 관측이 갱신되면 바뀐다.</summary>
        public int ObservedAtSeconds { get; set; }

        public int DeliverAtSeconds { get; set; } = WaitingForComms;
        public bool IsWaitingForComms => DeliverAtSeconds == WaitingForComms;

        // Event
        public string Text { get; set; }

        /// <summary>이 보고가 본부에 알리는 계획 사건(○소대 접촉/도착). 도착하면 그 사건을 기다리는 소대에 통지가 나간다.</summary>
        public PlanEventKind PlanEvent { get; set; }

        // Sighting
        public int TargetId { get; set; }
        public Vec2 TargetPosition { get; set; }
        public Vec2 ObserverPosition { get; set; }

        // Status
        public PlatoonSnapshot Snapshot { get; set; }
    }
}
