using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 임무 판정 테스트: 지역 확보·방어, 폭파 후 철수, 제한 시간, 전멸, 판 종료.
    /// 탐지 확률을 0으로 둬서 교전 없이 위치만으로 판정을 확인한다.
    /// </summary>
    public class MissionTests
    {
        private const float CellSize = 10f;

        [Test]
        public void Seize_CompletesAfterHoldSeconds()
        {
            Simulation simulation = CreateSimulation();
            simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Mission mission = CreateMission(simulation, out Objective zone);
            mission.AddStep(zone);

            RunTicks(simulation, zone.HoldSeconds - 1);
            Assert.AreEqual(MissionResult.InProgress, mission.Result);
            Assert.AreEqual(zone.HoldSeconds - 1, zone.ProgressSeconds);

            simulation.Tick();
            Assert.AreEqual(MissionResult.Victory, mission.Result);
            Assert.AreEqual(ObjectiveState.Complete, zone.State);
        }

        [Test]
        public void Seize_EnemyInsideZone_ResetsProgress()
        {
            Simulation simulation = CreateSimulation();
            simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Platoon enemy = simulation.AddPlatoon("적", Side.Red, new Vec2(260f, 280f), 180f);
            Mission mission = CreateMission(simulation, out Objective zone);
            mission.AddStep(zone);

            RunTicks(simulation, zone.HoldSeconds + 10);
            Assert.AreEqual(0, zone.ProgressSeconds);
            Assert.AreEqual(MissionResult.InProgress, mission.Result);

            Destroy(enemy);
            RunTicks(simulation, zone.HoldSeconds);
            Assert.AreEqual(MissionResult.Victory, mission.Result);
        }

        [Test]
        public void Seize_BrokenEnemyInsideZone_StillBlocks()
        {
            Simulation simulation = CreateSimulation();
            simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Platoon enemy = simulation.AddPlatoon("적", Side.Red, new Vec2(260f, 280f), 180f);
            Mission mission = CreateMission(simulation, out Objective zone);
            mission.AddStep(zone);

            // 사기 붕괴 상태로 구역 안에 남아 있다(사격을 받지 않아 더 물러나지 않음, 사기 0이라 재집결도 안 함).
            enemy.IsBroken = true;
            enemy.Morale = 0f;

            RunTicks(simulation, zone.HoldSeconds + 10);
            Assert.AreEqual(0, zone.ProgressSeconds);
            Assert.AreEqual(MissionResult.InProgress, mission.Result);

            Destroy(enemy);
            RunTicks(simulation, zone.HoldSeconds);
            Assert.AreEqual(MissionResult.Victory, mission.Result);
        }

        [Test]
        public void Hold_FailsWhenEnemyHoldsZone()
        {
            Simulation simulation = CreateSimulation();
            simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 50f), 0f);
            simulation.AddPlatoon("적", Side.Red, new Vec2(250f, 250f), 180f);
            Mission mission = CreateMission(simulation, out Objective zone, ObjectiveKind.HoldZone);
            mission.AddConstraint(zone);

            RunTicks(simulation, zone.HoldSeconds);

            Assert.AreEqual(MissionResult.Defeat, mission.Result);
            Assert.AreEqual(ObjectiveState.Failed, zone.State);
        }

        [Test]
        public void Timeout_UsesScenarioResult()
        {
            Simulation simulation = CreateSimulation();
            simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Mission mission = CreateMission(simulation, out Objective zone, ObjectiveKind.HoldZone);
            mission.AddConstraint(zone);
            mission.TimeLimitSeconds = 100;
            mission.TimeoutResult = MissionResult.Victory;

            RunTicks(simulation, 99);
            Assert.AreEqual(MissionResult.InProgress, mission.Result);

            simulation.Tick();
            Assert.AreEqual(MissionResult.Victory, mission.Result);
        }

        [Test]
        public void DestroyThenExfiltrate_RunsInOrder()
        {
            Simulation simulation = CreateSimulation();
            Platoon platoon = simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Mission mission = CreateMission(simulation, out Objective target, ObjectiveKind.DestroyTarget);
            target.WorkSeconds = 30;
            mission.AddStep(target);
            Objective exfil = mission.AddStep(new Objective(ObjectiveKind.Exfiltrate, "철수 지점", new Vec2(250f, 50f), 40f));

            RunTicks(simulation, 30);
            Assert.AreEqual(ObjectiveState.Complete, target.State);
            Assert.AreEqual(ObjectiveState.Active, exfil.State);
            Assert.AreEqual(MissionResult.InProgress, mission.Result);

            simulation.OrderMove(platoon, new[] { new Vec2(250f, 50f) }, MoveMode.Fast, false);
            for (int i = 0; i < 600 && !simulation.IsFinished; i++)
            {
                simulation.Tick();
            }

            Assert.AreEqual(MissionResult.Victory, mission.Result);
        }

        [Test]
        public void DestroyTarget_WorkPausesWhenPlatoonLeaves_AndResumes()
        {
            Simulation simulation = CreateSimulation();
            Platoon platoon = simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Mission mission = CreateMission(simulation, out Objective target, ObjectiveKind.DestroyTarget);
            target.WorkSeconds = 100;
            mission.AddStep(target);

            RunTicks(simulation, 40);
            platoon.Position = new Vec2(250f, 50f);
            RunTicks(simulation, 20);
            Assert.AreEqual(40, target.ProgressSeconds);

            platoon.Position = new Vec2(250f, 250f);
            RunTicks(simulation, 60);
            Assert.AreEqual(MissionResult.Victory, mission.Result);
        }

        [Test]
        public void AllPlatoonsDestroyed_IsDefeat()
        {
            Simulation simulation = CreateSimulation();
            Platoon platoon = simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 50f), 0f);
            Mission mission = CreateMission(simulation, out Objective zone);
            mission.AddStep(zone);

            Destroy(platoon);
            simulation.Tick();

            Assert.AreEqual(MissionResult.Defeat, mission.Result);
        }

        [Test]
        public void FinishedSimulation_StopsTicking()
        {
            Simulation simulation = CreateSimulation();
            simulation.AddPlatoon("소대", Side.Blue, new Vec2(250f, 250f), 0f);
            Mission mission = CreateMission(simulation, out Objective zone);
            zone.HoldSeconds = 5;
            mission.AddStep(zone);

            RunTicks(simulation, 5);
            Assert.IsTrue(simulation.IsFinished);
            int finishedAt = simulation.ElapsedSeconds;

            RunTicks(simulation, 10);
            Assert.AreEqual(finishedAt, simulation.ElapsedSeconds);
        }

        /// <summary>평지 500m. 탐지 확률 0이라 교전이 일어나지 않는다. 본부 없음(명령 즉시 적용).</summary>
        private static Simulation CreateSimulation()
        {
            var rules = new RulesConfig { BaseDetectionPerMinute = 0f };
            return new Simulation(new MapData(50, 50, CellSize), rules, 1, 0);
        }

        /// <summary>블루 임무와 (250, 250) 반경 60m 구역 하나. 구역은 단계·조건 어느 쪽에 넣을지 테스트가 정한다.</summary>
        private static Mission CreateMission(
            Simulation simulation, out Objective zone, ObjectiveKind kind = ObjectiveKind.SeizeZone)
        {
            var mission = new Mission(Side.Blue, "시험 임무");
            zone = new Objective(kind, "구역", new Vec2(250f, 250f), 60f);
            simulation.SetMission(mission);
            return mission;
        }

        private static void Destroy(Platoon platoon)
        {
            platoon.Killed += platoon.Effective;
            platoon.Effective = 0;
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
