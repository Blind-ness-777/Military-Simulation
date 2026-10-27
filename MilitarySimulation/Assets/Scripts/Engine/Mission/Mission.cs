using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    public enum MissionResult
    {
        InProgress,
        Victory,
        Defeat,
        Draw
    }

    /// <summary>
    /// 한 진영의 임무(시나리오 데이터).
    /// 단계(Steps)는 순서대로 완료해야 하고, 조건(Constraints)은 판 내내 지켜야 한다.
    /// 제한 시간은 시나리오마다 선택이며, 시간이 끝났을 때의 결과도 시나리오가 정한다
    /// (방어 임무는 "제한 시간 끝 = 승리").
    /// </summary>
    public class Mission
    {
        private readonly List<Objective> _steps = new List<Objective>();
        private readonly List<Objective> _constraints = new List<Objective>();

        public Mission(Side owner, string name)
        {
            Owner = owner;
            Name = name;
        }

        public Side Owner { get; }
        public string Name { get; }

        /// <summary>순서대로 완료해야 하는 목표(확보, 폭파, 철수).</summary>
        public IReadOnlyList<Objective> Steps => _steps;

        /// <summary>판 내내 지켜야 하는 목표(방어).</summary>
        public IReadOnlyList<Objective> Constraints => _constraints;

        /// <summary>제한 시간(초). 0이면 없음.</summary>
        public int TimeLimitSeconds { get; set; }

        /// <summary>제한 시간이 끝났을 때의 결과.</summary>
        public MissionResult TimeoutResult { get; set; } = MissionResult.Defeat;

        public MissionResult Result { get; internal set; } = MissionResult.InProgress;
        public bool IsFinished => Result != MissionResult.InProgress;

        /// <summary>결과가 난 이유(로그·결과 화면용).</summary>
        public string ResultReason { get; internal set; }

        /// <summary>진행 중인 단계. 단계가 없거나 모두 끝났으면 null.</summary>
        public Objective CurrentStep
        {
            get
            {
                foreach (Objective step in _steps)
                {
                    if (step.State != ObjectiveState.Complete)
                    {
                        return step;
                    }
                }

                return null;
            }
        }

        public Objective AddStep(Objective objective)
        {
            _steps.Add(objective);
            return objective;
        }

        public Objective AddConstraint(Objective objective)
        {
            _constraints.Add(objective);
            return objective;
        }
    }
}
