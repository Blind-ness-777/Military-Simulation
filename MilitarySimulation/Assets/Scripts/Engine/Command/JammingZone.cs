namespace MilitarySimulation.Engine
{
    public enum JammingStrength
    {
        /// <summary>통신 상태를 한 단계 나쁘게 한다(정상 → 저하, 저하 → 두절).</summary>
        Degrade,

        /// <summary>통신을 끊는다.</summary>
        Block
    }

    /// <summary>
    /// 재밍 구역(시나리오 데이터). 본부나 부대 중 하나라도 구역 안에 있으면 그 둘 사이 통신이 영향을 받는다.
    /// </summary>
    public class JammingZone
    {
        public JammingZone(Vec2 center, float radius, JammingStrength strength, Side affectedSide)
        {
            Center = center;
            Radius = radius;
            Strength = strength;
            AffectedSide = affectedSide;
        }

        public Vec2 Center { get; }
        public float Radius { get; }
        public JammingStrength Strength { get; }

        /// <summary>재밍을 받는 진영. 보통 재밍을 거는 쪽의 반대편이다.</summary>
        public Side AffectedSide { get; }

        public bool Contains(Vec2 position) => Vec2.Distance(Center, position) <= Radius;
    }
}
