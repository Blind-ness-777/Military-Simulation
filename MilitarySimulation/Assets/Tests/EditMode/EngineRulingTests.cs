using System.Collections.Generic;
using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 판정 선례 테스트. 채팅 시뮬레이션 엔진 규칙 v2의 선례(P1~P12) 중
    /// 현재 엔진 범위에 해당하는 것과, 기획서 4.4 상태 규칙을 고정한다.
    /// 규칙 수치를 바꿨을 때 "같은 상황은 같게 판정"이 깨지지 않았는지 확인하는 용도다.
    /// </summary>
    public class EngineRulingTests
    {
        private const float CellSize = 10f;

        [Test]
        public void P5_ReverseSlope_HidesObserverBehindCrest()
        {
            MapData map = CreateRidgeMap();
            var rules = new RulesConfig();

            // 능선(y=300) 북쪽 100m 역사면의 관측자 → 남쪽 개활지의 표적
            bool canSee = LineOfSight.HasLineOfSight(
                map, new Vec2(15f, 400f), rules.EyeHeight, new Vec2(15f, 100f), rules.TargetHeight, rules.ForestCanopyHeight);

            Assert.IsFalse(canSee);
        }

        [Test]
        public void P5_OnCrest_SeesForwardSlope()
        {
            MapData map = CreateRidgeMap();
            var rules = new RulesConfig();

            bool canSee = LineOfSight.HasLineOfSight(
                map, new Vec2(15f, 300f), rules.EyeHeight, new Vec2(15f, 100f), rules.TargetHeight, rules.ForestCanopyHeight);

            Assert.IsTrue(canSee);
        }

        [Test]
        public void FlatOpenGround_HasLineOfSight()
        {
            var map = new MapData(3, 60, CellSize);
            var rules = new RulesConfig();

            bool canSee = LineOfSight.HasLineOfSight(
                map, new Vec2(15f, 10f), rules.EyeHeight, new Vec2(15f, 580f), rules.TargetHeight, rules.ForestCanopyHeight);

            Assert.IsTrue(canSee);
        }

        [Test]
        public void ForestBetween_BlocksLineOfSight()
        {
            MapData map = CreateForestBandMap();
            var rules = new RulesConfig();

            bool canSee = LineOfSight.HasLineOfSight(
                map, new Vec2(15f, 50f), rules.EyeHeight, new Vec2(15f, 400f), rules.TargetHeight, rules.ForestCanopyHeight);

            Assert.IsFalse(canSee);
        }

        [Test]
        public void ForestEdge_ObserverCanSeeOut()
        {
            MapData map = CreateForestBandMap();
            var rules = new RulesConfig();

            // 숲(y 200~260) 북쪽 가장자리 안쪽 5m에 선 관측자 → 북쪽 개활지
            bool canSee = LineOfSight.HasLineOfSight(
                map, new Vec2(15f, 255f), rules.EyeHeight, new Vec2(15f, 500f), rules.TargetHeight, rules.ForestCanopyHeight);

            Assert.IsTrue(canSee);
        }

        [Test]
        public void Movement_NormalSpeed_ArrivesOnExpectedTick()
        {
            var simulation = new Simulation(new MapData(3, 30, CellSize), new RulesConfig(), 1, 0);
            Platoon platoon = simulation.AddPlatoon("시험", Side.Blue, new Vec2(15f, 5f), 0f);

            // 일반 1.1 m/s, 109m → 99틱 후 108.9m(이동 중), 100틱째 도착
            simulation.OrderMove(platoon, new[] { new Vec2(15f, 114f) }, MoveMode.Normal, false);
            for (int i = 0; i < 99; i++)
            {
                simulation.Tick();
            }

            Assert.IsTrue(platoon.IsMoving);

            simulation.Tick();
            Assert.IsFalse(platoon.IsMoving);
        }

        [Test]
        public void Detection_WatchingTowardTarget_IsHigherThanWatchingAway()
        {
            Simulation simulation = CreateOpenFieldSimulation(out Platoon observer, out Platoon target);

            simulation.OrderWatch(observer, 0f, 90f);
            double toward = simulation.GetDetectionChancePerTick(observer, target);

            simulation.OrderWatch(observer, 180f, 90f);
            double away = simulation.GetDetectionChancePerTick(observer, target);

            simulation.OrderHold(observer);
            double stationary = simulation.GetDetectionChancePerTick(observer, target);

            Assert.Greater(toward, stationary);
            Assert.Greater(stationary, away);
        }

        [Test]
        public void Detection_MovingTarget_IsEasierToSpotThanStationary()
        {
            Simulation simulation = CreateOpenFieldSimulation(out Platoon observer, out Platoon target);

            double stationary = simulation.GetDetectionChancePerTick(observer, target);

            simulation.OrderMove(target, new[] { new Vec2(25f, 590f) }, MoveMode.Stealth, false);
            double stealth = simulation.GetDetectionChancePerTick(observer, target);

            simulation.OrderMove(target, new[] { new Vec2(25f, 590f) }, MoveMode.Fast, false);
            double fast = simulation.GetDetectionChancePerTick(observer, target);

            Assert.Greater(stealth, stationary);
            Assert.Greater(fast, stealth);
        }

        [Test]
        public void SameSeedAndOrders_ProduceSameEventLog()
        {
            List<string> first = RunScenarioLog(777);
            List<string> second = RunScenarioLog(777);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i], second[i]);
            }
        }

        /// <summary>폭 3칸, 길이 600m. y=300m에 높이 25m 능선.</summary>
        private static MapData CreateRidgeMap()
        {
            var map = new MapData(3, 60, CellSize);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    float offset = (map.GetCellCenter(x, y).Y - 300f) / 90f;
                    map.SetCellHeight(x, y, 25f * (float)System.Math.Exp(-offset * offset));
                }
            }

            return map;
        }

        /// <summary>폭 3칸, 길이 600m 평지. y 200~260m에 숲 띠.</summary>
        private static MapData CreateForestBandMap()
        {
            var map = new MapData(3, 60, CellSize);
            for (int y = 20; y < 26; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    map.SetCellTerrain(x, y, TerrainType.Forest);
                }
            }

            return map;
        }

        /// <summary>평지에서 관측자(남쪽)와 표적(북쪽 300m). 표적은 관측자의 정북.</summary>
        private static Simulation CreateOpenFieldSimulation(out Platoon observer, out Platoon target)
        {
            var simulation = new Simulation(new MapData(5, 60, CellSize), new RulesConfig(), 1, 0);
            observer = simulation.AddPlatoon("관측", Side.Blue, new Vec2(25f, 100f), 0f);
            target = simulation.AddPlatoon("표적", Side.Red, new Vec2(25f, 400f), 180f);
            return simulation;
        }

        private static List<string> RunScenarioLog(int seed)
        {
            Simulation simulation = TestScenario.Create(seed);
            simulation.OrderMove(simulation.Platoons[0], new[] { new Vec2(260f, 300f) }, MoveMode.Stealth, false);
            simulation.OrderMove(simulation.Platoons[1], new[] { new Vec2(450f, 480f) }, MoveMode.Normal, false);
            for (int i = 0; i < 900; i++)
            {
                simulation.Tick();
            }

            var log = new List<string>();
            foreach (SimEvent simEvent in simulation.Events)
            {
                log.Add($"{simEvent.ElapsedSeconds} {simEvent.Side} {simEvent.Text}");
            }

            return log;
        }
    }
}
