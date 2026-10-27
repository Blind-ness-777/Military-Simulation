using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 계획 명령 테스트: 웨이포인트 대기 조건(신호·H시·○소대 접촉/도착), 해제 시 사격 방침, 통신과의 관계.
    /// 평지라 무전은 정상(명령 지연 2초). 두절·저하는 재밍 구역으로 만든다.
    /// </summary>
    public class PlanOrderTests
    {
        private const float CellSize = 10f;
        private const int ArrivalTicks = 120;

        private static readonly Vec2 HeadquartersPosition = new Vec2(300f, 50f);

        [Test]
        public void SignalWait_HoldsUntilSignalArrives()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 200f), new WaitCondition(1, false, null, null));

            RunTicks(simulation, ArrivalTicks);
            Assert.IsTrue(first.IsWaiting);
            Assert.IsFalse(first.IsMoving);
            Assert.AreEqual(1, first.Waypoints.Count);
            Assert.IsTrue(Vec2.Distance(first.Position, new Vec2(300f, 200f)) < 0.5f);

            RunTicks(simulation, 60);
            Assert.IsTrue(first.IsWaiting);

            simulation.SendSignal(Side.Blue, 1);
            int delay = simulation.Rules.NormalCommandDelaySeconds;
            RunTicks(simulation, delay - 1);
            Assert.IsTrue(first.IsWaiting);

            simulation.Tick();
            Assert.IsFalse(first.IsWaiting);
            Assert.IsTrue(first.IsMoving);
            Assert.IsTrue(HasEvent(simulation, "대기 해제(신호 1 수신)"));
        }

        [Test]
        public void OtherSignalNumber_DoesNotRelease()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 200f), new WaitCondition(2, false, null, null));
            RunTicks(simulation, ArrivalTicks);

            simulation.SendSignal(Side.Blue, 1);
            RunTicks(simulation, 10);

            Assert.IsTrue(first.IsWaiting);
        }

        [Test]
        public void SignalSentDuringCommsCut_ArrivesAfterRecovery()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            Vec2 waitPoint = new Vec2(300f, 200f);
            OrderWaitThenGo(simulation, first, waitPoint, new WaitCondition(1, false, null, null));
            RunTicks(simulation, ArrivalTicks);

            var jamming = new JammingZone(waitPoint, 40f, JammingStrength.Block, Side.Blue);
            simulation.AddJammingZone(jamming);
            simulation.Tick();
            Assert.AreEqual(CommsState.Cut, first.Comms);

            simulation.SendSignal(Side.Blue, 1);
            Assert.IsTrue(HasEvent(simulation, "회복되면 전달: 1소대"));
            RunTicks(simulation, 60);
            Assert.IsTrue(first.IsWaiting);

            simulation.RemoveJammingZone(jamming);
            RunTicks(simulation, 5);
            Assert.IsFalse(first.IsWaiting);
            Assert.IsTrue(first.IsMoving);
        }

        [Test]
        public void SignalReceivedBeforeArrival_ReleasesOnArrival()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 200f), new WaitCondition(1, false, null, null));
            simulation.SendSignal(Side.Blue, 1);

            RunTicks(simulation, ArrivalTicks);

            Assert.IsFalse(first.IsWaiting);
            Assert.IsTrue(first.Position.Y > 201f);
        }

        [Test]
        public void SignalFromBeforeNewPlan_IsForgotten()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            simulation.SendSignal(Side.Blue, 1);
            RunTicks(simulation, 5);

            OrderWaitThenGo(simulation, first, new Vec2(300f, 200f), new WaitCondition(1, false, null, null));
            RunTicks(simulation, ArrivalTicks + 30);

            Assert.IsTrue(first.IsWaiting);
        }

        [Test]
        public void HHour_CutPlatoonKeepsOldTime_AndStillGoesAfterSelfDirected()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out Platoon second);
            simulation.SetHHour(Side.Blue, 300);
            var atHHour = new WaitCondition(0, true, null, null);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 200f), atHHour);
            Vec2 secondWait = new Vec2(150f, 200f);
            OrderWaitThenGo(simulation, second, secondWait, atHHour);
            RunTicks(simulation, 150);
            Assert.IsTrue(first.IsWaiting);
            Assert.IsTrue(second.IsWaiting);

            // 2소대만 두절. H시를 07:06:40(400초)으로 미뤄도 2소대는 변경을 받지 못한다.
            simulation.AddJammingZone(new JammingZone(secondWait, 40f, JammingStrength.Block, Side.Blue));
            simulation.Tick();
            Assert.AreEqual(CommsState.Cut, second.Comms);
            simulation.SetHHour(Side.Blue, 400);

            RunUntil(simulation, 299);
            Assert.IsTrue(first.IsWaiting);
            Assert.IsTrue(second.IsWaiting);
            Assert.IsTrue(second.IsSelfDirected, "두절 120초가 지나 자체 판단(고수) 중이어야 한다");
            Assert.AreEqual(400, first.KnownHHourSeconds);
            Assert.AreEqual(300, second.KnownHHourSeconds);

            RunUntil(simulation, 300);
            Assert.IsFalse(second.IsWaiting, "자체 판단 중이어도 알고 있는 H시에는 출발한다");
            Assert.IsTrue(first.IsWaiting);

            RunUntil(simulation, 400);
            Assert.IsFalse(first.IsWaiting);
        }

        [Test]
        public void ArrivalCondition_ReleasesAfterHeadquartersHears()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out Platoon second);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 120f), new WaitCondition(0, false, null, second));
            simulation.OrderMove(second, new[] { new Vec2(150f, 200f) }, MoveMode.Normal, false);

            int arrivedAt = RunUntilStopped(simulation, second);
            Assert.IsTrue(first.IsWaiting);

            int delay = simulation.Rules.NormalCommandDelaySeconds;
            RunUntil(simulation, arrivedAt + delay - 1);
            Assert.IsTrue(first.IsWaiting);
            RunUntil(simulation, arrivedAt + delay);
            Assert.IsFalse(first.IsWaiting);
            Assert.IsTrue(HasEvent(simulation, "대기 해제(2소대 도착 통보)"));
        }

        [Test]
        public void ArrivalCondition_DelayedReport_DelaysRelease()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out Platoon second);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 120f), new WaitCondition(0, false, null, second));
            Vec2 destination = new Vec2(150f, 200f);
            simulation.OrderMove(second, new[] { destination }, MoveMode.Normal, false);
            RunTicks(simulation, 5);

            // 2소대 목적지 일대만 저하 재밍(1소대 대기 지점은 밖): 도착 보고가 15초 늦게 본부에 간다.
            simulation.AddJammingZone(new JammingZone(destination, 60f, JammingStrength.Degrade, Side.Blue));
            int arrivedAt = RunUntilStopped(simulation, second);
            Assert.AreEqual(CommsState.Degraded, second.Comms);

            RunUntil(simulation, arrivedAt + simulation.Rules.DegradedReportDelaySeconds - 1);
            Assert.IsTrue(first.IsWaiting, "도착 보고가 본부에 닿기 전에는 풀리지 않는다");
            RunTicks(simulation, simulation.Rules.NormalCommandDelaySeconds + 2);
            Assert.IsFalse(first.IsWaiting);
        }

        [Test]
        public void ContactCondition_OtherPlatoon_ReleasesOnContactReport()
        {
            Simulation simulation = CreateNetwork(true, out Platoon first, out Platoon second);
            Platoon enemy = simulation.AddPlatoon("적", Side.Red, new Vec2(300f, 1350f), 180f);
            enemy.FirePolicy = FirePolicy.HoldFire;
            OrderWaitThenGo(simulation, first, new Vec2(300f, 120f), new WaitCondition(0, false, second, null));
            simulation.OrderMove(second, new[] { new Vec2(300f, 900f) }, MoveMode.Normal, false);
            RunTicks(simulation, 30);
            Assert.IsTrue(first.IsWaiting);

            for (int i = 0; i < 1200 && first.IsWaiting; i++)
            {
                simulation.Tick();
            }

            Assert.IsFalse(first.IsWaiting);
            Assert.IsTrue(HasEvent(simulation, "대기 해제(2소대 접촉 통보)"));
            Assert.IsTrue(Vec2.Distance(first.Position, enemy.Position) > first.UnitType.MaxVisualRange, "1소대는 적을 직접 보지 않았다");
        }

        [Test]
        public void Ambush_SelfContact_HoldsFireUntilContact_ThenFires()
        {
            Simulation simulation = CreateNetwork(true, out Platoon first, out _);
            Platoon enemy = simulation.AddPlatoon("적", Side.Red, new Vec2(300f, 1100f), 180f);
            enemy.FirePolicy = FirePolicy.HoldFire;

            simulation.OrderFirePolicy(first, FirePolicy.HoldFire);
            var ambush = new Waypoint(
                new Vec2(300f, 110f), MoveMode.Normal, ContactBehavior.TakeCover,
                new WaitCondition(0, false, first, null), FirePolicy.Free);
            simulation.OrderMove(first, new[] { ambush }, false);
            RunTicks(simulation, 30);
            Assert.IsTrue(first.IsWaiting);
            Assert.AreEqual(FirePolicy.HoldFire, first.FirePolicy);

            simulation.OrderMove(enemy, new[] { new Vec2(300f, 300f) }, MoveMode.Normal, false);
            for (int i = 0; i < 1200 && first.IsWaiting; i++)
            {
                Assert.IsFalse(first.IsFiring);
                simulation.Tick();
            }

            Assert.IsFalse(first.IsWaiting);
            Assert.AreEqual(FirePolicy.Free, first.FirePolicy);
            Assert.IsTrue(HasEvent(simulation, "대기 해제(적 접촉) — 대기 종료, 자유 사격"));
        }

        [Test]
        public void NewOrder_CancelsWaitAndRemainingPlan()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            OrderWaitThenGo(simulation, first, new Vec2(300f, 200f), new WaitCondition(1, false, null, null));
            RunTicks(simulation, ArrivalTicks);
            Assert.IsTrue(first.IsWaiting);

            simulation.OrderHold(first);
            RunTicks(simulation, 5);
            Assert.IsFalse(first.IsWaiting);
            Assert.AreEqual(0, first.Waypoints.Count);

            simulation.SendSignal(Side.Blue, 1);
            RunTicks(simulation, 5);
            Assert.IsFalse(first.IsMoving);
        }

        [Test]
        public void SignalOrder_DoesNotCancelPendingMove()
        {
            Simulation simulation = CreateNetwork(false, out Platoon first, out _);
            simulation.OrderMove(first, new[] { new Vec2(300f, 300f) }, MoveMode.Normal, false);
            simulation.SendSignal(Side.Blue, 1);

            RunTicks(simulation, 5);

            Assert.IsTrue(first.IsMoving);
        }

        [Test]
        public void RedSide_UsesSamePlanOrders()
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 0f };
            var simulation = new Simulation(new MapData(60, 150, CellSize), rules, 1, 0);
            simulation.AddPlatoon("적 본부", Side.Red, new Vec2(300f, 1450f), 180f, 6, PlatoonRole.Headquarters);
            Platoon red = simulation.AddPlatoon("적 소대", Side.Red, new Vec2(300f, 1400f), 180f);
            OrderWaitThenGo(simulation, red, new Vec2(300f, 1300f), new WaitCondition(3, false, null, null));
            RunTicks(simulation, ArrivalTicks);
            Assert.IsTrue(red.IsWaiting);

            simulation.SendSignal(Side.Red, 3);
            RunTicks(simulation, simulation.Rules.NormalCommandDelaySeconds);

            Assert.IsFalse(red.IsWaiting);
            Assert.IsTrue(red.IsMoving);
        }

        /// <summary>
        /// 평지 600m × 1.5km. 블루 본부 (300, 50), 1소대 (300, 100), 2소대 (200, 100).
        /// detect=false면 탐지 확률 0(교전 없음).
        /// </summary>
        private static Simulation CreateNetwork(bool detect, out Platoon first, out Platoon second)
        {
            var rules = detect ? new RulesConfig() : new RulesConfig { BaseDetectionPerMinute = 0f };
            var simulation = new Simulation(new MapData(60, 150, CellSize), rules, 1, 0);
            simulation.AddPlatoon("본부", Side.Blue, HeadquartersPosition, 0f, 6, PlatoonRole.Headquarters);
            first = simulation.AddPlatoon("1소대", Side.Blue, new Vec2(300f, 100f), 0f);
            second = simulation.AddPlatoon("2소대", Side.Blue, new Vec2(200f, 100f), 0f);
            return simulation;
        }

        /// <summary>대기 지점(조건 있음)까지 간 뒤 100m 더 가는 경로.</summary>
        private static void OrderWaitThenGo(Simulation simulation, Platoon platoon, Vec2 waitPoint, WaitCondition wait)
        {
            var waypoints = new[]
            {
                new Waypoint(waitPoint, MoveMode.Normal, ContactBehavior.TakeCover, wait, null),
                new Waypoint(waitPoint + new Vec2(0f, 100f), MoveMode.Normal, ContactBehavior.TakeCover)
            };
            simulation.OrderMove(platoon, waypoints, false);
        }

        private static void RunTicks(Simulation simulation, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                simulation.Tick();
            }
        }

        private static void RunUntil(Simulation simulation, int elapsedSeconds)
        {
            while (simulation.ElapsedSeconds < elapsedSeconds)
            {
                simulation.Tick();
            }
        }

        /// <summary>소대가 움직이기 시작한 뒤 멈출 때까지 진행하고, 멈춘 시각을 돌려준다.</summary>
        private static int RunUntilStopped(Simulation simulation, Platoon platoon)
        {
            bool started = platoon.IsMoving;
            for (int i = 0; i < 2000; i++)
            {
                simulation.Tick();
                if (platoon.IsMoving)
                {
                    started = true;
                }
                else if (started)
                {
                    return simulation.ElapsedSeconds;
                }
            }

            Assert.Fail($"{platoon.Name}이(가) 멈추지 않았다");
            return -1;
        }

        private static bool HasEvent(Simulation simulation, string text)
        {
            foreach (SimEvent simEvent in simulation.Events)
            {
                if (simEvent.Text.Contains(text))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
