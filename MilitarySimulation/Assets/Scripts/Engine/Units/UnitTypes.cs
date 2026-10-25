namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 기본 병과 정의. 호출할 때마다 새 인스턴스를 돌려준다.
    /// (공유 인스턴스를 테스트나 시나리오에서 고치면 다른 곳까지 바뀌기 때문이다.)
    /// 파일(JSON)에서 읽어 오는 것은 맵·시나리오 로딩 단계에서 붙인다.
    /// </summary>
    public static class UnitTypes
    {
        public static UnitType CreateInfantryPlatoon()
        {
            return new UnitType("보병 소대");
        }

        /// <summary>본부(지휘 분대). 인원이 적고, 위성통신과 더 높은 안테나를 가진다.</summary>
        public static UnitType CreateHeadquarters()
        {
            return new UnitType("본부")
            {
                DefaultStrength = 6,
                DefaultCommsEquipment = CommsEquipment.LineOfSightRadio | CommsEquipment.Satcom,
                AntennaHeight = 3f
            };
        }
    }
}
