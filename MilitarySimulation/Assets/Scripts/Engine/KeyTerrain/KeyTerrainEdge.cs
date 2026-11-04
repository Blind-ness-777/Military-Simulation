using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 이웃한 두 요지를 잇는 구간. 불러올 때 일반 이동 비용(노출 없음)으로 찾은 격자 경로를 기준으로 성질을 계산한다.
    /// 다른 요지를 지나가는 경로는 구간이 아니다(그 요지를 거치는 두 구간으로 표현된다).
    /// </summary>
    public sealed class KeyTerrainEdge
    {
        internal KeyTerrainEdge(
            int fromIndex,
            int toIndex,
            IReadOnlyList<Vec2> path,
            float length,
            float coverRatio,
            float[] visibilityByNode)
        {
            FromIndex = fromIndex;
            ToIndex = toIndex;
            Path = path;
            Length = length;
            CoverRatio = coverRatio;
            _visibilityByNode = visibilityByNode;
        }

        private readonly float[] _visibilityByNode;

        public int FromIndex { get; }
        public int ToIndex { get; }

        /// <summary>구간을 따라가는 경로(출발 요지 중심 다음 점부터 도착 요지 중심까지, 펴고 다듬은 것). 그리기·참고용.</summary>
        public IReadOnlyList<Vec2> Path { get; }

        /// <summary>격자 경로 길이(m).</summary>
        public float Length { get; }

        /// <summary>경로 칸 중 숲 칸의 비율(0~1).</summary>
        public float CoverRatio { get; }

        /// <summary>
        /// 경로 칸 중 그 요지에서 보이는 칸의 비율(0~1). 의심 지점(그 요지에 적이 있을 확률)과 곱하면 예상 노출이 된다.
        /// 구간 양 끝 요지 자신도 값이 있다(보통 높다).
        /// </summary>
        public float GetVisibilityFrom(int nodeIndex) => _visibilityByNode[nodeIndex];

        public int GetOtherEnd(int nodeIndex) => nodeIndex == FromIndex ? ToIndex : FromIndex;
    }
}
