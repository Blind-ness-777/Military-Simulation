using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 교전 규칙 테스트. 인원 검산, 사격 방침, 엄폐 효과, 접촉 시 행동을 고정한다.
    /// 확률이 끼는 판정은 결과 대신 확률 값의 대소 관계로 검사해서, 수치를 조정해도 깨지지 않게 한다.
    /// </summary>
    public class CombatRulingTests
    {
        private const float CellSize = 10f;
        private const int MaxTicks = 600;

        [Test]
        public void StrengthLedger_HoldsThroughScenarioEngagement()
        {
            Simulation simulation = TestScenario.Create(12345);
            simulation.OrderMove(simulation.Platoons[0], new[] { new Vec2(300f, 410f) }, MoveMode.Stealth, false);
            simulation.OrderMove(simulation.Platoons[1], new[] { new Vec2(450f, 480f), new Vec2(450f, 560f) }, MoveMode.Normal, false);

            // 검산이 틀리면 Tick()이 예외를 던진다.
            for (int i = 0; i < 1800; i++)
            {
                simulation.Tick();
            }

            foreach (Platoon platoon in simulation.Platoons)
            {
                Assert.AreEqual(platoon.InitialStrength, platoon.Effective + platoon.Wounded + platoon.Killed);
            }
        }

        [Test]
        public void HoldFire_NeverShootsEvenWithConfirmedContact()
        {
            Simulation simulation = CreateFastDetectionSimulation(out Platoon blue, out Platoon red);
            simulation.OrderHold(blue);
            simulation.OrderFirePolicy(blue, FirePolicy.HoldFire);
            simulation.OrderFirePolicy(red, FirePolicy.HoldFire);

            for (int i = 0; i < MaxTicks; i++)
            {
                simulation.Tick();
                Assert.IsFalse(blue.IsFiring);
            }

            Assert.IsTrue(simulation.GetIntel(Side.Blue).TryGetContact(red.Id, out _));
            Assert.AreEqual(red.InitialStrength, red.Effective);
        }

        [Test]
        public void Cover_ReducesCasualtyChance()
        {
            var map = new MapData(60, 60, CellSize);
            for (int y = 30; y < 40; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    map.SetCellTerrain(x, y, TerrainType.Forest);
                }
            }

            var simulation = new Simulation(map, new RulesConfig(), 1, 0);
            Platoon shooter = simulation.AddPlatoon("사수", Side.Blue, new Vec2(250f, 100f), 0f);
            Platoon inForest = simulation.AddPlatoon("숲", Side.Red, new Vec2(50f, 350f), 180f);
            Platoon inOpen = simulation.AddPlatoon("개활지", Side.Red, new Vec2(450f, 350f), 180f);

            double forest = simulation.GetCasualtyChancePerShooterPerTick(shooter, inForest);
            double openStationary = simulation.GetCasualtyChancePerShooterPerTick(shooter, inOpen);

            simulation.OrderMove(inOpen, new[] { new Vec2(450f, 500f) }, MoveMode.Normal, false);
            double openMoving = simulation.GetCasualtyChancePerShooterPerTick(shooter, inOpen);

            Assert.Greater(openStationary, forest);
            Assert.Greater(openMoving, openStationary);
        }

        [Test]
        public void ContactBehavior_TakeCover_StopsOnContact()
        {
            Platoon blue = RunUntilContact(ContactBehavior.TakeCover, out _);
            Assert.IsFalse(blue.IsMoving);
        }

        [Test]
        public void ContactBehavior_PushThrough_KeepsOriginalPath()
        {
            Platoon blue = RunUntilContact(ContactBehavior.PushThrough, out _);
            Assert.IsTrue(blue.IsMoving);
            Assert.AreEqual(new Vec2(1100f, 200f), blue.Waypoints[0].Position);
        }

        [Test]
        public void ContactBehavior_FallBack_ReturnsToSegmentStart()
        {
            Platoon blue = RunUntilContact(ContactBehavior.FallBack, out Vec2 start);
            Assert.IsTrue(blue.IsMoving);
            Assert.AreEqual(start, blue.Waypoints[0].Position);
        }

        /// <summary>
        /// 청 소대가 동쪽으로 이동하다가, 북쪽에 정지한 홍 소대와 접촉할 때까지 진행한다.
        /// 탐지 확률을 최대로 올려서 접촉 시점이 빨리, 거의 확실하게 오게 한다.
        /// </summary>
        private static Platoon RunUntilContact(ContactBehavior contactBehavior, out Vec2 start)
        {
            Simulation simulation = CreateFastDetectionSimulation(out Platoon blue, out _);
            start = blue.Position;
            simulation.OrderMove(blue, new[] { new Vec2(1100f, 200f) }, MoveMode.Normal, false, contactBehavior);

            for (int i = 0; i < MaxTicks; i++)
            {
                simulation.Tick();
                bool inContact = blue.IsFiring || blue.LastFiredUponSeconds == simulation.ElapsedSeconds;
                if (inContact)
                {
                    return blue;
                }
            }

            Assert.Fail("제한 시간 안에 접촉이 일어나지 않았습니다.");
            return blue;
        }

        /// <summary>1200m × 600m 평지. 청은 (300, 200), 홍은 (600, 400). 탐지 확률 최대.</summary>
        private static Simulation CreateFastDetectionSimulation(out Platoon blue, out Platoon red)
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 100f };
            var simulation = new Simulation(new MapData(120, 60, CellSize), rules, 7, 0);
            blue = simulation.AddPlatoon("청", Side.Blue, new Vec2(300f, 200f), 90f);
            red = simulation.AddPlatoon("홍", Side.Red, new Vec2(600f, 400f), 180f);
            return simulation;
        }
    }
}
