using System.Collections.Generic;

namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 한 진영의 정보 원장. 플레이어 화면과 (이후) 적 지휘관 AI는 이것만 보고 판단한다.
    /// </summary>
    public class SideIntel
    {
        private readonly List<Contact> _contacts = new List<Contact>();
        private readonly Dictionary<int, Contact> _contactsByTarget = new Dictionary<int, Contact>();

        internal SideIntel(Side side)
        {
            Side = side;
        }

        public Side Side { get; }

        /// <summary>등록 순서대로 정렬된 접촉 목록.</summary>
        public IReadOnlyList<Contact> Contacts => _contacts;

        /// <summary>실제 부대 Id로 접촉을 찾는다. 사격선 표시처럼 엔진이 아는 표적을 진영 정보로 옮길 때 쓴다.</summary>
        public bool TryGetContact(int targetId, out Contact contact)
        {
            return _contactsByTarget.TryGetValue(targetId, out contact);
        }

        internal Contact AddContact(int targetId)
        {
            var contact = new Contact(_contacts.Count + 1, targetId);
            _contacts.Add(contact);
            _contactsByTarget.Add(targetId, contact);
            return contact;
        }
    }
}
