using System.Runtime.CompilerServices;

// 판정 선례 테스트가 내부 상태(예: 통신병 손실)를 직접 만들어 검사할 수 있게 한다.
[assembly: InternalsVisibleTo("MilitarySimulation.Engine.Tests")]
