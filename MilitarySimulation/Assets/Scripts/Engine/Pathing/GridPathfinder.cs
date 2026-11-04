using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 지도 격자(칸) 위의 경로 탐색. 판단은 요지 그래프가 하고, 실제로 걸어갈 길은 여기서 만든다(설계 2회차).
    /// 순서: 8방향 A*(이동 방식별 비용) → 경로 펴기(직선으로 바꿔도 비용이 거의 같으면 중간 점을 건너뜀)
    /// → 곡선화(Chaikin 모서리 깎기) → 웨이포인트 여러 개.
    /// 같은 입력이면 항상 같은 경로가 나온다(우선순위가 같으면 먼저 넣은 칸부터 꺼냄).
    /// 직접 이동 명령(OrderMove)은 이 탐색을 거치지 않고 그대로 직선이다.
    /// </summary>
    public sealed class GridPathfinder
    {
        private const float Sqrt2 = 1.41421356f;

        // 이웃 순서를 고정해 결정론을 지킨다(상하좌우 먼저, 대각선 다음).
        private static readonly int[] NeighborX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighborY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly MapData _map;
        private readonly RulesConfig _rules;

        public GridPathfinder(MapData map, RulesConfig rules)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public PathCostProfile GetProfile(MoveMode moveMode)
        {
            switch (moveMode)
            {
                case MoveMode.Fast:
                    return _rules.FastPathCost;
                case MoveMode.Stealth:
                    return _rules.StealthPathCost;
                default:
                    return _rules.NormalPathCost;
            }
        }

        /// <summary>
        /// from에서 to까지 걸어갈 경로. 결과는 from을 뺀 지점들이고 마지막은 to다(웨이포인트로 바로 쓸 수 있게).
        /// exposure가 없으면 노출 비용은 0(아무도 안 본다고 가정)이다.
        /// </summary>
        public List<Vec2> FindPath(Vec2 from, Vec2 to, MoveMode moveMode, ExposureField exposure = null)
        {
            from = _map.ClampInside(from);
            to = _map.ClampInside(to);
            PathCostProfile profile = GetProfile(moveMode);

            List<int> cells = FindCellPath(from, to, profile, exposure);
            var points = new List<Vec2>(cells.Count + 1) { from };
            for (int i = 1; i < cells.Count - 1; i++)
            {
                points.Add(GetCellCenter(cells[i]));
            }

            points.Add(to);

            List<Vec2> straightened = Straighten(points, profile, exposure);
            List<Vec2> smoothed = Smooth(straightened, profile, exposure);
            smoothed.RemoveAt(0);
            return smoothed;
        }

        /// <summary>
        /// 칸 경로(칸 인덱스 목록, 출발 칸과 도착 칸 포함). A*: 비용 = GetStepCost 합, 휴리스틱 = 직선 거리 × 1m당 최소 비용.
        /// </summary>
        internal List<int> FindCellPath(Vec2 from, Vec2 to, PathCostProfile profile, ExposureField exposure)
        {
            int width = _map.Width;
            int cellCount = width * _map.Height;
            int start = GetCellIndex(from);
            int goal = GetCellIndex(to);

            var path = new List<int>();
            if (start == goal)
            {
                path.Add(start);
                return path;
            }

            var gScore = new float[cellCount];
            var cameFrom = new int[cellCount];
            var closed = new bool[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                gScore[i] = float.PositiveInfinity;
                cameFrom[i] = -1;
            }

            float heuristicPerMeter = profile.MinCostPerMeter;
            Vec2 goalCenter = GetCellCenter(goal);
            var open = new MinHeap();
            gScore[start] = 0f;
            open.Push(Vec2.Distance(GetCellCenter(start), goalCenter) * heuristicPerMeter, start);

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current])
                {
                    continue;
                }

                if (current == goal)
                {
                    break;
                }

                closed[current] = true;
                int cx = current % width;
                int cy = current / width;
                for (int n = 0; n < NeighborX.Length; n++)
                {
                    int nx = cx + NeighborX[n];
                    int ny = cy + NeighborY[n];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= _map.Height)
                    {
                        continue;
                    }

                    int neighbor = ny * width + nx;
                    if (closed[neighbor])
                    {
                        continue;
                    }

                    float distance = n < 4 ? _map.CellSize : _map.CellSize * Sqrt2;
                    float tentative = gScore[current] + GetStepCost(current, neighbor, distance, profile, exposure);
                    if (tentative >= gScore[neighbor])
                    {
                        continue;
                    }

                    gScore[neighbor] = tentative;
                    cameFrom[neighbor] = current;
                    float heuristic = Vec2.Distance(GetCellCenter(neighbor), goalCenter) * heuristicPerMeter;
                    open.Push(tentative + heuristic, neighbor);
                }
            }

            for (int cell = goal; cell != -1; cell = cameFrom[cell])
            {
                path.Add(cell);
            }

            path.Reverse();
            return path;
        }

        /// <summary>이웃한 두 칸 사이 한 걸음의 비용. 지형·노출은 들어가는 칸 기준.</summary>
        internal float GetStepCost(int fromIndex, int toIndex, float distance, PathCostProfile profile, ExposureField exposure)
        {
            int width = _map.Width;
            TerrainType terrain = _map.GetCellTerrain(toIndex % width, toIndex / width);
            float climb = Math.Abs(
                _map.GetCellHeight(toIndex % width, toIndex / width) - _map.GetCellHeight(fromIndex % width, fromIndex / width));
            float exposureValue = exposure != null ? exposure.GetIndex(toIndex) : 0f;
            return GetCost(distance, climb, terrain, exposureValue, profile);
        }

        /// <summary>두 지점을 잇는 직선의 비용. 반 칸 간격으로 잘라 GetStepCost와 같은 식으로 더한다.</summary>
        internal float GetSegmentCost(Vec2 from, Vec2 to, PathCostProfile profile, ExposureField exposure)
        {
            float length = Vec2.Distance(from, to);
            if (length < 1e-4f)
            {
                return 0f;
            }

            int pieces = Math.Max(1, (int)Math.Ceiling(length / (_map.CellSize * 0.5f)));
            float pieceLength = length / pieces;
            float cost = 0f;
            Vec2 previous = from;
            float previousHeight = _map.GetHeightAt(from);
            for (int i = 1; i <= pieces; i++)
            {
                Vec2 point = Vec2.Lerp(from, to, (float)i / pieces);
                float height = _map.GetHeightAt(point);
                Vec2 middle = Vec2.Lerp(previous, point, 0.5f);
                float exposureValue = exposure != null ? exposure.GetAt(middle) : 0f;
                cost += GetCost(pieceLength, Math.Abs(height - previousHeight), _map.GetTerrainAt(middle), exposureValue, profile);
                previous = point;
                previousHeight = height;
            }

            return cost;
        }

        private static float GetCost(float distance, float climb, TerrainType terrain, float exposure, PathCostProfile profile)
        {
            float speedFactor;
            switch (terrain)
            {
                case TerrainType.Forest:
                    speedFactor = profile.ForestSpeedFactor;
                    break;
                case TerrainType.Road:
                    speedFactor = profile.RoadSpeedFactor;
                    break;
                default:
                    speedFactor = 1f;
                    break;
            }

            float perMeter = profile.TimeWeight / speedFactor + profile.ExposureWeight * exposure;
            if (terrain != TerrainType.Forest)
            {
                perMeter += profile.OpenGroundWeight;
            }

            return distance * perMeter + profile.SlopeWeight * climb;
        }

        /// <summary>
        /// 경로 펴기: 지금 점에서 가장 먼 점부터, 직선 비용이 원래 경로 비용 + 허용 폭 이하인 점으로 건너뛴다.
        /// 허용 폭 = 그 구간 길이 × 1m당 최소 비용 × StraightenTolerance(거리 기준). 비용 기준(%)으로 하면
        /// 노출이 큰 구간이 끼었을 때 허용 폭이 커져 숲 우회가 통째로 지워진다.
        /// 비용으로 판단하므로 은밀 경로가 개활지를 가로지르는 지름길로 바뀌지 않는다.
        /// </summary>
        private List<Vec2> Straighten(List<Vec2> points, PathCostProfile profile, ExposureField exposure)
        {
            var result = new List<Vec2> { points[0] };
            if (points.Count <= 2)
            {
                if (points.Count == 2)
                {
                    result.Add(points[1]);
                }

                return result;
            }

            var cumulative = new float[points.Count];
            var cumulativeLength = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
            {
                cumulative[i] = cumulative[i - 1] + GetSegmentCost(points[i - 1], points[i], profile, exposure);
                cumulativeLength[i] = cumulativeLength[i - 1] + Vec2.Distance(points[i - 1], points[i]);
            }

            float slackPerMeter = profile.MinCostPerMeter * profile.StraightenTolerance;

            int last = points.Count - 1;
            int anchor = 0;
            while (anchor < last)
            {
                int next = anchor + 1;
                for (int candidate = last; candidate > anchor + 1; candidate--)
                {
                    float pathCost = cumulative[candidate] - cumulative[anchor];
                    float slack = (cumulativeLength[candidate] - cumulativeLength[anchor]) * slackPerMeter;
                    float lineCost = GetSegmentCost(points[anchor], points[candidate], profile, exposure);
                    if (lineCost <= pathCost + slack + 1e-3f)
                    {
                        next = candidate;
                        break;
                    }
                }

                result.Add(points[next]);
                anchor = next;
            }

            return result;
        }

        /// <summary>
        /// 곡선화: Chaikin 모서리 깎기를 정해진 횟수만큼(양 끝점은 그대로). 너무 가까운 점은 뺀다.
        /// 깎은 직선의 비용이 원래 모서리를 도는 비용 + 펴기 허용 폭보다 크면 그 모서리는 깎지 않는다
        /// (노출 칸 모서리를 파고들어 은밀 경로가 보이는 땅을 밟지 않게).
        /// </summary>
        private List<Vec2> Smooth(List<Vec2> points, PathCostProfile profile, ExposureField exposure)
        {
            float slackPerMeter = profile.MinCostPerMeter * profile.StraightenTolerance;
            List<Vec2> current = points;
            for (int iteration = 0; iteration < _rules.PathSmoothingIterations && current.Count > 2; iteration++)
            {
                var next = new List<Vec2>(current.Count * 2) { current[0] };
                for (int i = 1; i < current.Count - 1; i++)
                {
                    Vec2 previous = current[i - 1];
                    Vec2 corner = current[i];
                    Vec2 following = current[i + 1];

                    // 첫·마지막 구간은 끝점을 옮기지 않도록 반이 아니라 끝까지 쓴다.
                    Vec2 cutIn = Vec2.Lerp(previous, corner, i == 1 ? 0.5f : 0.75f);
                    Vec2 cutOut = Vec2.Lerp(corner, following, i == current.Count - 2 ? 0.5f : 0.25f);
                    float aroundCost = GetSegmentCost(cutIn, corner, profile, exposure) + GetSegmentCost(corner, cutOut, profile, exposure);
                    float cutCost = GetSegmentCost(cutIn, cutOut, profile, exposure);
                    float slack = Vec2.Distance(cutIn, cutOut) * slackPerMeter;

                    next.Add(cutIn);
                    if (cutCost > aroundCost + slack + 1e-3f)
                    {
                        next.Add(corner);
                    }

                    next.Add(cutOut);
                }

                next.Add(current[current.Count - 1]);
                current = next;
            }

            var result = new List<Vec2>(current.Count) { current[0] };
            for (int i = 1; i < current.Count - 1; i++)
            {
                if (Vec2.Distance(result[result.Count - 1], current[i]) >= _rules.PathMinWaypointSpacing)
                {
                    result.Add(current[i]);
                }
            }

            result.Add(current[current.Count - 1]);
            return result;
        }

        internal int GetCellIndex(Vec2 position)
        {
            int x = Math.Min(Math.Max((int)Math.Floor(position.X / _map.CellSize), 0), _map.Width - 1);
            int y = Math.Min(Math.Max((int)Math.Floor(position.Y / _map.CellSize), 0), _map.Height - 1);
            return y * _map.Width + x;
        }

        internal Vec2 GetCellCenter(int index) => _map.GetCellCenter(index % _map.Width, index / _map.Width);

        /// <summary>A*용 최소 힙. 우선순위가 같으면 먼저 넣은 것부터 꺼낸다(결정론).</summary>
        private sealed class MinHeap
        {
            private readonly List<float> _priorities = new List<float>();
            private readonly List<long> _orders = new List<long>();
            private readonly List<int> _values = new List<int>();
            private long _nextOrder;

            public int Count => _values.Count;

            public void Push(float priority, int value)
            {
                _priorities.Add(priority);
                _orders.Add(_nextOrder++);
                _values.Add(value);
                int index = _values.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(index, parent))
                    {
                        break;
                    }

                    Swap(index, parent);
                    index = parent;
                }
            }

            public int Pop()
            {
                int top = _values[0];
                int last = _values.Count - 1;
                Swap(0, last);
                _priorities.RemoveAt(last);
                _orders.RemoveAt(last);
                _values.RemoveAt(last);

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;
                    if (left < _values.Count && Less(left, smallest))
                    {
                        smallest = left;
                    }

                    if (right < _values.Count && Less(right, smallest))
                    {
                        smallest = right;
                    }

                    if (smallest == index)
                    {
                        break;
                    }

                    Swap(index, smallest);
                    index = smallest;
                }

                return top;
            }

            private bool Less(int a, int b)
            {
                return _priorities[a] < _priorities[b] || (_priorities[a] == _priorities[b] && _orders[a] < _orders[b]);
            }

            private void Swap(int a, int b)
            {
                (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
                (_orders[a], _orders[b]) = (_orders[b], _orders[a]);
                (_values[a], _values[b]) = (_values[b], _values[a]);
            }
        }
    }
}
