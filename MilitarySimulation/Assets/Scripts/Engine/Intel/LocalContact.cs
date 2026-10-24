namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 소대가 직접 본 적 한 건(소대 인지). 본부 상황판(SideIntel)과 별개로, 이 소대의 사격·후퇴 판단에 쓰인다.
    /// 통신이 끊겨도 소대는 자기가 본 것은 안다.
    /// </summary>
    internal sealed class LocalContact
    {
        public LocalContact(int targetId)
        {
            TargetId = targetId;
        }

        public int TargetId { get; }
        public Vec2 LastKnownPosition { get; set; }
        public int LastSeenSeconds { get; set; }

        /// <summary>지금 시야선으로 추적 중인지. 놓친 뒤 ContactLostSeconds가 지나면 false.</summary>
        public bool IsTracking { get; set; }
    }
}
