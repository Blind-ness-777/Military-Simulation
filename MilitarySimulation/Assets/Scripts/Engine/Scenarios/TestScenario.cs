using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 엔진 검증용 임시 시나리오. 1km × 1km, 10m 격자.
    /// - 중앙(동서 방향)에 높이 25m 능선. 북쪽 사면 뒤에 적 1소대가 있어 역사면(P5)을 확인할 수 있다.
    /// - 남서쪽 숲: 아군이 엄폐하며 접근할 수 있는 곳.
    /// - 동쪽 언덕과 남북 도로. 적 2소대가 도로를 따라 남하한다.
    /// - 블루는 본부 분대가 있어 명령이 통신을 거친다. 능선 너머는 무전 음영, 동쪽 숲 일대는 재밍 구역.
    /// 시나리오 데이터 파일 형식을 정하기 전까지 코드로 만든다.
    /// </summary>
    public static class TestScenario
    {
        private const int MapCells = 100;
        private const float CellSize = 10f;
        private const int StartTimeOfDay = 7 * 3600;
        private const int HeadquartersStrength = 6;

        public static Simulation Create(int seed)
        {
            MapData map = CreateMap();
            var simulation = new Simulation(map, new RulesConfig(), seed, StartTimeOfDay);

            simulation.AddPlatoon("1소대", Side.Blue, new Vec2(250f, 80f), 0f);
            simulation.AddPlatoon("2소대", Side.Blue, new Vec2(420f, 80f), 0f);

            // 본부 분대: 현재 기준 장비(본부만 위성통신). 소대까지 위성통신을 주는 건 근미래 스테이지에서.
            Platoon headquarters = simulation.AddPlatoon(
                "본부", Side.Blue, new Vec2(335f, 40f), 0f, HeadquartersStrength, PlatoonRole.Headquarters);
            headquarters.CommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom;

            // 동쪽 숲 일대에 약한 재밍(적이 건 것). 들어가면 블루 통신이 저하된다.
            simulation.AddJammingZone(new JammingZone(new Vec2(680f, 420f), 150f, JammingStrength.Degrade, Side.Blue));

            Platoon redFirst = simulation.AddPlatoon("적 1소대", Side.Red, new Vec2(450f, 610f), 180f);
            simulation.OrderWatch(redFirst, 180f, 120f);

            Platoon redSecond = simulation.AddPlatoon("적 2소대", Side.Red, new Vec2(525f, 950f), 180f);
            simulation.OrderMove(
                redSecond,
                new[] { new Vec2(525f, 700f), new Vec2(760f, 420f), new Vec2(760f, 300f) },
                MoveMode.Normal,
                false);

            return simulation;
        }

        private static MapData CreateMap()
        {
            var map = new MapData(MapCells, MapCells, CellSize);

            for (int y = 0; y < MapCells; y++)
            {
                for (int x = 0; x < MapCells; x++)
                {
                    Vec2 center = map.GetCellCenter(x, y);
                    map.SetCellHeight(x, y, GetHeight(center));
                    map.SetCellTerrain(x, y, GetTerrain(center));
                }
            }

            return map;
        }

        private static float GetHeight(Vec2 position)
        {
            double ridge = 25.0 * Gaussian((position.Y - 500.0) / 90.0);
            double eastHillDistance = Vec2.Distance(position, new Vec2(780f, 300f)) / 120.0;
            double eastHill = 15.0 * Gaussian(eastHillDistance);
            return (float)(ridge + eastHill);
        }

        private static TerrainType GetTerrain(Vec2 position)
        {
            if (position.X >= 520f && position.X < 530f)
            {
                return TerrainType.Road;
            }

            bool southWestForest = position.X >= 180f && position.X < 340f && position.Y >= 240f && position.Y < 420f;
            bool eastWoods = position.X >= 620f && position.X < 720f && position.Y >= 360f && position.Y < 440f;
            return southWestForest || eastWoods ? TerrainType.Forest : TerrainType.Open;
        }

        private static double Gaussian(double x) => Math.Exp(-x * x);
    }
}
