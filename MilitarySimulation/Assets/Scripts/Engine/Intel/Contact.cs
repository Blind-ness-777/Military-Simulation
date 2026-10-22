namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 진영 원장의 접촉 정보 한 건. 진영이 "안다고 믿는 것"이다.
    /// </summary>
    public class Contact
    {
        internal Contact(int contactNumber, int targetId)
        {
            ContactNumber = contactNumber;
            TargetId = targetId;
        }

        /// <summary>진영 안에서 붙인 접촉 번호. 화면에는 이 번호만 보여준다.</summary>
        public int ContactNumber { get; }

        /// <summary>
        /// 어느 실제 부대에 대한 접촉인지(엔진 내부 대응용).
        /// 실제 부대 이름이나 상태를 화면에 드러내는 데 쓰면 안 된다.
        /// </summary>
        public int TargetId { get; }

        public Vec2 LastKnownPosition { get; internal set; }
        public int LastSeenSeconds { get; internal set; }
        public ContactConfidence Confidence { get; internal set; }
    }
}
