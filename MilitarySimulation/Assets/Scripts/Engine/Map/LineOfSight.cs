namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 높이맵 기반 시야선 판정.
    /// Unity 레이캐스트가 아니라 엔진의 MapData만 사용한다.
    /// 화면의 지형과 판정의 지형이 같은 데이터에서 나오게 하기 위해서다.
    /// </summary>
    public static class LineOfSight
    {
        /// <summary>
        /// from(관측자 눈높이)에서 to(표적 높이)까지 시선이 지형이나 숲에 막히지 않는지 판정한다.
        /// </summary>
        /// <remarks>
        /// 양 끝에서 한 칸 이내의 숲은 가리지 않는 것으로 본다.
        /// 숲 가장자리에 붙은 부대가 바깥을 볼 수 있어야 하기 때문이다.
        /// 지면 높이는 구간 전체에서 검사한다.
        /// </remarks>
        public static bool HasLineOfSight(
            MapData map,
            Vec2 from,
            float fromEyeHeight,
            Vec2 to,
            float toTargetHeight,
            float forestCanopyHeight)
        {
            float distance = Vec2.Distance(from, to);
            float step = map.CellSize * 0.5f;
            if (distance <= step)
            {
                return true;
            }

            float startHeight = map.GetHeightAt(from) + fromEyeHeight;
            float endHeight = map.GetHeightAt(to) + toTargetHeight;
            float forestClearance = map.CellSize;
            int sampleCount = (int)(distance / step);

            for (int i = 1; i < sampleCount; i++)
            {
                float travelled = i * step;
                float t = travelled / distance;
                Vec2 point = Vec2.Lerp(from, to, t);
                float sightHeight = startHeight + (endHeight - startHeight) * t;

                float obstacleHeight = map.GetHeightAt(point);
                bool nearEndpoint = travelled < forestClearance || distance - travelled < forestClearance;
                if (!nearEndpoint && map.GetTerrainAt(point) == TerrainType.Forest)
                {
                    obstacleHeight += forestCanopyHeight;
                }

                if (obstacleHeight > sightHeight)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
