using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 칸마다 "얼마나 보이는 곳인가"(노출). 관측자가 볼 수 있는 칸에 그 관측자의 무게를 더한 값이다.
    /// 무게는 그 관측자가 실제로 거기 있을 가능성(지휘관의 의심 지점 점유 확률, 확인된 접촉이면 1)이다.
    /// 경로 탐색이 이 값을 비용으로 쓴다. 누가 만드느냐(지휘관의 믿음, 플레이어 진영의 접촉)에 따라 같은 지도라도 값이 다르다 —
    /// 실제 적 위치를 쓰지 않으므로 반칙이 없다.
    /// </summary>
    public sealed class ExposureField
    {
        private readonly float[] _values;

        public ExposureField(MapData map)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            _values = new float[map.Width * map.Height];
        }

        public MapData Map { get; }

        public float GetCell(int x, int y) => _values[y * Map.Width + x];

        public float GetAt(Vec2 position)
        {
            int x = Math.Min(Math.Max((int)Math.Floor(position.X / Map.CellSize), 0), Map.Width - 1);
            int y = Math.Min(Math.Max((int)Math.Floor(position.Y / Map.CellSize), 0), Map.Height - 1);
            return GetCell(x, y);
        }

        internal float GetIndex(int index) => _values[index];

        /// <summary>시야 영역(Viewshed 결과)에 들어 있는 칸마다 weight를 더한다.</summary>
        public void AddVisible(bool[] visible, float weight)
        {
            if (visible == null || visible.Length != _values.Length)
            {
                throw new ArgumentException("시야 영역의 칸 수가 지도와 다릅니다.", nameof(visible));
            }

            if (weight <= 0f)
            {
                return;
            }

            for (int i = 0; i < _values.Length; i++)
            {
                if (visible[i])
                {
                    _values[i] += weight;
                }
            }
        }

        /// <summary>한 지점의 관측자를 더한다(시야 영역을 그 자리에서 계산).</summary>
        public void AddObserver(Vec2 position, float weight, float eyeHeight, float targetHeight, float maxRange, float forestCanopyHeight)
        {
            if (weight <= 0f)
            {
                return;
            }

            AddVisible(Viewshed.Compute(Map, position, eyeHeight, targetHeight, maxRange, forestCanopyHeight), weight);
        }
    }
}
