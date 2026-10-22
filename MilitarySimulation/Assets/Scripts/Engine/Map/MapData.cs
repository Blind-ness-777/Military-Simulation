using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 높이맵과 지형 종류를 담는 격자 지도.
    /// 위치는 연속 좌표(미터)로 다루고, 지형 판정만 격자 단위로 한다.
    /// 높이는 각 칸의 중심에 정의되고, 칸 사이는 쌍선형 보간한다.
    /// </summary>
    public class MapData
    {
        /// <summary>격자 참조(예: "C3")의 한 칸 크기(미터).</summary>
        public const float GridReferenceSize = 100f;

        private readonly float[] _heights;
        private readonly TerrainType[] _terrain;

        public MapData(int width, int height, float cellSize)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));

            Width = width;
            Height = height;
            CellSize = cellSize;
            _heights = new float[width * height];
            _terrain = new TerrainType[width * height];
        }

        /// <summary>가로 칸 수.</summary>
        public int Width { get; }

        /// <summary>세로 칸 수.</summary>
        public int Height { get; }

        /// <summary>한 칸의 크기(미터).</summary>
        public float CellSize { get; }

        public float WidthMeters => Width * CellSize;
        public float HeightMeters => Height * CellSize;

        public float GetCellHeight(int x, int y) => _heights[ToIndex(x, y)];

        public void SetCellHeight(int x, int y, float height) => _heights[ToIndex(x, y)] = height;

        public TerrainType GetCellTerrain(int x, int y) => _terrain[ToIndex(x, y)];

        public void SetCellTerrain(int x, int y, TerrainType terrain) => _terrain[ToIndex(x, y)] = terrain;

        /// <summary>칸 중심의 월드 좌표.</summary>
        public Vec2 GetCellCenter(int x, int y) => new Vec2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

        public bool IsInside(Vec2 position)
        {
            return position.X >= 0f && position.Y >= 0f && position.X < WidthMeters && position.Y < HeightMeters;
        }

        public Vec2 ClampInside(Vec2 position)
        {
            const float margin = 0.01f;
            float x = Clamp(position.X, 0f, WidthMeters - margin);
            float y = Clamp(position.Y, 0f, HeightMeters - margin);
            return new Vec2(x, y);
        }

        public TerrainType GetTerrainAt(Vec2 position)
        {
            int x = ClampIndex((int)Math.Floor(position.X / CellSize), Width);
            int y = ClampIndex((int)Math.Floor(position.Y / CellSize), Height);
            return _terrain[ToIndex(x, y)];
        }

        public float GetHeightAt(Vec2 position)
        {
            float fx = position.X / CellSize - 0.5f;
            float fy = position.Y / CellSize - 0.5f;

            int x0 = (int)Math.Floor(fx);
            int y0 = (int)Math.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            int x1 = ClampIndex(x0 + 1, Width);
            int y1 = ClampIndex(y0 + 1, Height);
            x0 = ClampIndex(x0, Width);
            y0 = ClampIndex(y0, Height);

            float bottom = Lerp(GetCellHeight(x0, y0), GetCellHeight(x1, y0), tx);
            float top = Lerp(GetCellHeight(x0, y1), GetCellHeight(x1, y1), tx);
            return Lerp(bottom, top, ty);
        }

        /// <summary>
        /// 100m 격자 참조. 열은 서쪽부터 A, B, C…, 행은 남쪽부터 1, 2, 3….
        /// </summary>
        public string GetGridReference(Vec2 position)
        {
            int column = Math.Max(0, (int)(position.X / GridReferenceSize));
            int row = Math.Max(0, (int)(position.Y / GridReferenceSize)) + 1;
            char columnLetter = (char)('A' + Math.Min(column, 25));
            return $"{columnLetter}{row}";
        }

        private int ToIndex(int x, int y)
        {
            if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
            return y * Width + x;
        }

        private static int ClampIndex(int value, int count) => value < 0 ? 0 : (value >= count ? count - 1 : value);

        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
