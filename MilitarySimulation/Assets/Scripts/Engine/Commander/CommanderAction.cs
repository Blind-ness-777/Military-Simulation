namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 적 지휘관이 결정 지점에서 고르는 행동(방어하는 적, 데모 1단계).
    /// 후보는 "행동 × 진지"로 만들어 점수를 매긴다(EnemyCommander).
    /// </summary>
    public enum CommanderAction
    {
        /// <summary>진지를 지킨다. 진지 소대의 경계 방향을 위협 쪽으로 돌린다.</summary>
        Hold,

        /// <summary>예비대를 위협받는 진지로 보낸다.</summary>
        Reinforce,

        /// <summary>진지 소대(와 그 진지로 간 예비대)를 후퇴 위치로 뺀다. 그 진지는 포기한다.</summary>
        FallBack,

        /// <summary>예비대를 진지에 다가온 적의 마지막 확인 위치로 돌격시킨다. 국지적으로 우세할 때만 후보가 된다.</summary>
        Counterattack
    }
}
