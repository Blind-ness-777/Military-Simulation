using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 요지 그래프와 격자 경로 탐색 테스트. 지형·노출을 직접 만들어 이동 방식별로 경로가 갈리는지,
    /// 그래프가 이웃 요지끼리만 잇는지, 시야 영역이 지형에 가리는지를 고정한다.
    /// </summary>
    public class KeyTerrainPathTests
    {
        private const float CellSize = 10f;

        [Test]
        public void PlanPath_OpenFlatGroundWithoutThreat_IsStraight()
        {
            Simulation simulation = CreateFlat(100, 60);

            List<Vec2> path = simulation.PlanPath(new Vec2(100f, 200f), new Vec2(900f, 300f), MoveMode.Normal);

            Assert.AreEqual(1, path.Count, "펴기로 중간 점이 모두 빠진다");
            Assert.AreEqual(new Vec2(900f, 300f), path[0]);
        }

        [Test]
        public void PlanPath_StealthGoesAroundExposedGround_FastCrossesIt()
        {
            // 가운데(x 400~600, y 0~440)가 관측자에게 보이는 땅. 위쪽(y 450 이상)으로 돌면 안 보인다.
            Simulation simulation = CreateFlat(100, 60);
            ExposureField exposure = CreateRectExposure(simulation.Map, 40, 60, 0, 44, 1f);
            var from = new Vec2(100f, 200f);
            var to = new Vec2(900f, 200f);

            List<Vec2> stealth = simulation.PlanPath(from, to, MoveMode.Stealth, exposure);
            List<Vec2> fast = simulation.PlanPath(from, to, MoveMode.Fast, exposure);

            Assert.Greater(0.1f, GetExposedFraction(exposure, from, stealth), "은밀은 보이는 땅을 거의 밟지 않는다");
            Assert.Greater(GetExposedFraction(exposure, from, fast), 0.15f, "신속은 노출을 감수하고 가로지른다");
            Assert.Greater(250f, GetMaxY(fast));
        }

        [Test]
        public void PlanPath_StealthUsesForestCorridor()
        {
            // 출발·도착 사이는 개활지, 100m 위쪽에 동서로 긴 숲 띠(y 300~350).
            Simulation simulation = CreateFlat(100, 60);
            for (int x = 0; x < 100; x++)
            {
                for (int y = 30; y < 35; y++)
                {
                    simulation.Map.SetCellTerrain(x, y, TerrainType.Forest);
                }
            }

            List<Vec2> stealth = simulation.PlanPath(new Vec2(100f, 200f), new Vec2(900f, 200f), MoveMode.Stealth);

            bool usesForest = false;
            foreach (Vec2 point in stealth)
            {
                usesForest |= simulation.Map.GetTerrainAt(point) == TerrainType.Forest;
            }

            Assert.IsTrue(usesForest, "은밀 이동은 개활지 대신 숲 띠를 탄다");
        }

        [Test]
        public void PlanPath_SameInput_GivesSamePath()
        {
            Simulation simulation = TestScenario.Create(1);
            ExposureField exposure = CreateRectExposure(simulation.Map, 30, 70, 50, 99, 1f);

            List<Vec2> first = simulation.PlanPath(new Vec2(335f, 80f), new Vec2(450f, 600f), MoveMode.Stealth, exposure);
            List<Vec2> second = simulation.PlanPath(new Vec2(335f, 80f), new Vec2(450f, 600f), MoveMode.Stealth, exposure);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i], second[i]);
            }
        }

        [Test]
        public void PlanPath_EndsAtDestinationAndStaysInsideMap()
        {
            Simulation simulation = TestScenario.Create(1);
            ExposureField exposure = CreateRectExposure(simulation.Map, 30, 70, 50, 99, 1f);

            List<Vec2> path = simulation.PlanPath(new Vec2(50f, 50f), new Vec2(950f, 950f), MoveMode.Stealth, exposure);

            Assert.AreEqual(new Vec2(950f, 950f), path[path.Count - 1]);
            foreach (Vec2 point in path)
            {
                Assert.IsTrue(simulation.Map.IsInside(point), $"지도 밖 점: {point}");
            }
        }

        [Test]
        public void Graph_NodeInTheMiddle_SplitsTheDirectEdge()
        {
            Simulation simulation = CreateFlat(100, 60);
            simulation.AddKeyTerrain("서", KeyTerrainKind.StagingArea, new Vec2(100f, 300f), 30f);
            simulation.AddKeyTerrain("가운데", KeyTerrainKind.Hill, new Vec2(500f, 300f), 30f);
            simulation.AddKeyTerrain("동", KeyTerrainKind.FirePosition, new Vec2(900f, 300f), 30f);

            KeyTerrainGraph graph = simulation.KeyTerrain;

            Assert.AreEqual(2, graph.Edges.Count);
            Assert.IsTrue(HasEdge(graph, 0, 1));
            Assert.IsTrue(HasEdge(graph, 1, 2));
            Assert.IsFalse(HasEdge(graph, 0, 2), "가운데 요지를 지나는 쌍은 구간이 아니다");
        }

        [Test]
        public void Graph_NearlySameLengthDetour_RemovesDirectEdge()
        {
            // 가운데 요지가 직선에서 조금 비켜나 있어도, 거쳐 가는 길이 거의 같으면 직행 구간은 없다.
            Simulation simulation = CreateFlat(100, 60);
            simulation.AddKeyTerrain("서", KeyTerrainKind.StagingArea, new Vec2(100f, 200f), 20f);
            simulation.AddKeyTerrain("북쪽 고지", KeyTerrainKind.Hill, new Vec2(500f, 300f), 20f);
            simulation.AddKeyTerrain("동", KeyTerrainKind.FirePosition, new Vec2(900f, 200f), 20f);

            KeyTerrainGraph graph = simulation.KeyTerrain;

            Assert.IsFalse(HasEdge(graph, 0, 2));
            Assert.IsTrue(HasEdge(graph, 0, 1));
            Assert.IsTrue(HasEdge(graph, 1, 2));
        }

        [Test]
        public void Graph_Viewshed_IsBlockedByRidge()
        {
            // x 500~520m에 높이 20m 둑. 서쪽 요지에서 둑 너머(동쪽 멀리)는 안 보인다.
            Simulation simulation = CreateFlat(100, 60);
            for (int y = 0; y < 60; y++)
            {
                simulation.Map.SetCellHeight(50, y, 20f);
                simulation.Map.SetCellHeight(51, y, 20f);
            }

            simulation.AddKeyTerrain("서쪽 관측소", KeyTerrainKind.Hill, new Vec2(300f, 300f), 20f);
            KeyTerrainGraph graph = simulation.KeyTerrain;

            Assert.IsTrue(graph.IsVisibleFrom(0, new Vec2(450f, 300f)));
            Assert.IsFalse(graph.IsVisibleFrom(0, new Vec2(700f, 300f)));
        }

        [Test]
        public void Graph_EdgeThroughForest_HasFullCoverRatio()
        {
            // 일반 이동 비용은 숲(느림)을 돌아가는 쪽을 고르므로, 지도 전체를 숲으로 만든다.
            Simulation simulation = CreateFlat(100, 60);
            for (int x = 0; x < 100; x++)
            {
                for (int y = 0; y < 60; y++)
                {
                    simulation.Map.SetCellTerrain(x, y, TerrainType.Forest);
                }
            }

            simulation.AddKeyTerrain("서쪽 숲", KeyTerrainKind.StagingArea, new Vec2(200f, 300f), 20f);
            simulation.AddKeyTerrain("동쪽 숲", KeyTerrainKind.ForestEdge, new Vec2(600f, 300f), 20f);

            KeyTerrainEdge edge = simulation.KeyTerrain.Edges[0];

            Assert.AreEqual(1f, edge.CoverRatio, 1e-4f);
            Assert.AreEqual(400f, edge.Length, 1f);
        }

        [Test]
        public void Graph_BuildExposure_AddsNodeWeightsWhereVisible()
        {
            Simulation simulation = CreateFlat(100, 60);
            simulation.AddKeyTerrain("가", KeyTerrainKind.Hill, new Vec2(300f, 300f), 20f);
            simulation.AddKeyTerrain("나", KeyTerrainKind.Hill, new Vec2(700f, 300f), 20f);
            KeyTerrainGraph graph = simulation.KeyTerrain;

            ExposureField exposure = graph.BuildExposure(new[] { 0.5f, 0.25f });

            // 평지라 둘 다 보이는 지점(관측 거리 800m 안).
            Assert.AreEqual(0.75f, exposure.GetAt(new Vec2(500f, 300f)), 1e-4f);
        }

        [Test]
        public void AddKeyTerrain_AfterGraphIsBuilt_Throws()
        {
            Simulation simulation = CreateFlat(100, 60);
            simulation.AddKeyTerrain("가", KeyTerrainKind.Hill, new Vec2(300f, 300f), 20f);
            Assert.AreEqual(1, simulation.KeyTerrain.Nodes.Count);

            bool threw = false;
            try
            {
                simulation.AddKeyTerrain("나", KeyTerrainKind.Hill, new Vec2(700f, 300f), 20f);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.IsTrue(threw);
        }

        private static Simulation CreateFlat(int width, int height)
        {
            return new Simulation(new MapData(width, height, CellSize), new RulesConfig(), 1, 0);
        }

        /// <summary>칸 범위(양 끝 포함)를 노출 value로 채운 노출 지도.</summary>
        private static ExposureField CreateRectExposure(MapData map, int minX, int maxX, int minY, int maxY, float value)
        {
            var visible = new bool[map.Width * map.Height];
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    visible[y * map.Width + x] = true;
                }
            }

            var field = new ExposureField(map);
            field.AddVisible(visible, value);
            return field;
        }

        /// <summary>경로를 5m 간격으로 따라가며 노출 칸을 밟은 비율.</summary>
        private static float GetExposedFraction(ExposureField exposure, Vec2 from, List<Vec2> path)
        {
            int samples = 0;
            int exposed = 0;
            Vec2 previous = from;
            foreach (Vec2 point in path)
            {
                int pieces = Math.Max(1, (int)(Vec2.Distance(previous, point) / 5f));
                for (int i = 1; i <= pieces; i++)
                {
                    samples++;
                    if (exposure.GetAt(Vec2.Lerp(previous, point, (float)i / pieces)) > 0f)
                    {
                        exposed++;
                    }
                }

                previous = point;
            }

            return (float)exposed / samples;
        }

        private static float GetMaxY(List<Vec2> path)
        {
            float max = float.MinValue;
            foreach (Vec2 point in path)
            {
                max = Math.Max(max, point.Y);
            }

            return max;
        }

        private static bool HasEdge(KeyTerrainGraph graph, int a, int b)
        {
            foreach (KeyTerrainEdge edge in graph.Edges)
            {
                if ((edge.FromIndex == a && edge.ToIndex == b) || (edge.FromIndex == b && edge.ToIndex == a))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
