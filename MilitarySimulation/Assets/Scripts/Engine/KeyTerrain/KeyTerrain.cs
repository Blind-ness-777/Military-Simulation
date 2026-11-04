using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 요지 하나(시나리오 데이터). 점이 아니라 영역이다 — 지휘관이 이 안에서 실제로 쓸 칸을 고른다.
    /// 이름·위치·반경·종류만 시나리오가 정하고, 시야 영역과 구간 성질은 불러올 때 코드가 계산한다(KeyTerrainGraph).
    /// </summary>
    public sealed class KeyTerrain
    {
        public KeyTerrain(string name, KeyTerrainKind kind, Vec2 center, float radius)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "요지 반경은 0보다 커야 합니다.");
            }

            Name = name;
            Kind = kind;
            Center = center;
            Radius = radius;
        }

        public string Name { get; }
        public KeyTerrainKind Kind { get; }
        public Vec2 Center { get; }
        public float Radius { get; }

        /// <summary>그래프 안 번호(추가한 순서). 그래프를 만들 때 정해진다.</summary>
        public int Index { get; internal set; } = -1;

        public bool Contains(Vec2 position) => Vec2.Distance(Center, position) <= Radius;
    }
}
