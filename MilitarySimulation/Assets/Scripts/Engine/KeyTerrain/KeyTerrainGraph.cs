using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 요지 그래프. 지휘관은 이 그래프로 작전을 생각하고(어느 요지를 거쳐 어디서 기다릴지),
    /// 실제 걸어갈 길은 GridPathfinder가 만든다(설계 2회차).
    /// 불러올 때 계산하는 것:
    /// - 요지별 시야 영역: 요지 영역 안 다섯 지점(중심 + 반경 60% 동서남북)에서 보이는 칸의 합집합. 보병 눈높이·표적 높이·관측 거리.
    /// - 구간: 모든 요지 쌍의 격자 경로(일반 이동, 노출 없음)를 구한 뒤 이웃끼리만 남긴다 —
    ///   다른 요지 영역을 지나는 쌍, 다른 요지를 거쳐도 거의 같은 거리(DetourRatio 이하)인 쌍은 뺀다.
    ///   남은 구간마다 길이·엄폐 비율·요지별 가시 비율.
    /// 계산이 무거워서 시뮬레이션은 처음 쓸 때 만든다(Simulation.KeyTerrain).
    /// </summary>
    public sealed class KeyTerrainGraph
    {
        private const float ViewSampleOffsetRatio = 0.6f;

        /// <summary>a–c–b 우회 길이가 a–b 직행의 이 배수 이하이면 a–b는 구간으로 두지 않는다(c를 거치면 됨).</summary>
        private const float DetourRatio = 1.2f;

        private readonly List<KeyTerrain> _nodes;
        private readonly List<KeyTerrainEdge> _edges = new List<KeyTerrainEdge>();
        private readonly List<bool[]> _viewsheds = new List<bool[]>();
        private readonly MapData _map;

        internal KeyTerrainGraph(
            MapData map,
            RulesConfig rules,
            GridPathfinder pathfinder,
            IReadOnlyList<KeyTerrain> nodes,
            UnitType observerType)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _nodes = new List<KeyTerrain>(nodes);
            for (int i = 0; i < _nodes.Count; i++)
            {
                _nodes[i].Index = i;
                _viewsheds.Add(ComputeViewshed(_nodes[i], rules, observerType));
            }

            BuildEdges(pathfinder);
        }

        public IReadOnlyList<KeyTerrain> Nodes => _nodes;
        public IReadOnlyList<KeyTerrainEdge> Edges => _edges;

        public KeyTerrain GetNode(string name)
        {
            foreach (KeyTerrain node in _nodes)
            {
                if (node.Name == name)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>그 요지에서 보이는 칸인지(칸 좌표).</summary>
        public bool IsVisibleFrom(int nodeIndex, int cellX, int cellY) => _viewsheds[nodeIndex][cellY * _map.Width + cellX];

        /// <summary>그 요지에서 보이는 지점인지(그 지점이 든 칸 기준).</summary>
        public bool IsVisibleFrom(int nodeIndex, Vec2 position)
        {
            int x = Math.Min(Math.Max((int)Math.Floor(position.X / _map.CellSize), 0), _map.Width - 1);
            int y = Math.Min(Math.Max((int)Math.Floor(position.Y / _map.CellSize), 0), _map.Height - 1);
            return IsVisibleFrom(nodeIndex, x, y);
        }

        /// <summary>그 요지에 닿은 구간들(추가 순서).</summary>
        public IEnumerable<KeyTerrainEdge> GetEdges(int nodeIndex)
        {
            foreach (KeyTerrainEdge edge in _edges)
            {
                if (edge.FromIndex == nodeIndex || edge.ToIndex == nodeIndex)
                {
                    yield return edge;
                }
            }
        }

        /// <summary>
        /// 요지마다 무게(그 요지에 적이 있을 확률 등)를 주면, 그 요지들에서 보이는 정도를 칸마다 더한 노출 지도를 만든다.
        /// 미리 계산한 시야 영역을 쓰므로 가볍다. 의심 지점(구현 3번)이 이걸로 경로 비용을 바꾼다.
        /// </summary>
        public ExposureField BuildExposure(IReadOnlyList<float> nodeWeights)
        {
            if (nodeWeights == null || nodeWeights.Count != _nodes.Count)
            {
                throw new ArgumentException("요지 수만큼 무게가 있어야 합니다.", nameof(nodeWeights));
            }

            var field = new ExposureField(_map);
            for (int i = 0; i < _nodes.Count; i++)
            {
                field.AddVisible(_viewsheds[i], nodeWeights[i]);
            }

            return field;
        }

        private bool[] ComputeViewshed(KeyTerrain node, RulesConfig rules, UnitType observerType)
        {
            var visible = new bool[_map.Width * _map.Height];
            float offset = node.Radius * ViewSampleOffsetRatio;
            Vec2[] samples =
            {
                node.Center,
                node.Center + new Vec2(0f, offset),
                node.Center + new Vec2(offset, 0f),
                node.Center + new Vec2(0f, -offset),
                node.Center + new Vec2(-offset, 0f)
            };

            foreach (Vec2 sample in samples)
            {
                Viewshed.Accumulate(
                    _map,
                    _map.ClampInside(sample),
                    observerType.EyeHeight,
                    observerType.TargetHeight,
                    observerType.MaxVisualRange,
                    rules.ForestCanopyHeight,
                    visible);
            }

            return visible;
        }

        private void BuildEdges(GridPathfinder pathfinder)
        {
            PathCostProfile profile = pathfinder.GetProfile(MoveMode.Normal);
            int count = _nodes.Count;
            var cellPaths = new List<int>[count, count];
            var lengths = new float[count, count];
            for (int a = 0; a < count; a++)
            {
                for (int b = a + 1; b < count; b++)
                {
                    List<int> cells = pathfinder.FindCellPath(_nodes[a].Center, _nodes[b].Center, profile, null);
                    cellPaths[a, b] = cells;
                    lengths[a, b] = GetCellPathLength(cells, pathfinder);
                    lengths[b, a] = lengths[a, b];
                }
            }

            for (int a = 0; a < count; a++)
            {
                for (int b = a + 1; b < count; b++)
                {
                    if (PassesThroughOtherNode(cellPaths[a, b], pathfinder, a, b) || HasShortDetour(lengths, a, b))
                    {
                        continue;
                    }

                    _edges.Add(CreateEdge(a, b, cellPaths[a, b], lengths[a, b], pathfinder));
                }
            }
        }

        /// <summary>다른 요지 c를 거치는 길이 a–b 직행보다 거의 길지 않은지(두 구간 모두 직행보다 짧고, 합이 DetourRatio 배 이하).</summary>
        private bool HasShortDetour(float[,] lengths, int a, int b)
        {
            float direct = lengths[a, b];
            for (int c = 0; c < _nodes.Count; c++)
            {
                if (c == a || c == b)
                {
                    continue;
                }

                if (lengths[a, c] < direct && lengths[c, b] < direct && lengths[a, c] + lengths[c, b] <= direct * DetourRatio)
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetCellPathLength(List<int> cells, GridPathfinder pathfinder)
        {
            float length = 0f;
            for (int i = 1; i < cells.Count; i++)
            {
                length += Vec2.Distance(pathfinder.GetCellCenter(cells[i - 1]), pathfinder.GetCellCenter(cells[i]));
            }

            return length;
        }

        private bool PassesThroughOtherNode(List<int> cells, GridPathfinder pathfinder, int a, int b)
        {
            foreach (int cell in cells)
            {
                Vec2 center = pathfinder.GetCellCenter(cell);
                for (int n = 0; n < _nodes.Count; n++)
                {
                    if (n != a && n != b && _nodes[n].Contains(center))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private KeyTerrainEdge CreateEdge(int a, int b, List<int> cells, float length, GridPathfinder pathfinder)
        {
            int forestCells = 0;
            var visibleCells = new int[_nodes.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (_map.GetCellTerrain(cell % _map.Width, cell / _map.Width) == TerrainType.Forest)
                {
                    forestCells++;
                }

                for (int n = 0; n < _nodes.Count; n++)
                {
                    if (_viewsheds[n][cell])
                    {
                        visibleCells[n]++;
                    }
                }
            }

            var visibility = new float[_nodes.Count];
            for (int n = 0; n < _nodes.Count; n++)
            {
                visibility[n] = (float)visibleCells[n] / cells.Count;
            }

            List<Vec2> path = pathfinder.FindPath(_nodes[a].Center, _nodes[b].Center, MoveMode.Normal);
            return new KeyTerrainEdge(a, b, path, length, (float)forestCells / cells.Count, visibility);
        }
    }
}
