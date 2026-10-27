using System;
using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 적 지휘관 AI 연결. 지휘관은 보고가 도착한 직후(DeliverReports 다음) 매 틱 판단 기회를 얻고,
    /// 명령은 플레이어와 같은 공개 명령으로 내려 통신 지연을 똑같이 탄다.
    /// 지휘관을 붙이지 않은 판은 이전과 완전히 같게 진행된다(지휘관은 자기 난수를 따로 쓴다).
    /// </summary>
    public partial class Simulation
    {
        private readonly List<EnemyCommander> _commanders = new List<EnemyCommander>();

        public IReadOnlyList<EnemyCommander> Commanders => _commanders;

        /// <summary>진영당 하나. 그 진영에 본부가 없거나 본부가 전멸하면 지휘관은 아무것도 하지 않는다.</summary>
        public void AttachCommander(EnemyCommander commander)
        {
            if (commander == null) throw new ArgumentNullException(nameof(commander));
            foreach (EnemyCommander existing in _commanders)
            {
                if (existing.Side == commander.Side)
                {
                    throw new InvalidOperationException($"{commander.Side} 진영에는 이미 지휘관이 있습니다.");
                }
            }

            _commanders.Add(commander);
        }

        /// <summary>지휘관의 판단 기록을 그 진영 로그에 남긴다.</summary>
        internal void LogCommander(Side side, string text)
        {
            Log(side, text);
        }

        private void UpdateCommanders()
        {
            foreach (EnemyCommander commander in _commanders)
            {
                commander.Update(this);
            }
        }
    }
}
