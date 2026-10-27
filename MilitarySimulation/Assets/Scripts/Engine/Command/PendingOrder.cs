using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    public enum OrderKind
    {
        Move,
        Watch,
        Hold,
        FirePolicy,

        /// <summary>계획 명령의 신호. 받은 소대는 번호를 기억하고, 그 신호를 기다리는 대기를 푼다.</summary>
        Signal,

        /// <summary>H시(작전 시각) 지정·변경.</summary>
        HHour,

        /// <summary>
        /// 본부가 자동으로 보내는 통지: "○소대 접촉/도착". 그 사건을 기다리는 소대에게만 보낸다.
        /// 로그에 남기지 않는다(대기가 풀릴 때 이유로 나온다).
        /// </summary>
        PlanNotice
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
        public int SignalNumber { get; internal set; }
        public int HHourSeconds { get; internal set; }
        public Platoon NoticeSubject { get; internal set; }
        public PlanEventKind NoticeKind { get; internal set; }

        /// <summary>발신할 때 "명령 발신" 로그를 남기는지. 신호·H시는 진영 전체에 한 줄로 따로 남긴다.</summary>
        public bool LogOnSubmit { get; internal set; } = true;

        /// <summary>도착할 때 "명령 수신" 로그를 남기는지.</summary>
        public bool LogOnDelivery { get; internal set; } = true;
    }
}
