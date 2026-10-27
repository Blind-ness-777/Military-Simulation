using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 방어 진지 하나(시나리오 데이터). 위치는 맵을 만들 때 시야 조건과 함께 정한다.
    /// AI가 지형을 분석해 진지를 고르는 기능은 없다(데모 1단계 결정).
    /// </summary>
    public class DefensivePosition
    {
        public DefensivePosition(
            string name,
            Platoon garrison,
            Vec2 center,
            float threatRadius,
            float watchBearingDegrees,
            float watchArcDegrees,
            Vec2 fallbackPoint)
        {
            if (threatRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(threatRadius));
            Name = name;
            Garrison = garrison ?? throw new ArgumentNullException(nameof(garrison));
            Center = center;
            ThreatRadius = threatRadius;
            WatchBearingDegrees = watchBearingDegrees;
            WatchArcDegrees = watchArcDegrees;
            FallbackPoint = fallbackPoint;
        }

        public string Name { get; }

        /// <summary>이 진지를 지키는 소대.</summary>
        public Platoon Garrison { get; }

        public Vec2 Center { get; }

        /// <summary>이 반경 안에 들어온 접촉을 이 진지에 대한 위협으로 본다.</summary>
        public float ThreatRadius { get; }

        /// <summary>기본 경계 방향. 증원·후퇴 뒤의 경계 방향 기준이기도 하다.</summary>
        public float WatchBearingDegrees { get; }
        public float WatchArcDegrees { get; }

        /// <summary>후퇴할 곳.</summary>
        public Vec2 FallbackPoint { get; }
    }

    /// <summary>
    /// 적 지휘관의 방어 계획(시나리오 데이터): 진지 목록과 예비대.
    /// 판 진행 중 바뀌는 상태(예비대 투입 여부, 포기한 진지)는 EnemyCommander가 따로 갖는다.
    /// </summary>
    public class DefensePlan
    {
        private readonly List<DefensivePosition> _positions = new List<DefensivePosition>();

        /// <param name="reserve">예비대 소대. 없으면 null(증원·역습 후보가 생기지 않는다).</param>
        public DefensePlan(Platoon reserve)
        {
            Reserve = reserve;
        }

        public Platoon Reserve { get; }
        public IReadOnlyList<DefensivePosition> Positions => _positions;

        public DefensivePosition AddPosition(DefensivePosition position)
        {
            _positions.Add(position ?? throw new ArgumentNullException(nameof(position)));
            return position;
        }
    }
}
