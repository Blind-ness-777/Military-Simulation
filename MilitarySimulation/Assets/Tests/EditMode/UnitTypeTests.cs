using NUnit.Framework;

namespace MilitarySimulation.Engine.Tests
{
    /// <summary>
    /// 병과 데이터 테스트. 관측·화력·속도 수치가 RulesConfig가 아니라 각 부대의 병과에서 읽히는지 확인한다.
    /// </summary>
    public class UnitTypeTests
    {
        private const float CellSize = 10f;

        [Test]
        public void AddPlatoon_WithoutUnitType_UsesDefaultForRole()
        {
            var simulation = new Simulation(new MapData(5, 60, CellSize), new RulesConfig(), 1, 0);
            Platoon line = simulation.AddPlatoon("소대", Side.Blue, new Vec2(25f, 200f), 0f);
            Platoon headquarters = simulation.AddPlatoon("본부", Side.Blue, new Vec2(25f, 50f), 0f, role: PlatoonRole.Headquarters);

            Assert.AreEqual("보병 소대", line.UnitType.Name);
            Assert.AreEqual(30, line.InitialStrength);
            Assert.AreEqual(CommsEquipment.LineOfSightRadio, line.CommsEquipment);

            Assert.AreEqual("본부", headquarters.UnitType.Name);
            Assert.AreEqual(6, headquarters.InitialStrength);
            Assert.AreEqual(CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom, headquarters.CommsEquipment);
        }

        [Test]
        public void VisualRange_IsReadFromObserverUnitType()
        {
            var simulation = new Simulation(new MapData(5, 60, CellSize), new RulesConfig(), 1, 0);
            UnitType shortSighted = UnitTypes.CreateInfantryPlatoon();
            shortSighted.MaxVisualRange = 400f;

            Platoon normal = simulation.AddPlatoon("일반", Side.Blue, new Vec2(15f, 50f), 0f);
            Platoon limited = simulation.AddPlatoon("근시", Side.Blue, new Vec2(35f, 50f), 0f, unitType: shortSighted);
            Platoon target = simulation.AddPlatoon("표적", Side.Red, new Vec2(25f, 550f), 180f);

            Assert.IsTrue(simulation.CanObserve(normal, target));
            Assert.IsFalse(simulation.CanObserve(limited, target));
        }

        [Test]
        public void FireRange_IsReadFromShooterWeapon()
        {
            var simulation = new Simulation(new MapData(5, 60, CellSize), new RulesConfig(), 1, 0);
            UnitType longRange = UnitTypes.CreateInfantryPlatoon();
            longRange.Weapon.FireRange = 600f;

            Platoon normal = simulation.AddPlatoon("일반", Side.Blue, new Vec2(15f, 50f), 0f);
            Platoon extended = simulation.AddPlatoon("장거리", Side.Blue, new Vec2(35f, 50f), 0f, unitType: longRange);
            Platoon target = simulation.AddPlatoon("표적", Side.Red, new Vec2(25f, 550f), 180f);

            Assert.AreEqual(0.0, simulation.GetCasualtyChancePerShooterPerTick(normal, target));
            Assert.Greater(simulation.GetCasualtyChancePerShooterPerTick(extended, target), 0.0);
        }

        [Test]
        public void MoveSpeed_IsReadFromUnitType()
        {
            var simulation = new Simulation(new MapData(5, 60, CellSize), new RulesConfig(), 1, 0);
            UnitType slow = UnitTypes.CreateInfantryPlatoon();
            slow.NormalSpeed = 0.5f;

            Platoon normal = simulation.AddPlatoon("일반", Side.Blue, new Vec2(15f, 50f), 0f);
            Platoon slowed = simulation.AddPlatoon("느림", Side.Blue, new Vec2(35f, 50f), 0f, unitType: slow);
            simulation.OrderMove(normal, new[] { new Vec2(15f, 500f) }, MoveMode.Normal, false);
            simulation.OrderMove(slowed, new[] { new Vec2(35f, 500f) }, MoveMode.Normal, false);

            for (int i = 0; i < 10; i++)
            {
                simulation.Tick();
            }

            Assert.AreEqual(61f, (float)System.Math.Round(normal.Position.Y));
            Assert.AreEqual(55f, (float)System.Math.Round(slowed.Position.Y));
        }
    }
}
