using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    public enum OrderKind
    {
        Move,
        Watch,
        Hold,
        FirePolicy
    }

    /// <summary>
    /// 발신했지만 아직 부대에 도착하지 않은 명령.
    /// 통신이 두절되면 DeliverAtSeconds가 -1이 되고, 회복되는 순간 도착 시각이 정해진다.
    /// </summary>
    public class PendingOrder
    {
        /// <summary>통신 두절로 도착 시각이 정해지지 않은 상태.</summary>
        public const int WaitingForComms = -1;

        internal PendingOrder(Platoon platoon, OrderKind kind, string description)
        {
            Platoon = platoon;
            Kind = kind;
            Description = description;
        }

        public Platoon Platoon { get; }
        public OrderKind Kind { get; }

        /// <summary>로그·화면 표시용 설명. 예: "1소대 이동 → C4 (일반, 접촉 시 엄폐 후 응사)"</summary>
        public string Description { get; }

        public int IssuedAtSeconds { get; internal set; }
        public int DeliverAtSeconds { get; internal set; } = WaitingForComms;
        public bool IsWaitingForComms => DeliverAtSeconds == WaitingForComms;

        // 명령 종류별 내용. 해당 종류일 때만 의미가 있다.
        public IReadOnlyList<Waypoint> Waypoints { get; internal set; }
        public bool Append { get; internal set; }
        public float WatchBearingDegrees { get; internal set; }
        public float WatchArcDegrees { get; internal set; }
        public FirePolicy FirePolicy { get; internal set; }
    }
}
