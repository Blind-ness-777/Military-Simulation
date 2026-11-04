using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 요지 그래프와 격자 경로 탐색 연결. 요지는 시나리오가 추가하고(AddKeyTerrain), 그래프는 처음 쓸 때 만든다
    /// (시야 영역·구간 계산이 무거워서, 쓰지 않는 판·테스트는 비용을 내지 않게).
    /// 둘 다 판정 상태와 난수를 건드리지 않으므로, 추가해도 기존 판의 결과는 같다.
    /// </summary>
    public partial class Simulation
    {
        private readonly List<KeyTerrain> _keyTerrainData = new List<KeyTerrain>();
        private GridPathfinder _pathfinder;
        private KeyTerrainGraph _keyTerrain;

        public GridPathfinder Pathfinder => _pathfinder ?? (_pathfinder = new GridPathfinder(Map, Rules));

        /// <summary>요지 그래프. 처음 읽을 때 계산한다. 요지가 없으면 빈 그래프.</summary>
        public KeyTerrainGraph KeyTerrain =>
            _keyTerrain ?? (_keyTerrain = new KeyTerrainGraph(
                Map, Rules, Pathfinder, _keyTerrainData, UnitTypes.CreateInfantryPlatoon()));

        /// <summary>요지를 추가한다(시나리오 구성 단계). 그래프를 이미 만든 뒤에는 추가할 수 없다.</summary>
        public KeyTerrain AddKeyTerrain(string name, KeyTerrainKind kind, Vec2 center, float radius)
        {
            if (_keyTerrain != null)
            {
                throw new InvalidOperationException("요지 그래프를 만든 뒤에는 요지를 추가할 수 없습니다.");
            }

            var keyTerrain = new KeyTerrain(name, kind, Map.ClampInside(center), radius);
            _keyTerrainData.Add(keyTerrain);
            return keyTerrain;
        }

        /// <summary>
        /// from에서 to까지 걸어갈 경로(from을 뺀 지점들, 마지막은 to). 지휘관이 계획을 명령으로 옮길 때 쓴다.
        /// exposure는 경로를 짜는 쪽이 아는 것으로만 만들어야 한다(반칙 금지).
        /// </summary>
        public List<Vec2> PlanPath(Vec2 from, Vec2 to, MoveMode moveMode, ExposureField exposure = null)
        {
            return Pathfinder.FindPath(from, to, moveMode, exposure);
        }

        /// <summary>
        /// 그 진영 상황판의 접촉(확인·소실)을 관측자로 본 노출 지도. 확인 = 무게 1, 소실 = 0.5.
        /// 디버그의 경로 탐색 이동과, 의심 지점이 생기기 전까지의 임시 노출 기준으로 쓴다.
        /// </summary>
        public ExposureField BuildContactExposure(Side side)
        {
            var field = new ExposureField(Map);
            UnitType observer = UnitTypes.CreateInfantryPlatoon();
            foreach (Contact contact in _intel[side].Contacts)
            {
                float weight = contact.Confidence == ContactConfidence.Confirmed ? 1f : 0.5f;
                field.AddObserver(
                    contact.LastKnownPosition, weight, observer.EyeHeight, observer.TargetHeight, observer.MaxVisualRange,
                    Rules.ForestCanopyHeight);
            }

            return field;
        }
    }
}
