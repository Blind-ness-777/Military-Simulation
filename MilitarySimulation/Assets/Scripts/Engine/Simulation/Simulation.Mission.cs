using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 임무 판정. 심판의 판정이라 실제 상태(진실)로 한다.
    /// 플레이어가 진행 상황을 얼마나 아는지는 화면이 보고 상태를 보고 정한다.
    /// 어느 진영이든 임무가 끝나면 판이 끝나고, 그 뒤로 Tick은 아무것도 하지 않는다.
    /// </summary>
    public partial class Simulation
    {
        private readonly Dictionary<Side, Mission> _missions = new Dictionary<Side, Mission>();

        /// <summary>임무 결과가 나서 판이 끝났는지.</summary>
        public bool IsFinished
        {
            get
            {
                foreach (Mission mission in _missions.Values)
                {
                    if (mission.IsFinished)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void SetMission(Mission mission)
        {
            _missions[mission.Owner] = mission;
        }

        public bool TryGetMission(Side side, out Mission mission)
        {
            return _missions.TryGetValue(side, out mission);
        }

        /// <summary>
        /// 구역을 이 진영이 확보하고 있는지: 싸울 수 있는(전투 불능·붕괴 아님) 아군이 구역 안에 있고, 전투 불능이 아닌 적은 없다.
        /// 사기가 붕괴한 적도 구역 안에 남아 있으면 확보가 아니다(진지에 적이 남아 있는데 "확보"가 뜨지 않게).
        /// 붕괴한 적은 스스로 물러나므로 영원히 막지는 않는다.
        /// </summary>
        public bool IsZoneHeldBy(Side side, Objective zone)
        {
            bool present = false;
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.IsDestroyed || !zone.Contains(platoon.Position))
                {
                    continue;
                }

                if (platoon.Side != side)
                {
                    return false;
                }

                if (!platoon.IsBroken)
                {
                    present = true;
                }
            }

            return present;
        }

        private void UpdateMissions()
        {
            // 진영 순서를 고정해 로그 순서가 결정론적이게 한다.
            foreach (Side side in AllSides)
            {
                if (_missions.TryGetValue(side, out Mission mission) && !mission.IsFinished)
                {
                    UpdateMission(mission);
                }
            }
        }

        private void UpdateMission(Mission mission)
        {
            foreach (Objective constraint in mission.Constraints)
            {
                UpdateConstraint(mission, constraint);
                if (mission.IsFinished)
                {
                    return;
                }
            }

            UpdateSteps(mission);
            if (mission.IsFinished)
            {
                return;
            }

            if (IsWipedOut(mission.Owner))
            {
                Finish(mission, MissionResult.Defeat, "전 소대 전투 불능");
                return;
            }

            if (mission.TimeLimitSeconds > 0 && ElapsedSeconds >= mission.TimeLimitSeconds)
            {
                Finish(mission, mission.TimeoutResult, "제한 시간 종료");
            }
        }

        /// <summary>지역 방어: 적이 구역을 HoldSeconds 동안 확보하면 실패.</summary>
        private void UpdateConstraint(Mission mission, Objective constraint)
        {
            if (constraint.Kind != ObjectiveKind.HoldZone || constraint.State == ObjectiveState.Failed)
            {
                return;
            }

            constraint.State = ObjectiveState.Active;
            bool enemyHolds = IsZoneHeldBy(Opponent(mission.Owner), constraint);
            constraint.ProgressSeconds = enemyHolds ? constraint.ProgressSeconds + TickSeconds : 0;

            if (constraint.ProgressSeconds >= constraint.HoldSeconds)
            {
                constraint.State = ObjectiveState.Failed;
                Finish(mission, MissionResult.Defeat, $"{constraint.Name} 상실");
            }
        }

        private void UpdateSteps(Mission mission)
        {
            Objective step = mission.CurrentStep;
            if (step == null)
            {
                if (mission.Steps.Count > 0)
                {
                    Finish(mission, MissionResult.Victory, "모든 목표 달성");
                }

                return;
            }

            if (step.State == ObjectiveState.Waiting)
            {
                ActivateStep(mission, step);
            }

            bool done;
            switch (step.Kind)
            {
                case ObjectiveKind.SeizeZone:
                    bool held = IsZoneHeldBy(mission.Owner, step);
                    step.ProgressSeconds = held ? step.ProgressSeconds + TickSeconds : 0;
                    done = step.ProgressSeconds >= step.HoldSeconds;
                    break;

                case ObjectiveKind.DestroyTarget:
                    // 작업은 누적된다. 작업할 소대가 없거나 제압되면 멈췄다가 다시 이어서 한다.
                    if (HasWorkingPlatoon(mission.Owner, step))
                    {
                        step.ProgressSeconds += TickSeconds;
                    }

                    done = step.ProgressSeconds >= step.WorkSeconds;
                    break;

                case ObjectiveKind.Exfiltrate:
                    done = AllSurvivorsInside(mission.Owner, step);
                    break;

                default:
                    // 지역 방어는 조건(Constraints)으로만 쓴다. 단계에 넣으면 끝나지 않는다.
                    done = false;
                    break;
            }

            if (!done)
            {
                return;
            }

            step.State = ObjectiveState.Complete;
            Log(mission.Owner, $"목표 달성: {DescribeObjective(step)}");

            Objective next = mission.CurrentStep;
            if (next == null)
            {
                Finish(mission, MissionResult.Victory, "모든 목표 달성");
                return;
            }

            // 다음 단계는 같은 틱에 바로 시작한다(화면에 다음 목표가 한 틱 늦게 뜨지 않게).
            ActivateStep(mission, next);
        }

        private void ActivateStep(Mission mission, Objective step)
        {
            step.State = ObjectiveState.Active;
            Log(mission.Owner, $"임무 목표: {DescribeObjective(step)}");
        }

        /// <summary>폭파 작업이 가능한 소대가 목표 반경 안에 있는지(전투 불능·붕괴·제압이 아님).</summary>
        private bool HasWorkingPlatoon(Side side, Objective target)
        {
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.Side == side && !platoon.IsDestroyed && !platoon.IsBroken && !platoon.IsPinned
                    && target.Contains(platoon.Position))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>살아 있는(전투 불능이 아닌) 소대가 전부 구역 안에 있는지. 붕괴한 소대도 포함한다.</summary>
        private bool AllSurvivorsInside(Side side, Objective zone)
        {
            bool any = false;
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.Side != side || platoon.IsDestroyed)
                {
                    continue;
                }

                if (!zone.Contains(platoon.Position))
                {
                    return false;
                }

                any = true;
            }

            return any;
        }

        private bool IsWipedOut(Side side)
        {
            foreach (Platoon platoon in _platoons)
            {
                if (platoon.Side == side && !platoon.IsDestroyed)
                {
                    return false;
                }
            }

            return true;
        }

        private void Finish(Mission mission, MissionResult result, string reason)
        {
            mission.Result = result;
            mission.ResultReason = reason;
            Log(mission.Owner, $"임무 종료: {DisplayNames.Of(result)} — {reason}");
        }

        private string DescribeObjective(Objective objective)
        {
            string where = Map.GetGridReference(objective.Center);
            switch (objective.Kind)
            {
                case ObjectiveKind.SeizeZone:
                    return $"{objective.Name} 확보 ({where})";
                case ObjectiveKind.HoldZone:
                    return $"{objective.Name} 방어 ({where})";
                case ObjectiveKind.DestroyTarget:
                    return $"{objective.Name} 폭파 ({where})";
                default:
                    return $"{objective.Name}(으)로 철수 ({where})";
            }
        }

        private static Side Opponent(Side side) => side == Side.Blue ? Side.Red : Side.Blue;
    }
}
