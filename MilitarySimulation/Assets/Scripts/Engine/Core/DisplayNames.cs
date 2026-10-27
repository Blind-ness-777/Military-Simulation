namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 열거형의 표시용 한글 이름. 엔진 로그와 디버그 뷰가 같은 이름을 쓰도록 한곳에 둔다.
    /// </summary>
    public static class DisplayNames
    {
        public static string Of(MoveMode moveMode)
        {
            switch (moveMode)
            {
                case MoveMode.Fast:
                    return "신속";
                case MoveMode.Stealth:
                    return "은밀";
                default:
                    return "일반";
            }
        }

        public static string Of(ContactBehavior contactBehavior)
        {
            switch (contactBehavior)
            {
                case ContactBehavior.PushThrough:
                    return "돌파";
                case ContactBehavior.FallBack:
                    return "후퇴";
                default:
                    return "엄폐 후 응사";
            }
        }

        public static string Of(CommsState commsState)
        {
            switch (commsState)
            {
                case CommsState.Direct:
                    return "직접";
                case CommsState.Degraded:
                    return "저하";
                case CommsState.Cut:
                    return "두절";
                default:
                    return "정상";
            }
        }

        public static string Of(FirePolicy firePolicy)
        {
            switch (firePolicy)
            {
                case FirePolicy.CloseOnly:
                    return "근접 시만";
                case FirePolicy.HoldFire:
                    return "사격 금지";
                default:
                    return "자유 사격";
            }
        }

        public static string Of(MissionResult result)
        {
            switch (result)
            {
                case MissionResult.Victory:
                    return "임무 성공";
                case MissionResult.Defeat:
                    return "임무 실패";
                case MissionResult.Draw:
                    return "무승부";
                default:
                    return "진행 중";
            }
        }

        public static string Of(CommanderAction action)
        {
            switch (action)
            {
                case CommanderAction.Reinforce:
                    return "증원";
                case CommanderAction.FallBack:
                    return "후퇴";
                case CommanderAction.Counterattack:
                    return "역습";
                default:
                    return "고수";
            }
        }
    }
}
