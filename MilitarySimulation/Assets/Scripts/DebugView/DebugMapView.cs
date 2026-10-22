using MilitarySimulation.Engine;
using UnityEngine;

namespace MilitarySimulation.DebugView
{
    /// <summary>
    /// 높이맵을 텍스처 한 장(지형 색 + 고도 음영 + 등고선)으로 그려 y = 0 평면에 깔고,
    /// 메인 카메라를 지도 전체가 보이는 고정 탑뷰로 맞춘다.
    /// 엔진의 MapData만 읽으며, 판정에는 관여하지 않는다.
    /// </summary>
    public class DebugMapView : MonoBehaviour
    {
        private const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        [SerializeField] private int _pixelsPerCell = 4;
        [SerializeField] private float _contourInterval = 5f;

        /// <summary>굵게 그리는 계곡선(주곡선) 간격. 높이를 한눈에 세기 위한 기준선.</summary>
        [SerializeField] private float _indexContourInterval = 25f;

        // 개활지는 높이에 따라 낮음 → 중간 → 높음 세 색 사이를 보간한다(등고 채색).
        [SerializeField] private Color _openLowColor = new Color(0.42f, 0.62f, 0.34f);
        [SerializeField] private Color _openMidColor = new Color(0.80f, 0.76f, 0.46f);
        [SerializeField] private Color _openHighColor = new Color(0.74f, 0.52f, 0.34f);
        [SerializeField] private Color _forestColor = new Color(0.16f, 0.36f, 0.18f);
        [SerializeField] private Color _roadColor = new Color(0.85f, 0.80f, 0.66f);
        [SerializeField] private Color _contourColor = new Color(0.25f, 0.20f, 0.12f);
        [SerializeField] private float _hillShadeStrength = 2.5f;

        private GameObject _mapQuad;
        private Texture2D _texture;
        private Material _material;

        public void Initialize(MapData map, Camera targetCamera)
        {
            _texture = BuildTexture(map);
            CreateMapQuad(map);
            FrameCamera(map, targetCamera);
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
            if (_material != null) Destroy(_material);
            if (_mapQuad != null) Destroy(_mapQuad);
        }

        private Texture2D BuildTexture(MapData map)
        {
            int width = map.Width * _pixelsPerCell;
            int height = map.Height * _pixelsPerCell;
            float metersPerPixel = map.CellSize / _pixelsPerCell;
            float maxHeight = GetMaxHeight(map);

            var heights = new float[width * height];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    var point = new Vec2((px + 0.5f) * metersPerPixel, (py + 0.5f) * metersPerPixel);
                    heights[py * width + px] = map.GetHeightAt(point);
                }
            }

            var pixels = new Color[width * height];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    int index = py * width + px;
                    var point = new Vec2((px + 0.5f) * metersPerPixel, (py + 0.5f) * metersPerPixel);
                    float h = heights[index];

                    Color color = GetTerrainColor(map.GetTerrainAt(point), maxHeight > 0f ? h / maxHeight : 0f);
                    color *= GetHillShade(heights, width, height, px, py, metersPerPixel);

                    if (IsContour(heights, width, height, px, py, _indexContourInterval, 1))
                    {
                        color = Color.Lerp(color, _contourColor, 0.9f);
                    }
                    else if (IsContour(heights, width, height, px, py, _contourInterval, 0))
                    {
                        color = Color.Lerp(color, _contourColor, 0.45f);
                    }

                    color.a = 1f;
                    pixels[index] = color;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "DebugMapTexture"
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private Color GetTerrainColor(TerrainType terrain, float normalizedHeight)
        {
            switch (terrain)
            {
                case TerrainType.Forest:
                    return _forestColor;
                case TerrainType.Road:
                    return _roadColor;
                default:
                    return normalizedHeight < 0.5f
                        ? Color.Lerp(_openLowColor, _openMidColor, normalizedHeight * 2f)
                        : Color.Lerp(_openMidColor, _openHighColor, (normalizedHeight - 0.5f) * 2f);
            }
        }

        /// <summary>북서쪽 광원 기준의 간단한 음영. 경사가 눈에 보이게 하는 용도.</summary>
        private float GetHillShade(float[] heights, int width, int height, int px, int py, float metersPerPixel)
        {
            int left = Mathf.Max(px - 1, 0);
            int right = Mathf.Min(px + 1, width - 1);
            int down = Mathf.Max(py - 1, 0);
            int up = Mathf.Min(py + 1, height - 1);

            float dx = (heights[py * width + right] - heights[py * width + left]) / ((right - left) * metersPerPixel);
            float dy = (heights[up * width + px] - heights[down * width + px]) / ((up - down) * metersPerPixel);

            // 면의 법선 ∝ (-dx, -dy, 1), 광원 방향 ∝ (-1, +1)(북서) → 내적 ∝ dx - dy.
            // 북서쪽을 향한 경사는 밝게, 남동쪽을 향한 경사는 어둡게
            float shade = 1f + (dx - dy) * _hillShadeStrength;
            return Mathf.Clamp(shade, 0.6f, 1.25f);
        }

        /// <summary>
        /// 이 픽셀과 주변 픽셀이 다른 높이 구간에 속하면 등고선으로 본다.
        /// thickness가 1이면 양옆 1픽셀까지 검사해서 선이 두꺼워진다.
        /// </summary>
        private static bool IsContour(float[] heights, int width, int height, int px, int py, float interval, int thickness)
        {
            if (interval <= 0f)
            {
                return false;
            }

            int band = Mathf.FloorToInt(heights[py * width + px] / interval);
            for (int oy = -1 - thickness; oy <= thickness; oy++)
            {
                for (int ox = -1 - thickness; ox <= thickness; ox++)
                {
                    int nx = px + ox;
                    int ny = py + oy;
                    if ((ox == 0 && oy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    if (Mathf.FloorToInt(heights[ny * width + nx] / interval) != band)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CreateMapQuad(MapData map)
        {
            Shader shader = Shader.Find(UnlitShaderName);
            if (shader == null)
            {
                // 디버그 뷰 전용이라 빌드 포함 여부를 따로 관리하지 않는다. 에디터 실행 기준.
                Debug.LogError($"[DebugMapView] '{UnlitShaderName}' 셰이더를 찾지 못했습니다. URP 설정을 확인하세요.");
                return;
            }

            _material = new Material(shader) { name = "DebugMapMaterial" };
            _material.SetTexture("_BaseMap", _texture);
            _material.mainTexture = _texture;

            _mapQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _mapQuad.name = "DebugMap";
            Destroy(_mapQuad.GetComponent<Collider>());
            _mapQuad.transform.SetParent(transform, false);
            _mapQuad.transform.position = new Vector3(map.WidthMeters * 0.5f, 0f, map.HeightMeters * 0.5f);
            // Quad는 기본적으로 -Z를 향한다. X축 90도 회전으로 위(+Y)를 향하게, 텍스처 위쪽이 북쪽(+Z)이 되게 한다.
            _mapQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _mapQuad.transform.localScale = new Vector3(map.WidthMeters, map.HeightMeters, 1f);
            _mapQuad.GetComponent<MeshRenderer>().sharedMaterial = _material;
        }

        private static void FrameCamera(MapData map, Camera targetCamera)
        {
            if (targetCamera == null)
            {
                return;
            }

            const float margin = 1.08f;
            float halfHeight = map.HeightMeters * 0.5f;
            float halfWidthAsHeight = map.WidthMeters * 0.5f / Mathf.Max(targetCamera.aspect, 0.01f);

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = Mathf.Max(halfHeight, halfWidthAsHeight) * margin;
            targetCamera.transform.SetPositionAndRotation(
                new Vector3(map.WidthMeters * 0.5f, 500f, map.HeightMeters * 0.5f),
                Quaternion.Euler(90f, 0f, 0f));
            targetCamera.nearClipPlane = 1f;
            targetCamera.farClipPlane = 1000f;
        }

        private static float GetMaxHeight(MapData map)
        {
            float max = 0f;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    max = Mathf.Max(max, map.GetCellHeight(x, y));
                }
            }

            return max;
        }
    }
}
