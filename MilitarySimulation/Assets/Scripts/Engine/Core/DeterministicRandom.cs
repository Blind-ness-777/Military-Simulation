namespace MilitarySimulation.Engine
{
    /// <summary>
    /// 시드 기반 난수(SplitMix64).
    /// System.Random은 런타임(Unity Mono / IL2CPP / .NET)마다 구현이 달라질 수 있어서,
    /// 같은 시드면 어디서 돌려도 같은 결과가 나오도록 직접 구현한다.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(int seed)
        {
            _state = unchecked((ulong)seed);
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>[0, 1) 범위의 실수.</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / (1UL << 53));
        }
    }
}
