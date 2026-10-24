using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 소대 → 본부 보고 지연 테스트.
    /// 본부 상황판(적 접촉, 아군 위치)과 로그는 보고가 도착한 것만 반영한다.
    /// 통신이 되는 소대는 상황판의 확인된 접촉도 쏠 수 있고, 두절된 소대는 자기가 본 것만 쏜다.
    /// </summary>
    public class ReportDelayTests
    {
        private const float CellSize = 10f;
        private const int MaxTicks = 300;

        [Test]
        public void CutPlatoon_FiresAtWhatItSees_ButSightingReachesHeadquartersOnlyAfterRestore()
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 5f };
            Simulation simulation = CreateNetwork(rules, out _, out Platoon platoon, out Platoon enemy);
            var zone = new JammingZone(platoon.Position, 100f, JammingStrength.Block, Side.Blue);
            simulation.AddJammingZone(zone);

            for (int i = 0; i < MaxTicks && !platoon.IsFiring; i++)
            {
                simulation.Tick();
            }

            Assert.AreEqual(CommsState.Cut, platoon.Comms);
            Assert.IsTrue(platoon.IsFiring, "두절돼도 자기가 본 적은 쏜다.");
            Assert.IsFalse(simulation.GetIntel(Side.Blue).TryGetContact(enemy.Id, out _), "보고가 도착하기 전에는 상황판에 없다.");

            simulation.RemoveJammingZone(zone);
            RunTicks(simulation, 2);

            Assert.IsTrue(simulation.GetIntel(Side.Blue).TryGetContact(enemy.Id, out Contact contact));
            Assert.AreEqual(ContactConfidence.Confirmed, contact.Confidence);
        }

        [Test]
        public void CutPlatoon_FriendlyReportStaysAtLastReportedPosition()
        {
            Simulation simulation = CreateNetwork(new RulesConfig(), out _, out Platoon platoon, out _);
            simulation.OrderMove(platoon, new[] { new Vec2(25f, 500f) }, MoveMode.Normal, false);
            RunTicks(simulation, 5);
            Assert.IsTrue(platoon.IsMoving);
            Assert.IsTrue(simulation.IsReportingLive(platoon));

            Vec2 positionAtCut = platoon.Position;
            simulation.AddJammingZone(new JammingZone(platoon.Position, 2000f, JammingStrength.Block, Side.Blue));
            simulation.Tick();

            Assert.IsFalse(simulation.IsReportingLive(platoon));
            Assert.IsTrue(simulation.TryGetLastReport(platoon, out FriendlyReport report));
            Assert.AreEqual(positionAtCut, report.Snapshot.Position);
            Assert.AreEqual(1, report.Snapshot.Waypoints.Count, "보고에는 그때 수행 중이던 경로가 남는다.");

            RunTicks(simulation, 20);
            Assert.IsTrue(simulation.TryGetLastReport(platoon, out report));
            Assert.AreEqual(positionAtCut, report.Snapshot.Position);
            Assert.AreNotEqual(positionAtCut, platoon.Position);
        }

        [Test]
        public void SharedTargeting_ConnectedPlatoonFiresAtSideContact()
        {
            Simulation simulation = CreateSharedTargetingCase(out Platoon platoon);

            simulation.Tick();

            Assert.IsTrue(platoon.IsFiring);
        }

        [Test]
        public void SharedTargeting_CutPlatoonCannotUseSideContact()
        {
            Simulation simulation = CreateSharedTargetingCase(out Platoon platoon);
            simulation.AddJammingZone(new JammingZone(platoon.Position, 100f, JammingStrength.Block, Side.Blue));

            simulation.Tick();

            Assert.AreEqual(CommsState.Cut, platoon.Comms);
            Assert.IsFalse(platoon.IsFiring);
        }

        [Test]
        public void DegradedComms_EventArrivesAfterReportDelayWithObservedTime()
        {
            Simulation simulation = CreateNetwork(new RulesConfig(), out _, out Platoon platoon, out _);
            simulation.AddJammingZone(new JammingZone(platoon.Position, 100f, JammingStrength.Degrade, Side.Blue));
            simulation.OrderMove(platoon, new[] { new Vec2(25f, 720f) }, MoveMode.Normal, false);

            for (int i = 0; i < MaxTicks && (platoon.IsMoving || simulation.PendingOrders.Count > 0); i++)
            {
                simulation.Tick();
            }

            Assert.AreEqual(CommsState.Degraded, platoon.Comms);
            int arrivedAt = simulation.ElapsedSeconds;
            Assert.IsFalse(HasEvent(simulation, "도착"));

            RunTicks(simulation, simulation.Rules.DegradedReportDelaySeconds - 1);
            Assert.IsFalse(HasEvent(simulation, "도착"));

            simulation.Tick();
            SimEvent arrival = FindEvent(simulation, "도착");
            Assert.AreEqual(arrivedAt + simulation.Rules.DegradedReportDelaySeconds, arrival.ElapsedSeconds);
            StringAssert.Contains($"관측 {simulation.FormatClock(arrivedAt)}", arrival.Text);
        }

        [Test]
        public void DestroyedWhileCut_HeadquartersIsNotTold_AndHeldOrderRemains()
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 5f, BaseCasualtyPerShooterPerMinute = 50f, BreakMorale = 0f };
            Simulation simulation = CreateNetwork(rules, out _, out Platoon platoon, out Platoon enemy);
            enemy.FirePolicy = FirePolicy.Free;
            platoon.FirePolicy = FirePolicy.HoldFire;
            simulation.AddJammingZone(new JammingZone(platoon.Position, 100f, JammingStrength.Block, Side.Blue));
            simulation.Tick();
            simulation.OrderMove(platoon, new[] { new Vec2(25f, 500f) }, MoveMode.Normal, false);

            for (int i = 0; i < MaxTicks && !platoon.IsDestroyed; i++)
            {
                simulation.Tick();
            }

            Assert.IsTrue(platoon.IsDestroyed);
            RunTicks(simulation, 10);

            Assert.IsFalse(HasEvent(simulation, "피해"));
            Assert.IsFalse(HasEvent(simulation, "전투 불능"));
            Assert.AreEqual(1, simulation.PendingOrders.Count);
            Assert.IsTrue(simulation.TryGetLastReport(platoon, out FriendlyReport report));
            Assert.IsFalse(report.Snapshot.IsDestroyed);
        }

        /// <summary>
        /// 탐지 확률 0이라 아무도 직접 못 본다. 본부 상황판에만 적이 확인 접촉으로 올라 있다(다른 소대 보고라고 가정).
        /// </summary>
        private static Simulation CreateSharedTargetingCase(out Platoon platoon)
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 0f };
            Simulation simulation = CreateNetwork(rules, out _, out platoon, out Platoon enemy);

            Contact contact = simulation.GetIntel(Side.Blue).AddContact(enemy.Id);
            contact.LastKnownPosition = enemy.Position;
            contact.LastSeenSeconds = 0;
            contact.Confidence = ContactConfidence.Confirmed;
            return simulation;
        }

        /// <summary>
        /// 평지 1.2km. 본부 (25, 50), 소대 (25, 700), 적 (25, 860) 사격 금지·정지.
        /// 적은 본부의 시야(800m) 밖이라, 본부 상황판에는 소대 보고로만 올라간다.
        /// </summary>
        private static Simulation CreateNetwork(RulesConfig rules, out Platoon headquarters, out Platoon platoon, out Platoon enemy)
        {
            var simulation = new Simulation(new MapData(5, 120, CellSize), rules, 1, 0);
            platoon = simulation.AddPlatoon("소대", Side.Blue, new Vec2(25f, 700f), 0f);
            headquarters = simulation.AddPlatoon("본부", Side.Blue, new Vec2(25f, 50f), 0f, 6, PlatoonRole.Headquarters);
            enemy = simulation.AddPlatoon("적", Side.Red, new Vec2(25f, 860f), 180f);
            enemy.FirePolicy = FirePolicy.HoldFire;
            return simulation;
        }

        private static bool HasEvent(Simulation simulation, string text)
        {
            foreach (SimEvent simEvent in simulation.Events)
            {
                if (IsPlatoonReport(simEvent, text))
                {
                    return true;
                }
            }

            return false;
        }

        private static SimEvent FindEvent(Simulation simulation, string text)
        {
            foreach (SimEvent simEvent in simulation.Events)
            {
                if (IsPlatoonReport(simEvent, text))
                {
                    return simEvent;
                }
            }

            Assert.Fail($"'{text}' 로그 없음");
            return default;
        }

        /// <summary>소대가 보낸 보고 로그만 찾는다("명령 발신 … 도착 예정" 같은 본부 쪽 로그 제외).</summary>
        private static bool IsPlatoonReport(SimEvent simEvent, string text)
        {
            return simEvent.Side == Side.Blue && simEvent.Text.StartsWith("소대 ") && simEvent.Text.Contains(text);
        }

        private static void RunTicks(Simulation simulation, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                simulation.Tick();
            }
        }
    }
}
