using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 제압과 엄폐 규칙 테스트. 제압 = "못 움직임"이 아니라 "앞으로 못 감"이다:
    /// 위협 쪽 전진은 거의 못 하고, 옆·뒤·엄폐 쪽은 느리게 가고, 명령이 없으면 스스로 엄폐로 간다.
    /// 확률이 끼지 않게 제압·소대 인지·사격 방침을 직접 만들어 검사한다.
    /// </summary>
    public class SuppressionCoverTests
    {
        private const float CellSize = 10f;
        private const int MaxTicks = 600;

        [Test]
        public void Pinned_AdvancingTowardThreat_IsMuchSlowerThanMovingSideways()
        {
            float towardThreat = MeasurePinnedStep(new Vec2(600f, 200f));
            float sideways = MeasurePinnedStep(new Vec2(300f, 500f));

            Assert.Greater(towardThreat, 0f, "제압돼도 아주 조금은 나아간다");
            Assert.Greater(sideways, towardThreat * 3f, "옆으로는 위협 쪽보다 훨씬 빨리 간다");
        }

        [Test]
        public void Pinned_WithoutOrders_CrawlsToForestAndRestoresWatch()
        {
            // 숲 칸은 청과 홍을 잇는 선 밖에 둔다(선 위에 있으면 숲이 시야를 가려 이미 엄폐다).
            Simulation simulation = CreatePinnedSetup(out Platoon blue, out Platoon red);
            simulation.Map.SetCellTerrain(30, 23, TerrainType.Forest);
            blue.SetWatch(45f, 90f);

            Vec2 start = blue.Position;
            RunUntilCoverMoveEnds(simulation, blue);

            // 숲 칸 자체이거나, 그 숲에 가려 홍에서 안 보이는 칸이면 엄폐다.
            Assert.Greater(Vec2.Distance(start, blue.Position), 1f);
            Assert.IsTrue(IsHiddenOrInForest(simulation, blue, red), $"엄폐가 아닌 곳에서 멈춤: {blue.Position}");
            Assert.IsFalse(blue.IsMoving);
            Assert.IsTrue(blue.IsWatching, "엄폐에 닿으면 경계를 되살린다");
            Assert.AreEqual(45f, blue.WatchBearingDegrees, 0.01f);
        }

        [Test]
        public void Pinned_BehindRidgeFromKnownThreat_CountsAsCover()
        {
            // 동서로 길게 높이 6m 둑(y 220~240m). 위협은 둑 북쪽 멀리, 청은 둑 바로 북쪽에 노출돼 있다.
            Simulation simulation = CreatePinnedSetup(out Platoon blue, out Platoon red, new Vec2(300f, 255f), new Vec2(300f, 520f));
            for (int x = 0; x < simulation.Map.Width; x++)
            {
                simulation.Map.SetCellHeight(x, 22, 6f);
                simulation.Map.SetCellHeight(x, 23, 6f);
            }

            RunUntilCoverMoveEnds(simulation, blue);

            Assert.Greater(220f, blue.Position.Y, "둑 남쪽(위협에서 시야가 가리는 칸)으로 간다");
            Assert.AreEqual(TerrainType.Open, simulation.Map.GetTerrainAt(blue.Position));
        }

        [Test]
        public void Pinned_WithoutCoverNearby_StaysInPlace()
        {
            Simulation simulation = CreatePinnedSetup(out Platoon blue, out _);
            Vec2 start = blue.Position;

            simulation.Tick();

            Assert.IsFalse(blue.IsMovingToCover);
            Assert.AreEqual(start, blue.Position);
        }

        [Test]
        public void Pinned_WhileWaitingAtWaypoint_DoesNotMove()
        {
            Simulation simulation = CreatePinnedSetup(out Platoon blue, out _);
            simulation.Map.SetCellTerrain(30, 23, TerrainType.Forest);
            var wait = new WaitCondition(1, false, null, null);
            var waitPoint = new Waypoint(blue.Position, MoveMode.Normal, ContactBehavior.TakeCover, wait, null);
            blue.SetPath(new[] { new Waypoint(new Vec2(300f, 500f), MoveMode.Normal, ContactBehavior.TakeCover) }, false);
            blue.BeginWait(waitPoint);
            Vec2 start = blue.Position;

            simulation.Tick();

            Assert.IsTrue(blue.IsWaiting);
            Assert.IsFalse(blue.IsMovingToCover);
            Assert.AreEqual(start, blue.Position);
        }

        [Test]
        public void ContactBehavior_TakeCover_MovesToNearbyCoverThenStops()
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 100f };
            var simulation = new Simulation(new MapData(120, 60, CellSize), rules, 7, 0);
            // 청 뒤쪽(홍 반대편) 숲. 청과 홍 사이에 두면 숲이 시야를 가려 이미 엄폐로 판정된다.
            for (int y = 15; y <= 17; y++)
            {
                simulation.Map.SetCellTerrain(28, y, TerrainType.Forest);
            }

            Platoon blue = simulation.AddPlatoon("청", Side.Blue, new Vec2(300f, 200f), 90f);
            Platoon red = simulation.AddPlatoon("홍", Side.Red, new Vec2(600f, 400f), 180f);
            simulation.OrderMove(blue, new[] { new Vec2(1100f, 200f) }, MoveMode.Normal, false, ContactBehavior.TakeCover);

            bool sawCoverMove = false;
            for (int i = 0; i < MaxTicks && !(sawCoverMove && !blue.IsMovingToCover); i++)
            {
                simulation.Tick();
                sawCoverMove |= blue.IsMovingToCover;
            }

            Assert.IsTrue(sawCoverMove, "접촉 때 엄폐 밖이면 엄폐로 간다");
            Assert.IsTrue(IsHiddenOrInForest(simulation, blue, red), $"엄폐가 아닌 곳에서 멈춤: {blue.Position}");
            Assert.IsFalse(blue.IsMoving, "엄폐에 닿으면 남은 경로 없이 멈춘다");
        }

        [Test]
        public void NewOrder_CancelsCoverMove()
        {
            Simulation simulation = CreatePinnedSetup(out Platoon blue, out _);
            simulation.Map.SetCellTerrain(30, 23, TerrainType.Forest);
            simulation.Tick();
            Assert.IsTrue(blue.IsMovingToCover);

            blue.SetPath(new[] { new Waypoint(new Vec2(300f, 500f), MoveMode.Normal, ContactBehavior.PushThrough) }, false);

            Assert.IsFalse(blue.IsMovingToCover);
        }

        private static bool IsHiddenOrInForest(Simulation simulation, Platoon blue, Platoon red)
        {
            return simulation.Map.GetTerrainAt(blue.Position) == TerrainType.Forest
                || !LineOfSight.HasLineOfSight(
                    simulation.Map, red.Position, red.UnitType.EyeHeight, blue.Position, blue.UnitType.TargetHeight,
                    simulation.Rules.ForestCanopyHeight);
        }

        /// <summary>제압된 청 소대가 지정한 곳으로 한 틱 움직인 거리.</summary>
        private static float MeasurePinnedStep(Vec2 destination)
        {
            Simulation simulation = CreatePinnedSetup(out Platoon blue, out _);
            blue.SetPath(new[] { new Waypoint(destination, MoveMode.Normal, ContactBehavior.PushThrough) }, false);
            Vec2 start = blue.Position;

            simulation.Tick();

            Assert.IsTrue(blue.IsPinned);
            return Vec2.Distance(start, blue.Position);
        }

        private static Simulation CreatePinnedSetup(out Platoon blue, out Platoon red)
        {
            return CreatePinnedSetup(out blue, out red, new Vec2(300f, 200f), new Vec2(600f, 200f));
        }

        /// <summary>
        /// 1200m × 600m 평지. 청은 제압된 상태(제압 100)로 홍의 위치를 알고 있다(추적 중).
        /// 양쪽 다 사격 금지라 사상·사기 변화 없이 이동 규칙만 본다.
        /// </summary>
        private static Simulation CreatePinnedSetup(out Platoon blue, out Platoon red, Vec2 bluePosition, Vec2 redPosition)
        {
            var simulation = new Simulation(new MapData(120, 60, CellSize), new RulesConfig(), 11, 0);
            blue = simulation.AddPlatoon("청", Side.Blue, bluePosition, 90f);
            red = simulation.AddPlatoon("홍", Side.Red, redPosition, 270f);
            blue.FirePolicy = FirePolicy.HoldFire;
            red.FirePolicy = FirePolicy.HoldFire;
            blue.Suppression = 100f;
            blue.IsPinned = true;
            blue.KnownEnemies.Add(red.Id, new LocalContact(red.Id)
            {
                LastKnownPosition = red.Position,
                LastSeenSeconds = 0,
                IsTracking = true
            });
            return simulation;
        }

        private static void RunUntilCoverMoveEnds(Simulation simulation, Platoon blue)
        {
            bool sawCoverMove = false;
            for (int i = 0; i < MaxTicks; i++)
            {
                simulation.Tick();
                sawCoverMove |= blue.IsMovingToCover;
                if (sawCoverMove && !blue.IsMovingToCover)
                {
                    return;
                }
            }

            Assert.Fail(sawCoverMove ? "엄폐 이동이 끝나지 않았습니다." : "엄폐 이동이 시작되지 않았습니다.");
        }
    }
}
