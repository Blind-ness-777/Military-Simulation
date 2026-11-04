using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 엄폐 찾기와 제압 중 이동. 제압은 "못 움직임"이 아니라 "앞으로 못 감"이다:
    /// 위협 쪽으로는 거의 못 가고, 옆·뒤·엄폐 쪽으로는 느리게 가며, 명령이 없으면 스스로 가까운 엄폐로 기어간다.
    /// 위협은 소대가 아는 적만 쓴다(자기가 추적 중인 적, 통신이 되면 본부의 확인된 접촉 — 붕괴 후퇴와 같은 규칙).
    /// </summary>
    public partial class Simulation
    {
        private readonly List<KnownThreat> _knownThreats = new List<KnownThreat>();

        /// <summary>
        /// 제압된 채 멈춰 있고 엄폐 밖이면 가까운 엄폐로 스스로 간다. 경계 중이었으면 엄폐에 닿은 뒤 경계를 되살린다.
        /// 대기 지점에서 기다리는 소대는 움직이지 않는다(대기 지점의 의미를 지키려고).
        /// </summary>
        private void UpdatePinnedCover()
        {
            foreach (Platoon platoon in _platoons)
            {
                if (!platoon.IsPinned || platoon.IsDestroyed || platoon.IsBroken
                    || platoon.IsMoving || platoon.IsWaiting || ElapsedSeconds < platoon.NextCoverSearchSeconds)
                {
                    continue;
                }

                if (!TryFindCover(platoon, out Vec2 coverPoint, out bool alreadyInCover))
                {
                    platoon.NextCoverSearchSeconds = ElapsedSeconds + Rules.CoverSearchRetrySeconds;
                    continue;
                }

                if (alreadyInCover)
                {
                    // 위협이 움직이면 지금 자리가 엄폐가 아니게 될 수 있어서 같은 간격으로 다시 본다.
                    platoon.NextCoverSearchSeconds = ElapsedSeconds + Rules.CoverSearchRetrySeconds;
                    continue;
                }

                platoon.StartCoverMove(coverPoint, true);
                Report(platoon, $"{platoon.Name} 제압 — {Map.GetGridReference(coverPoint)} 엄폐로 이동");
            }
        }

        /// <summary>
        /// 가장 가까운 엄폐 칸을 찾는다. 엄폐 = 숲 칸, 또는 아는 위협 전부에서 시야선이 가리는 칸.
        /// 아는 위협이 없으면 숲 칸만 엄폐로 본다(어디서 쏘는지 모르므로).
        /// 지금 자리가 엄폐면 alreadyInCover. 반경 안 칸을 정해진 순서로 훑어서 결과가 결정론적이다.
        /// </summary>
        private bool TryFindCover(Platoon platoon, out Vec2 coverPoint, out bool alreadyInCover)
        {
            CollectKnownThreats(platoon);

            if (IsCoverFor(platoon, platoon.Position))
            {
                coverPoint = platoon.Position;
                alreadyInCover = true;
                return true;
            }

            alreadyInCover = false;
            coverPoint = default;
            float radius = Rules.CoverSearchRadius;
            float cellSize = Map.CellSize;
            int minX = Math.Max(0, (int)Math.Floor((platoon.Position.X - radius) / cellSize));
            int maxX = Math.Min(Map.Width - 1, (int)Math.Floor((platoon.Position.X + radius) / cellSize));
            int minY = Math.Max(0, (int)Math.Floor((platoon.Position.Y - radius) / cellSize));
            int maxY = Math.Min(Map.Height - 1, (int)Math.Floor((platoon.Position.Y + radius) / cellSize));

            bool found = false;
            float bestDistance = float.MaxValue;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vec2 center = Map.GetCellCenter(x, y);
                    float distance = Vec2.Distance(platoon.Position, center);
                    if (distance > radius || distance >= bestDistance || !IsCoverFor(platoon, center))
                    {
                        continue;
                    }

                    found = true;
                    bestDistance = distance;
                    coverPoint = center;
                }
            }

            return found;
        }

        /// <summary>CollectKnownThreats로 모은 위협 기준으로 그 자리가 엄폐인지.</summary>
        private bool IsCoverFor(Platoon platoon, Vec2 position)
        {
            if (Map.GetTerrainAt(position) == TerrainType.Forest)
            {
                return true;
            }

            if (_knownThreats.Count == 0)
            {
                return false;
            }

            foreach (KnownThreat threat in _knownThreats)
            {
                if (LineOfSight.HasLineOfSight(
                    Map, threat.Position, threat.EyeHeight, position, platoon.UnitType.TargetHeight, Rules.ForestCanopyHeight))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 제압된 소대의 속도 비율. 진행 방향이 가장 가까운 아는 위협 쪽이면 전진(거의 못 감), 아니면 느리게.
        /// 스스로 엄폐로 가는 중이거나 위협을 모르면 "전진"으로 보지 않는다.
        /// </summary>
        private float GetPinnedSpeedFactor(Platoon platoon)
        {
            if (platoon.IsMovingToCover || !platoon.IsMoving)
            {
                return Rules.PinnedOtherSpeedFactor;
            }

            CollectKnownThreats(platoon);
            bool found = false;
            Vec2 nearest = default;
            float nearestDistance = float.MaxValue;
            foreach (KnownThreat threat in _knownThreats)
            {
                float distance = Vec2.Distance(platoon.Position, threat.Position);
                if (distance < nearestDistance)
                {
                    found = true;
                    nearest = threat.Position;
                    nearestDistance = distance;
                }
            }

            Vec2 moveTarget = platoon.Waypoints[0].Position;
            if (!found || Vec2.Distance(platoon.Position, moveTarget) < 1e-4f)
            {
                return Rules.PinnedOtherSpeedFactor;
            }

            float moveBearing = Bearing.Between(platoon.Position, moveTarget);
            float threatBearing = Bearing.Between(platoon.Position, nearest);
            bool advancing = Bearing.Difference(moveBearing, threatBearing) <= Rules.PinnedForwardArcDegrees * 0.5f;
            return advancing ? Rules.PinnedForwardSpeedFactor : Rules.PinnedOtherSpeedFactor;
        }

        /// <summary>소대가 아는 적 위치를 _knownThreats에 모은다(부대 목록 순서, 결정론).</summary>
        private void CollectKnownThreats(Platoon platoon)
        {
            _knownThreats.Clear();
            foreach (Platoon enemy in _platoons)
            {
                if (enemy.Side == platoon.Side || !TryGetKnownEnemyPosition(platoon, enemy, out Vec2 position))
                {
                    continue;
                }

                _knownThreats.Add(new KnownThreat(position, enemy.UnitType.EyeHeight));
            }
        }

        private readonly struct KnownThreat
        {
            public readonly Vec2 Position;
            public readonly float EyeHeight;

            public KnownThreat(Vec2 position, float eyeHeight)
            {
                Position = position;
                EyeHeight = eyeHeight;
            }
        }
    }
}
