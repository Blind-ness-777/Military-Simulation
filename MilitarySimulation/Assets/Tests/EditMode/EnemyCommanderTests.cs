using System.Collections.Generic;
using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 적 지휘관 AI(데모 1단계: 방어하는 적) 테스트.
    /// 기본 상황: 평지, 탐지 확률 0(아무도 직접 보지 못함). 위협은 레드 상황판에 접촉을 직접 올려서 만든다.
    /// 판단 무작위는 0으로 두어 점수가 가장 높은 후보가 항상 뽑히게 한다(점수 계산은 EnemyCommander 주석 참고).
    /// </summary>
    public class EnemyCommanderTests
    {
        private const float CellSize = 10f;
        private const float ThreatRadius = 200f;
        private const float WatchArc = 120f;
        private const int FakeTargetIdBase = 1000;

        private static readonly Vec2 PositionCenter = new Vec2(300f, 600f);
        private static readonly Vec2 FallbackPoint = new Vec2(300f, 850f);

        [Test]
        public void EnemyNotOnSituationBoard_IsIgnored()
        {
            // 블루 소대가 진지 위협 반경 안에 실제로 있지만, 아무도 보지 못해 레드 상황판에는 없다.
            Simulation simulation = CreateDefense(0.35f, true, out EnemyCommander commander, out _, out Platoon reserve);
            Platoon blue = simulation.AddPlatoon("블루 소대", Side.Blue, new Vec2(300f, 480f), 0f);
            blue.FirePolicy = FirePolicy.HoldFire;

            RunTicks(simulation, 150);
            Assert.AreEqual(0, commander.Decisions.Count);
            Assert.IsFalse(reserve.IsMoving);

            // 같은 위치가 상황판에 올라오면 그때 판단한다.
            Contact contact = AddContact(simulation, blue.Id, blue.Position);
            RunTicks(simulation, 20, contact);
            Assert.AreEqual(1, commander.Decisions.Count);
            Assert.AreEqual("새 접촉", commander.Decisions[0].Trigger);
        }

        [Test]
        public void SingleContact_CautiousCommanderHolds()
        {
            Simulation simulation = CreateDefense(0.35f, true, out EnemyCommander commander, out _, out Platoon reserve);
            Contact contact = AddContact(simulation, FakeTargetIdBase, new Vec2(300f, 450f));

            RunTicks(simulation, 20, contact);

            Assert.AreEqual(1, commander.Decisions.Count);
            Assert.AreEqual(CommanderAction.Hold, commander.Decisions[0].Action);
            Assert.IsFalse(commander.IsReserveCommitted);
            Assert.IsFalse(reserve.IsMoving);
        }

        [Test]
        public void GrowingThreat_CommitsReserve_AndWatchesOnArrival()
        {
            Simulation simulation = CreateDefense(0.35f, true, out EnemyCommander commander, out _, out Platoon reserve);
            Contact first = AddContact(simulation, FakeTargetIdBase, new Vec2(280f, 450f));
            Contact second = AddContact(simulation, FakeTargetIdBase + 1, new Vec2(330f, 460f));

            RunTicks(simulation, 20, first, second);

            Assert.AreEqual(CommanderAction.Reinforce, commander.Decisions[0].Action);
            Assert.IsTrue(commander.IsReserveCommitted);
            Assert.IsTrue(reserve.IsMoving);
            Assert.AreEqual(PositionCenter, reserve.Waypoints[reserve.Waypoints.Count - 1].Position);

            // 200m를 일반 이동으로 가면 3분 남짓. 도착 보고를 받으면 위협 쪽 경계로 마무리한다.
            RunTicks(simulation, 300, first, second);
            Assert.IsFalse(reserve.IsMoving);
            Assert.IsTrue(Vec2.Distance(reserve.Position, PositionCenter) < 1f);
            Assert.IsTrue(reserve.IsWatching);
            Assert.IsTrue(Bearing.Difference(reserve.WatchBearingDegrees, 180f) < 30f);
        }

        [Test]
        public void HeavyDamage_WithoutReserve_FallsBack()
        {
            Simulation simulation = CreateDefense(0.35f, false, out EnemyCommander commander, out Platoon garrison, out _);
            SetCasualties(garrison, 18);
            Contact first = AddContact(simulation, FakeTargetIdBase, new Vec2(280f, 450f));
            Contact second = AddContact(simulation, FakeTargetIdBase + 1, new Vec2(330f, 460f));

            RunTicks(simulation, 20, first, second);

            Assert.AreEqual(CommanderAction.FallBack, commander.Decisions[0].Action);
            Assert.IsTrue(commander.IsAbandoned(commander.Plan.Positions[0]));
            Assert.IsTrue(garrison.IsMoving);
            Assert.AreEqual(FallbackPoint, garrison.Waypoints[garrison.Waypoints.Count - 1].Position);
        }

        [Test]
        public void LocalSuperiority_AggressiveCommanderCounterattacks()
        {
            Simulation simulation = CreateDefense(0.8f, true, out EnemyCommander commander, out _, out Platoon reserve);
            Vec2 contactPosition = new Vec2(300f, 450f);
            Contact contact = AddContact(simulation, FakeTargetIdBase, contactPosition);

            RunTicks(simulation, 20, contact);

            Assert.AreEqual(CommanderAction.Counterattack, commander.Decisions[0].Action);
            Assert.IsTrue(reserve.IsMoving);
            Assert.AreEqual(contactPosition, reserve.Waypoints[reserve.Waypoints.Count - 1].Position);
            Assert.AreEqual(ContactBehavior.PushThrough, reserve.Waypoints[0].ContactBehavior);
        }

        [Test]
        public void SameSeed_SameDecisions()
        {
            List<string> first = RunDefenseScenario(7);
            List<string> second = RunDefenseScenario(7);

            Assert.Greater(first.Count, 0);
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i], second[i]);
            }
        }

        [Test]
        public void WithoutHeadquarters_CommanderDoesNothing()
        {
            Simulation simulation = CreateDefense(0.35f, true, out EnemyCommander commander, out _, out _, false);
            Contact contact = AddContact(simulation, FakeTargetIdBase, new Vec2(300f, 450f));

            RunTicks(simulation, 150, contact);

            Assert.AreEqual(0, commander.Decisions.Count);
        }

        [Test]
        public void HeadquartersDestroyed_CommanderStops()
        {
            Simulation simulation = CreateDefense(0.35f, true, out EnemyCommander commander, out _, out Platoon reserve);
            Assert.IsTrue(simulation.TryGetHeadquarters(Side.Red, out Platoon headquarters));
            SetCasualties(headquarters, headquarters.InitialStrength);
            Contact first = AddContact(simulation, FakeTargetIdBase, new Vec2(280f, 450f));
            Contact second = AddContact(simulation, FakeTargetIdBase + 1, new Vec2(330f, 460f));

            RunTicks(simulation, 150, first, second);

            Assert.AreEqual(0, commander.Decisions.Count);
            Assert.IsFalse(reserve.IsMoving);
        }

        /// <summary>
        /// 평지 600m × 1km. 레드 본부 (300, 950), 진지 소대 (300, 600), 예비대 (300, 800).
        /// 진지 위협 반경 200m, 후퇴 위치 (300, 850). 블루는 없다(필요한 테스트에서 추가).
        /// </summary>
        private static Simulation CreateDefense(
            float aggression,
            bool withReserve,
            out EnemyCommander commander,
            out Platoon garrison,
            out Platoon reserve,
            bool withHeadquarters = true)
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 0f };
            var simulation = new Simulation(new MapData(60, 100, CellSize), rules, 1, 0);
            garrison = simulation.AddPlatoon("진지 소대", Side.Red, PositionCenter, 180f);
            reserve = withReserve ? simulation.AddPlatoon("예비대", Side.Red, new Vec2(300f, 800f), 180f) : null;
            if (withHeadquarters)
            {
                simulation.AddPlatoon("적 본부", Side.Red, new Vec2(300f, 950f), 180f, 6, PlatoonRole.Headquarters);
            }

            var plan = new DefensePlan(reserve);
            plan.AddPosition(new DefensivePosition("진지", garrison, PositionCenter, ThreatRadius, 180f, WatchArc, FallbackPoint));
            commander = new EnemyCommander(Side.Red, new CommanderPersonality(aggression), new CommanderSkill { DecisionNoise = 0f }, plan, 1);
            simulation.AttachCommander(commander);
            return simulation;
        }

        private static Contact AddContact(Simulation simulation, int targetId, Vec2 position)
        {
            Contact contact = simulation.GetIntel(Side.Red).AddContact(targetId);
            contact.LastKnownPosition = position;
            contact.LastSeenSeconds = simulation.ElapsedSeconds;
            contact.Confidence = ContactConfidence.Confirmed;
            return contact;
        }

        /// <summary>틱을 진행하면서 주어진 접촉을 계속 "방금 본" 상태로 유지한다(누가 계속 보고 있다고 가정).</summary>
        private static void RunTicks(Simulation simulation, int ticks, params Contact[] keepFresh)
        {
            for (int i = 0; i < ticks; i++)
            {
                foreach (Contact contact in keepFresh)
                {
                    contact.LastSeenSeconds = simulation.ElapsedSeconds;
                    contact.Confidence = ContactConfidence.Confirmed;
                }

                simulation.Tick();
            }
        }

        private static void SetCasualties(Platoon platoon, int casualties)
        {
            platoon.Effective = platoon.InitialStrength - casualties;
            platoon.Wounded = casualties;
            platoon.Killed = 0;
        }

        /// <summary>방어 시나리오에서 블루 3개 소대가 1진지로 돌파 이동하는 판을 15분 돌리고 판단 기록을 문자열로 남긴다.</summary>
        private static List<string> RunDefenseScenario(int seed)
        {
            Simulation simulation = DefenseScenario.Create(seed);
            var objective = new Vec2(450f, 620f);
            foreach (Platoon platoon in simulation.Platoons)
            {
                if (platoon.Side == Side.Blue && !platoon.IsHeadquarters)
                {
                    simulation.OrderMove(platoon, new[] { objective }, MoveMode.Normal, false, ContactBehavior.PushThrough);
                }
            }

            for (int i = 0; i < 900 && !simulation.IsFinished; i++)
            {
                simulation.Tick();
            }

            var decisions = new List<string>();
            foreach (CommanderDecision decision in simulation.Commanders[0].Decisions)
            {
                decisions.Add($"{decision.ElapsedSeconds} {decision.Trigger} {decision.Action} {decision.Position.Name} {decision.Candidates}");
            }

            return decisions;
        }
    }
}
