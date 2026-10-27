using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 웨이포인트의 대기 조건(계획 명령). 소대가 그 지점에 도착하면 멈춰 서고, 걸린 조건 중 하나라도 충족되면 출발한다.
    /// 작전의 동시성(포위·양동·매복 등)을 플레이어의 반사신경이 아니라 미리 짠 계획으로 맞추기 위한 장치다.
    /// - 신호: 본부가 보낸 신호 번호가 소대에 도착하면(통신을 탄다).
    /// - H시: 소대가 알고 있는 작전 시각이 되면(통신이 필요 없다. 다만 H시 변경은 통신을 탄다).
    /// - ○소대 접촉/도착: 본부가 그 보고를 받으면 자동으로 통지가 나가고, 통지가 소대에 도착하면.
    ///   자기 소대를 고르면 "내가 적을 발견하면"(접촉) — 이 경우는 통신이 필요 없다.
    /// </summary>
    public sealed class WaitCondition
    {
        /// <summary>신호 번호는 1부터 이 값까지.</summary>
        public const int MaxSignal = 3;

        /// <param name="signal">0이면 신호 조건 없음.</param>
        public WaitCondition(int signal, bool atHHour, Platoon contactPlatoon, Platoon arrivalPlatoon)
        {
            if (signal < 0 || signal > MaxSignal) throw new ArgumentOutOfRangeException(nameof(signal));
            Signal = signal;
            AtHHour = atHHour;
            ContactPlatoon = contactPlatoon;
            ArrivalPlatoon = arrivalPlatoon;
        }

        public int Signal { get; }
        public bool AtHHour { get; }

        /// <summary>이 소대가 접촉하면. null이면 조건 없음.</summary>
        public Platoon ContactPlatoon { get; }

        /// <summary>이 소대가 도착(경로 끝이나 대기 지점)하면. null이면 조건 없음.</summary>
        public Platoon ArrivalPlatoon { get; }

        public bool IsEmpty => Signal == 0 && !AtHHour && ContactPlatoon == null && ArrivalPlatoon == null;
    }

    /// <summary>대기 조건이 기다리는 다른 소대의 사건.</summary>
    public enum PlanEventKind
    {
        None,
        Contact,
        Arrival
    }
}
