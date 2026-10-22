using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 방위각 계산. 0도 = 북쪽(+Y), 시계 방향으로 증가한다.
    /// </summary>
    public static class Bearing
    {
        public static float Between(Vec2 from, Vec2 to)
        {
            Vec2 delta = to - from;
            double degrees = Math.Atan2(delta.X, delta.Y) * 180.0 / Math.PI;
            return Normalize((float)degrees);
        }

        public static float Normalize(float degrees)
        {
            float result = degrees % 360f;
            return result < 0f ? result + 360f : result;
        }

        /// <summary>두 방위각 사이의 각도 차이(0~180도).</summary>
        public static float Difference(float a, float b)
        {
            float diff = Normalize(b - a);
            return diff > 180f ? 360f - diff : diff;
        }

        /// <summary>방위각 방향의 단위 벡터.</summary>
        public static Vec2 ToDirection(float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            return new Vec2((float)Math.Sin(radians), (float)Math.Cos(radians));
        }
    }
}
