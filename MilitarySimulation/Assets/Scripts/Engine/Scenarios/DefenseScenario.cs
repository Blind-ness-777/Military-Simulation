namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 적 지휘관 AI 검증용 방어 시나리오. 지도는 TestScenario와 같다(1km, 중앙 능선, 남서쪽 숲, 동쪽 언덕).
    /// - 블루: 1·2·3소대 + 본부. 임무는 TestScenario와 같은 "능선 너머 적 진지(1진지) 확보, 30분".
    /// - 레드: 본부 + 1진지(적 1소대, 역사면) + 2진지(적 2소대, 능선 동쪽) + 예비대(적 3소대, 능선 뒤).
    ///   레드도 본부가 있어 명령·보고가 통신을 거친다. 적 지휘관은 신중 65 / 공격 35, 실력 보통.
    /// 진지·후퇴 위치는 임시 값이다. 데모 맵을 만들 때 시야 조건과 함께 다시 정한다.
    /// </summary>
    public static class DefenseScenario
    {
        private const int StartTimeOfDay = 7 * 3600;
        private const int HeadquartersStrength = 6;
        private const float CommanderAggression = 0.35f;
        private const float WatchArc = 120f;
        private const float ThreatRadius = 200f;

        /// <summary>지휘관 난수 시드를 판 시드에서 떨어뜨리는 값(시뮬레이션 난수와 같은 흐름이 되지 않게).</summary>
        private const int CommanderSeedOffset = 1000003;

        public static Simulation Create(int seed)
        {
            var simulation = new Simulation(TestScenario.CreateMap(), new RulesConfig(), seed, StartTimeOfDay);

            simulation.AddPlatoon("1소대", Side.Blue, new Vec2(250f, 80f), 0f);
            simulation.AddPlatoon("2소대", Side.Blue, new Vec2(420f, 80f), 0f);
            simulation.AddPlatoon("3소대", Side.Blue, new Vec2(590f, 80f), 0f);
            Platoon blueHeadquarters = simulation.AddPlatoon(
                "본부", Side.Blue, new Vec2(335f, 40f), 0f, HeadquartersStrength, PlatoonRole.Headquarters);
            blueHeadquarters.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;

            // TestScenario와 같은 동쪽 숲 재밍(블루 통신 저하).
            simulation.AddJammingZone(new JammingZone(new Vec2(680f, 420f), 150f, JammingStrength.Degrade, Side.Blue));

            Platoon redFirst = simulation.AddPlatoon("적 1소대", Side.Red, new Vec2(450f, 610f), 180f);
            Platoon redSecond = simulation.AddPlatoon("적 2소대", Side.Red, new Vec2(700f, 600f), 200f);
            Platoon redReserve = simulation.AddPlatoon("적 3소대", Side.Red, new Vec2(575f, 780f), 180f);
            Platoon redHeadquarters = simulation.AddPlatoon(
                "적 본부", Side.Red, new Vec2(575f, 880f), 180f, HeadquartersStrength, PlatoonRole.Headquarters);
            redHeadquarters.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;

            var plan = new DefensePlan(redReserve);
            plan.AddPosition(new DefensivePosition(
                "1진지", redFirst, new Vec2(450f, 610f), ThreatRadius, 180f, WatchArc, new Vec2(450f, 800f)));
            plan.AddPosition(new DefensivePosition(
                "2진지", redSecond, new Vec2(700f, 600f), ThreatRadius, 200f, WatchArc, new Vec2(700f, 800f)));

            // 시작 배치(경계 방향). 레드 본부가 있으므로 이 명령도 통신을 거쳐 판 시작 직후 도착한다.
            simulation.OrderWatch(redFirst, 180f, WatchArc);
            simulation.OrderWatch(redSecond, 200f, WatchArc);
            simulation.OrderWatch(redReserve, 180f, WatchArc);

            simulation.AttachCommander(new EnemyCommander(
                Side.Red,
                new CommanderPersonality(CommanderAggression),
                new CommanderSkill(),
                plan,
                seed + CommanderSeedOffset));

            var mission = new Mission(Side.Blue, "능선 너머 적 진지 확보")
            {
                TimeLimitSeconds = 30 * 60,
                TimeoutResult = MissionResult.Defeat
            };
            mission.AddStep(new Objective(ObjectiveKind.SeizeZone, "적 진지", new Vec2(450f, 620f), 100f));
            simulation.SetMission(mission);

            return simulation;
        }
    }
}
