using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 명령 전달·통신 상태·자체 판단 모드 테스트.
    /// 현대전 기준: 평시는 지연이 거의 없고, 지형 음영·재밍·통신 수단 손실로만 저하·두절된다.
    /// 두절 중 명령은 보류했다가 회복되면 전달한다.
    /// </summary>
    public class CommandNetworkTests
    {
        private const float CellSize = 10f;
        private const int MaxTicks = 900;

        [Test]
        public void WithoutHeadquarters_OrdersApplyImmediately()
        {
            var simulation = new Simulation(new MapData(5, 60, CellSize), new RulesConfig(), 1, 0);
            Platoon platoon = simulation.AddPlatoon("소대", Side.Blue, new Vec2(25f, 50f), 0f);

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 400f) }, MoveMode.Normal, false);

            Assert.IsTrue(platoon.IsMoving);
            Assert.AreEqual(0, simulation.PendingOrders.Count);
        }

        [Test]
        public void HeadquartersOwnOrders_ApplyImmediately()
        {
            Simulation simulation = CreateFlatNetwork(out Platoon headquarters, out _);

            simulation.OrderMove(headquarters, new[] { new Vec2(25f, 400f) }, MoveMode.Normal, false);

            Assert.IsTrue(headquarters.IsMoving);
        }

        [Test]
        public void NormalComms_OrderArrivesAfterShortDelay()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);
            int delay = simulation.Rules.NormalCommandDelaySeconds;

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 550f) }, MoveMode.Normal, false);
            Assert.IsFalse(platoon.IsMoving);

            for (int i = 0; i < delay - 1; i++)
            {
                simulation.Tick();
            }

            Assert.IsFalse(platoon.IsMoving);
            simulation.Tick();
            Assert.IsTrue(platoon.IsMoving);
        }

        [Test]
        public void AppendedMove_DoesNotCancelPendingMove()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 450f) }, MoveMode.Normal, false);
            simulation.OrderMove(platoon, new[] { new Vec2(25f, 550f) }, MoveMode.Normal, true);
            Assert.AreEqual(2, simulation.PendingOrders.Count);

            for (int i = 0; i < 5; i++)
            {
                simulation.Tick();
            }

            Assert.AreEqual(2, platoon.Waypoints.Count);
            Assert.AreEqual(new Vec2(25f, 550f), platoon.Waypoints[1].Position);
        }

        [Test]
        public void FirePolicyOrder_DoesNotCancelPendingMove()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 450f) }, MoveMode.Normal, false);
            simulation.OrderFirePolicy(platoon, FirePolicy.HoldFire);
            RunTicks(simulation, 5);

            Assert.IsTrue(platoon.IsMoving);
            Assert.AreEqual(FirePolicy.HoldFire, platoon.FirePolicy);
        }

        [Test]
        public void NewMoveOrder_ReplacesPendingMove()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 450f) }, MoveMode.Normal, false);
            simulation.OrderMove(platoon, new[] { new Vec2(25f, 550f) }, MoveMode.Normal, false);
            Assert.AreEqual(1, simulation.PendingOrders.Count);

            RunTicks(simulation, 5);
            Assert.AreEqual(new Vec2(25f, 550f), platoon.Waypoints[0].Position);
        }

        [Test]
        public void TerrainShadow_HoldsOrderUntilCommsRestored()
        {
            // 높이 60m 능선(y=300) 너머의 소대. 가시선 무전만 있으면 두절된다.
            Simulation simulation = CreateRidgeNetwork(out Platoon headquarters, out Platoon platoon);

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 580f) }, MoveMode.Normal, false);
            Assert.AreEqual(CommsState.Cut, platoon.Comms);
            Assert.IsTrue(simulation.PendingOrders[0].IsWaitingForComms);

            for (int i = 0; i < 30; i++)
            {
                simulation.Tick();
            }

            Assert.IsFalse(platoon.IsMoving);

            // 본부가 능선 위로 올라가면 무전이 닿고, 보류된 명령이 출발한다.
            simulation.OrderMove(headquarters, new[] { new Vec2(25f, 300f) }, MoveMode.Normal, false);
            for (int i = 0; i < MaxTicks && !platoon.IsMoving; i++)
            {
                simulation.Tick();
            }

            Assert.IsTrue(platoon.IsMoving);
        }

        [Test]
        public void Satcom_IgnoresTerrainShadowWhenSkyIsOpen()
        {
            Simulation simulation = CreateRidgeNetwork(out Platoon headquarters, out Platoon platoon);
            headquarters.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;
            platoon.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;

            Assert.AreEqual(CommsState.Normal, simulation.EvaluateComms(platoon));
        }

        [Test]
        public void Satcom_BlockedInForest_FallsBackToLineOfSightRadio()
        {
            MapData map = CreateRidgeMap();
            for (int y = 48; y < 53; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    map.SetCellTerrain(x, y, TerrainType.Forest);
                }
            }

            Simulation simulation = CreateNetwork(map, new Vec2(25f, 50f), new Vec2(25f, 500f), out Platoon headquarters, out Platoon platoon);
            headquarters.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;
            platoon.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;

            Assert.AreEqual(CommsState.Cut, simulation.EvaluateComms(platoon));
        }

        [Test]
        public void BlockJamming_HoldsOrderAndRemovalDeliversIt()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);
            var zone = new JammingZone(platoon.Position, 100f, JammingStrength.Block, Side.Blue);
            simulation.AddJammingZone(zone);

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 550f) }, MoveMode.Normal, false);
            for (int i = 0; i < 20; i++)
            {
                simulation.Tick();
            }

            Assert.IsFalse(platoon.IsMoving);

            simulation.RemoveJammingZone(zone);
            for (int i = 0; i < 10 && !platoon.IsMoving; i++)
            {
                simulation.Tick();
            }

            Assert.IsTrue(platoon.IsMoving);
        }

        [Test]
        public void DegradeJamming_UsesDegradedDelay()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);
            simulation.AddJammingZone(new JammingZone(platoon.Position, 100f, JammingStrength.Degrade, Side.Blue));

            simulation.OrderMove(platoon, new[] { new Vec2(25f, 550f) }, MoveMode.Normal, false);

            Assert.AreEqual(CommsState.Degraded, platoon.Comms);
            Assert.AreEqual(simulation.Rules.DegradedCommandDelaySeconds, simulation.PendingOrders[0].DeliverAtSeconds);
        }

        [Test]
        public void JammingOnOtherSide_HasNoEffect()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);
            simulation.AddJammingZone(new JammingZone(platoon.Position, 100f, JammingStrength.Block, Side.Red));

            Assert.AreEqual(CommsState.Normal, simulation.EvaluateComms(platoon));
        }

        [Test]
        public void RadioOperatorLost_CutsBeyondVoiceRangeOnly()
        {
            Simulation simulation = CreateFlatNetwork(out Platoon headquarters, out Platoon platoon);
            headquarters.CommsEquipment = CommsEquipment.None;

            Assert.AreEqual(CommsState.Cut, simulation.EvaluateComms(platoon));

            Platoon nearby = simulation.AddPlatoon("근접", Side.Blue, headquarters.Position + new Vec2(0f, 30f), 0f);
            Assert.AreEqual(CommsState.Direct, simulation.EvaluateComms(nearby));
        }

        [Test]
        public void HeadquartersDestroyed_CutsEveryoneAndIsReported()
        {
            Simulation simulation = CreateFlatNetwork(out Platoon headquarters, out Platoon platoon);
            headquarters.Killed = headquarters.Effective;
            headquarters.Effective = 0;

            simulation.Tick();

            Assert.IsTrue(simulation.IsHeadquartersLost(Side.Blue));
            Assert.AreEqual(CommsState.Cut, platoon.Comms);
        }

        [Test]
        public void LongCut_EntersSelfDirectedMode_AndRecoveryEndsIt()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);
            var zone = new JammingZone(platoon.Position, 100f, JammingStrength.Block, Side.Blue);
            simulation.AddJammingZone(zone);

            RunTicks(simulation, simulation.Rules.SelfDirectedDelaySeconds + simulation.Rules.CommsCheckIntervalSeconds + 1);
            Assert.IsTrue(platoon.IsSelfDirected);

            simulation.RemoveJammingZone(zone);
            RunTicks(simulation, 2);
            Assert.IsFalse(platoon.IsSelfDirected);
        }

        [Test]
        public void SelfDirectedRetreatThenWithdraw_NeverMovesBackTowardEnemy()
        {
            Simulation simulation = CreateFlatNetwork(out _, out Platoon platoon);

            // 남(y=200)에서 북(y=500)으로 300m 전진한 뒤 두절.
            simulation.OrderMove(platoon, new[] { new Vec2(25f, 500f) }, MoveMode.Normal, false);
            for (int i = 0; i < MaxTicks && (platoon.IsMoving || simulation.PendingOrders.Count > 0); i++)
            {
                simulation.Tick();
            }

            // 후퇴하는 동안에도 계속 두절되도록 맵 전체를 덮는 재밍.
            simulation.AddJammingZone(new JammingZone(platoon.Position, 2000f, JammingStrength.Block, Side.Blue));
            RunTicks(simulation, simulation.Rules.SelfDirectedDelaySeconds + simulation.Rules.CommsCheckIntervalSeconds + 1);
            Assert.IsTrue(platoon.IsSelfDirected);

            // 1단계: 전투 가능 50% 이하 → 약 200m 후퇴. 모든 지점이 현재보다 남쪽이어야 한다.
            SetCasualties(platoon, 16);
            simulation.Tick();
            Assert.IsTrue(platoon.IsMoving);
            AssertPathRetreatsSouth(platoon);
            float retreatEndY = platoon.Waypoints[platoon.Waypoints.Count - 1].Position.Y;
            Assert.IsTrue(retreatEndY > 280f && retreatEndY < 320f);

            RunUntilStopped(simulation, platoon);

            // 2단계: 30% 이하 → 출발점까지 철수. 방금 물러난 길을 따라 다시 북쪽으로 가면 안 된다.
            SetCasualties(platoon, 22);
            simulation.Tick();
            Assert.IsTrue(platoon.IsMoving);
            AssertPathRetreatsSouth(platoon);
            Assert.IsTrue(platoon.Waypoints[platoon.Waypoints.Count - 1].Position.Y < 210f);
        }

        private static void AssertPathRetreatsSouth(Platoon platoon)
        {
            float previousY = platoon.Position.Y;
            foreach (Waypoint waypoint in platoon.Waypoints)
            {
                Assert.IsTrue(waypoint.Position.Y <= previousY + 0.01f);
                previousY = waypoint.Position.Y;
            }
        }

        private static void SetCasualties(Platoon platoon, int totalCasualties)
        {
            platoon.Effective = platoon.InitialStrength - totalCasualties;
            platoon.Wounded = totalCasualties;
            platoon.Killed = 0;
        }

        private static void RunTicks(Simulation simulation, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                simulation.Tick();
            }
        }

        private static void RunUntilStopped(Simulation simulation, Platoon platoon)
        {
            for (int i = 0; i < MaxTicks && platoon.IsMoving; i++)
            {
                simulation.Tick();
            }
        }

        /// <summary>평지. 본부 (25, 50), 소대 (25, 200). 목소리 거리(50m) 밖이라 무전으로 지휘한다.</summary>
        private static Simulation CreateFlatNetwork(out Platoon headquarters, out Platoon platoon)
        {
            return CreateNetwork(new MapData(5, 60, CellSize), new Vec2(25f, 50f), new Vec2(25f, 200f), out headquarters, out platoon);
        }

        /// <summary>y=300에 높이 60m 능선. 본부 (25, 50), 소대 (25, 500).</summary>
        private static Simulation CreateRidgeNetwork(out Platoon headquarters, out Platoon platoon)
        {
            return CreateNetwork(CreateRidgeMap(), new Vec2(25f, 50f), new Vec2(25f, 500f), out headquarters, out platoon);
        }

        private static Simulation CreateNetwork(
            MapData map, Vec2 headquartersPosition, Vec2 platoonPosition, out Platoon headquarters, out Platoon platoon)
        {
            var simulation = new Simulation(map, new RulesConfig(), 1, 0);
            platoon = simulation.AddPlatoon("소대", Side.Blue, platoonPosition, 0f);
            headquarters = simulation.AddPlatoon("본부", Side.Blue, headquartersPosition, 0f, 6, PlatoonRole.Headquarters);
            return simulation;
        }

        private static MapData CreateRidgeMap()
        {
            var map = new MapData(5, 60, CellSize);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    float offset = (map.GetCellCenter(x, y).Y - 300f) / 90f;
                    map.SetCellHeight(x, y, 60f * (float)System.Math.Exp(-offset * offset));
                }
            }

            return map;
        }
    }
}
