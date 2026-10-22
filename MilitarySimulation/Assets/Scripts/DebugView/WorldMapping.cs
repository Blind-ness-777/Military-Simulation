using MilitarySimulation.Engine;
using UnityEngine;

namespace MilitarySimulation.DebugView
{
    /// <summary>
    /// 엔진 좌표(미터, X = 동, Y = 북) ↔ Unity 월드 좌표(X = 동, Z = 북, Y = 위) 변환.
    /// 디버그 뷰는 지도를 y = 0 평면에 깐다.
    /// </summary>
    public static class WorldMapping
    {
        public static Vector3 ToWorld(Vec2 position, float height = 0f)
        {
            return new Vector3(position.X, height, position.Y);
        }

        public static Vec2 ToEngine(Vector3 worldPosition)
        {
            return new Vec2(worldPosition.x, worldPosition.z);
        }

        public static Vector3 BearingToWorldDirection(float bearingDegrees)
        {
            Vec2 direction = Bearing.ToDirection(bearingDegrees);
            return new Vector3(direction.X, 0f, direction.Y);
        }
    }
}
