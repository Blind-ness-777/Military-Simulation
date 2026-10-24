using System;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 부대가 가진 통신 수단. 본부와 부대 사이 통신은 양쪽이 공통으로 가진 수단 중 가장 좋은 것으로 판정한다.
    /// 중계 드론은 드론 부대가 생기면 추가한다.
    /// </summary>
    [Flags]
    public enum CommsEquipment
    {
        None = 0,

        /// <summary>가시선(VHF/UHF) 무전기. 산이나 깊은 골짜기에 막힌다.</summary>
        LineOfSightRadio = 1,

        /// <summary>위성통신. 지형과 무관하지만 하늘이 트여 있어야 한다(숲·깊은 골짜기에서 막힘).</summary>
        Satcom = 2
    }
}
