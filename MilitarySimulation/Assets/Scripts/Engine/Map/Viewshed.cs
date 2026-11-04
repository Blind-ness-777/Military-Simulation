using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 한 지점에서 보이는 칸들(시야 영역). 칸 중심까지의 시야선을 LineOfSight로 하나씩 검사한다.
    /// 판정과 같은 시야선 규칙을 쓰므로, 시야 영역과 실제 탐지가 어긋나지 않는다.
    /// 결과는 칸 인덱스(y * Width + x) 기준 bool 배열이다.
    /// </summary>
    /// <remarks>
    /// 칸 수 × 시야선 길이만큼 계산하므로 1km 지도(100×100칸)에서는 가볍지만, 10km 지도에서는 칸 크기나 범위를 줄여야 한다.
    /// </remarks>
    public static class Viewshed
    {
        public static bool[] Compute(
            MapData map,
            Vec2 origin,
            float eyeHeight,
            float targetHeight,
            float maxRange,
            float forestCanopyHeight)
        {
            var visible = new bool[map.Width * map.Height];
            Accumulate(map, origin, eyeHeight, targetHeight, maxRange, forestCanopyHeight, visible);
            return visible;
        }

        /// <summary>origin에서 보이는 칸을 visible에 더한다(이미 true인 칸은 다시 검사하지 않는다).</summary>
        public static void Accumulate(
            MapData map,
            Vec2 origin,
            float eyeHeight,
            float targetHeight,
            float maxRange,
            float forestCanopyHeight,
            bool[] visible)
        {
            float cellSize = map.CellSize;
            int minX = Math.Max(0, (int)Math.Floor((origin.X - maxRange) / cellSize));
            int maxX = Math.Min(map.Width - 1, (int)Math.Floor((origin.X + maxRange) / cellSize));
            int minY = Math.Max(0, (int)Math.Floor((origin.Y - maxRange) / cellSize));
            int maxY = Math.Min(map.Height - 1, (int)Math.Floor((origin.Y + maxRange) / cellSize));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * map.Width + x;
                    if (visible[index])
                    {
                        continue;
                    }

                    Vec2 center = map.GetCellCenter(x, y);
                    if (Vec2.Distance(origin, center) > maxRange)
                    {
                        continue;
                    }

                    visible[index] = LineOfSight.HasLineOfSight(
                        map, origin, eyeHeight, center, targetHeight, forestCanopyHeight);
                }
            }
        }
    }
}
