using System.Collections.Generic;
using System.Text;
using MilitarySimulation.Engine;
using UnityEngine;

namespace MilitarySimulation.DebugView
{
    /// <summary>
    /// 부대·경로·경계 부채꼴·접촉을 동적 메시(선 + 채운 도형)로, 이름표·HUD·로그를 IMGUI로 그리는 디버그 표시.
    /// 기본은 플레이어 진영 정보만 보여주고(안개), ShowTruth일 때만 실제 적 위치를 함께 그린다.
    /// Gizmos 대신 메시를 쓰는 이유: Gizmos는 Game 뷰의 Gizmos 버튼 상태에 따라 보이지 않을 수 있다.
    /// 매 프레임 갱신은 SimulationController가 Refresh()를 호출해서 한다.
    /// </summary>
    public class DebugUnitView : MonoBehaviour
    {
        /// <summary>정점 색(투명도 포함)을 그대로 그리는 내장 셰이더.</summary>
        private const string VertexColorShaderName = "Sprites/Default";
        private const float MarkerHeight = 2f;
        private const float WatchDirectionLineLength = 120f;
        private const float WatchArcAlpha = 0.3f;
        private const int LostContactFadeSeconds = 300;
        private const int CircleSegments = 16;

        [SerializeField] private float _markerSize = 18f;
        [SerializeField] private int _logLineCount = 14;
        [SerializeField] private Color _friendlyColor = new Color(0.25f, 0.55f, 1f);
        [SerializeField] private Color _selectedColor = new Color(1f, 0.9f, 0.2f);
        [SerializeField] private Color _contactColor = new Color(0.95f, 0.2f, 0.2f);
        [SerializeField] private Color _truthColor = new Color(1f, 0.3f, 1f);
        [SerializeField] private Color _fireColor = new Color(1f, 0.6f, 0.1f);

        private readonly List<string> _logBuffer = new List<string>();
        private readonly StringBuilder _textBuilder = new StringBuilder();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _lineIndices = new List<int>();
        private readonly List<int> _triangleIndices = new List<int>();

        private SimulationController _controller;
        private Camera _camera;
        private GameObject _meshObject;
        private Mesh _mesh;
        private Material _material;
        private GUIStyle _labelStyle;
        private GUIStyle _panelStyle;

        public void Initialize(SimulationController controller, Camera targetCamera)
        {
            _controller = controller;
            _camera = targetCamera;
            CreateMeshObject();
        }

        /// <summary>현재 시뮬레이션 상태로 메시를 다시 만든다.</summary>
        public void Refresh()
        {
            if (_controller == null || _controller.Simulation == null || _mesh == null)
            {
                return;
            }

            _vertices.Clear();
            _colors.Clear();
            _lineIndices.Clear();
            _triangleIndices.Clear();

            Simulation simulation = _controller.Simulation;
            AddFriendlyPlatoons(simulation);
            AddContacts(simulation);
            if (_controller.ShowTruth)
            {
                AddTruth(simulation);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.subMeshCount = 2;
            _mesh.SetIndices(_lineIndices, MeshTopology.Lines, 0);
            _mesh.SetIndices(_triangleIndices, MeshTopology.Triangles, 1);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (_meshObject != null) Destroy(_meshObject);
        }

        private void CreateMeshObject()
        {
            Shader shader = Shader.Find(VertexColorShaderName);
            if (shader == null)
            {
                Debug.LogError($"[DebugUnitView] '{VertexColorShaderName}' 셰이더를 찾지 못했습니다.");
                return;
            }

            _material = new Material(shader) { name = "DebugUnitMaterial" };
            _mesh = new Mesh { name = "DebugUnitMesh" };
            _mesh.MarkDynamic();

            _meshObject = new GameObject("DebugUnits");
            _meshObject.transform.SetParent(transform, false);
            _meshObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var meshRenderer = _meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = new[] { _material, _material };
        }

        private void AddFriendlyPlatoons(Simulation simulation)
        {
            Platoon selected = _controller.SelectedPlatoon;
            foreach (Platoon platoon in simulation.Platoons)
            {
                if (platoon.Side != _controller.PlayerSide)
                {
                    continue;
                }

                Color color = platoon == selected ? _selectedColor : _friendlyColor;
                if (platoon.IsDestroyed)
                {
                    color = WithAlpha(color, 0.3f);
                }

                AddPlatoonMarker(platoon, color, true);
                AddPath(platoon, color);
                AddWatchArc(platoon, color, simulation.Rules.MaxVisualRange);
                AddFriendlyFireLine(simulation, platoon);
            }
        }

        /// <summary>아군 사격선은 진영 정보의 접촉 위치로 그린다(플레이어가 아는 표적 위치).</summary>
        private void AddFriendlyFireLine(Simulation simulation, Platoon shooter)
        {
            if (!shooter.IsFiring)
            {
                return;
            }

            SideIntel intel = simulation.GetIntel(shooter.Side);
            if (!intel.TryGetContact(shooter.CurrentTargetId, out Contact contact))
            {
                return;
            }

            AddLine(
                WorldMapping.ToWorld(shooter.Position, MarkerHeight),
                WorldMapping.ToWorld(contact.LastKnownPosition, MarkerHeight),
                _fireColor);
        }

        private void AddContacts(Simulation simulation)
        {
            SideIntel intel = simulation.GetIntel(_controller.PlayerSide);
            float half = _markerSize * 0.5f;

            foreach (Contact contact in intel.Contacts)
            {
                Vector3 center = WorldMapping.ToWorld(contact.LastKnownPosition, MarkerHeight);
                if (contact.Confidence == ContactConfidence.Confirmed)
                {
                    AddFilledSquare(center, half, _contactColor);
                }
                else
                {
                    // 소실된 접촉은 시간이 지날수록 흐려진다.
                    int age = simulation.ElapsedSeconds - contact.LastSeenSeconds;
                    float alpha = Mathf.Lerp(0.8f, 0.15f, Mathf.Clamp01((float)age / LostContactFadeSeconds));
                    AddSquareOutline(center, half, WithAlpha(_contactColor, alpha));
                }
            }
        }

        private void AddTruth(Simulation simulation)
        {
            foreach (Platoon platoon in simulation.Platoons)
            {
                if (platoon.Side == _controller.PlayerSide)
                {
                    continue;
                }

                AddPlatoonMarker(platoon, _truthColor, false);
                AddPath(platoon, _truthColor);
                AddWatchArc(platoon, _truthColor, simulation.Rules.MaxVisualRange);
                AddTruthFireLine(simulation, platoon);
            }
        }

        /// <summary>적의 사격선은 실제 표적 위치로 그린다. ShowTruth 전용.</summary>
        private void AddTruthFireLine(Simulation simulation, Platoon shooter)
        {
            if (!shooter.IsFiring)
            {
                return;
            }

            foreach (Platoon target in simulation.Platoons)
            {
                if (target.Id != shooter.CurrentTargetId)
                {
                    continue;
                }

                AddLine(
                    WorldMapping.ToWorld(shooter.Position, MarkerHeight),
                    WorldMapping.ToWorld(target.Position, MarkerHeight),
                    WithAlpha(_truthColor, 0.8f));
                return;
            }
        }

        private void AddPlatoonMarker(Platoon platoon, Color color, bool filled)
        {
            Vector3 center = WorldMapping.ToWorld(platoon.Position, MarkerHeight);
            float radius = _markerSize * 0.5f;

            if (filled)
            {
                AddFilledCircle(center, radius, color);
            }
            else
            {
                AddCircleOutline(center, radius, color);
            }

            Vector3 facing = WorldMapping.BearingToWorldDirection(platoon.FacingDegrees);
            AddLine(center, center + facing * _markerSize * 1.5f, color);
        }

        private void AddPath(Platoon platoon, Color color)
        {
            if (!platoon.IsMoving)
            {
                return;
            }

            Color pathColor = WithAlpha(color, 0.8f);
            Vector3 previous = WorldMapping.ToWorld(platoon.Position, MarkerHeight);
            foreach (Waypoint waypoint in platoon.Waypoints)
            {
                Vector3 next = WorldMapping.ToWorld(waypoint.Position, MarkerHeight);
                AddLine(previous, next, pathColor);
                AddSquareOutline(next, 3f, pathColor);
                previous = next;
            }
        }

        /// <summary>
        /// 경계 부채꼴. 실제 탐지 거리(visualRange)까지 옅게 그린다.
        /// 부채꼴은 "이 거리까지 본다"가 아니라 "이 방향의 발견 확률이 높다"는 뜻이라서,
        /// 짧게 그리면 그 밖은 안전하다고 오해하게 된다.
        /// </summary>
        private void AddWatchArc(Platoon platoon, Color color, float visualRange)
        {
            if (!platoon.IsWatching)
            {
                return;
            }

            Color arcColor = WithAlpha(color, WatchArcAlpha);
            Vector3 origin = WorldMapping.ToWorld(platoon.Position, MarkerHeight);
            float half = platoon.WatchArcDegrees * 0.5f;
            float startBearing = platoon.WatchBearingDegrees - half;

            Vector3 previous = origin + WorldMapping.BearingToWorldDirection(startBearing) * visualRange;
            AddLine(origin, previous, arcColor);

            const int segments = 24;
            for (int i = 1; i <= segments; i++)
            {
                float bearing = startBearing + platoon.WatchArcDegrees * i / segments;
                Vector3 next = origin + WorldMapping.BearingToWorldDirection(bearing) * visualRange;
                AddLine(previous, next, arcColor);
                previous = next;
            }

            AddLine(origin, previous, arcColor);

            // 경계 방향 중심선은 조금 진하게 짧게 그려서 방향을 알아보기 쉽게 한다.
            Vector3 center = WorldMapping.BearingToWorldDirection(platoon.WatchBearingDegrees);
            AddLine(origin, origin + center * WatchDirectionLineLength, WithAlpha(color, 0.8f));
        }

        private void AddLine(Vector3 from, Vector3 to, Color color)
        {
            _lineIndices.Add(AddVertex(from, color));
            _lineIndices.Add(AddVertex(to, color));
        }

        private void AddFilledCircle(Vector3 center, float radius, Color color)
        {
            int centerIndex = AddVertex(center, color);
            int firstRim = _vertices.Count;
            for (int i = 0; i < CircleSegments; i++)
            {
                AddVertex(center + GetCircleOffset(i, radius), color);
            }

            for (int i = 0; i < CircleSegments; i++)
            {
                _triangleIndices.Add(centerIndex);
                _triangleIndices.Add(firstRim + i);
                _triangleIndices.Add(firstRim + (i + 1) % CircleSegments);
            }
        }

        private void AddCircleOutline(Vector3 center, float radius, Color color)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                AddLine(center + GetCircleOffset(i, radius), center + GetCircleOffset(i + 1, radius), color);
            }
        }

        private void AddFilledSquare(Vector3 center, float half, Color color)
        {
            int a = AddVertex(center + new Vector3(-half, 0f, -half), color);
            int b = AddVertex(center + new Vector3(-half, 0f, half), color);
            int c = AddVertex(center + new Vector3(half, 0f, half), color);
            int d = AddVertex(center + new Vector3(half, 0f, -half), color);
            _triangleIndices.Add(a);
            _triangleIndices.Add(b);
            _triangleIndices.Add(c);
            _triangleIndices.Add(a);
            _triangleIndices.Add(c);
            _triangleIndices.Add(d);
        }

        private void AddSquareOutline(Vector3 center, float half, Color color)
        {
            var a = center + new Vector3(-half, 0f, -half);
            var b = center + new Vector3(-half, 0f, half);
            var c = center + new Vector3(half, 0f, half);
            var d = center + new Vector3(half, 0f, -half);
            AddLine(a, b, color);
            AddLine(b, c, color);
            AddLine(c, d, color);
            AddLine(d, a, color);
        }

        private int AddVertex(Vector3 position, Color color)
        {
            _vertices.Add(position);
            _colors.Add(color);
            return _vertices.Count - 1;
        }

        private static Vector3 GetCircleOffset(int index, float radius)
        {
            float angle = index * Mathf.PI * 2f / CircleSegments;
            return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        private void OnGUI()
        {
            if (_controller == null || _controller.Simulation == null)
            {
                return;
            }

            EnsureStyles();
            Simulation simulation = _controller.Simulation;
            DrawLabels(simulation);
            DrawStatusPanel(simulation);
            DrawLogPanel(simulation);
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            _labelStyle.normal.textColor = Color.white;
            _panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = false };
            _panelStyle.normal.textColor = Color.white;
        }

        private void DrawLabels(Simulation simulation)
        {
            foreach (Platoon platoon in simulation.Platoons)
            {
                bool isFriendly = platoon.Side == _controller.PlayerSide;
                if (!isFriendly && !_controller.ShowTruth)
                {
                    continue;
                }

                string label = isFriendly
                    ? $"{platoon.Name} {GetStateText(platoon)}\n{GetCombatText(platoon)}"
                    : $"[진실] {platoon.Name} {GetCombatText(platoon)}";
                DrawWorldLabel(platoon.Position, label);
            }

            foreach (Contact contact in simulation.GetIntel(_controller.PlayerSide).Contacts)
            {
                string label = contact.Confidence == ContactConfidence.Confirmed
                    ? $"접촉 {contact.ContactNumber}"
                    : $"접촉 {contact.ContactNumber} (소실 {simulation.FormatClock(contact.LastSeenSeconds)})";
                DrawWorldLabel(contact.LastKnownPosition, label);
            }
        }

        private void DrawWorldLabel(Vec2 position, string text)
        {
            if (_camera == null)
            {
                return;
            }

            Vector3 screen = _camera.WorldToScreenPoint(WorldMapping.ToWorld(position, MarkerHeight));
            if (screen.z < 0f)
            {
                return;
            }

            var rect = new Rect(screen.x + 12f, Screen.height - screen.y - 10f, 300f, 40f);
            GUI.Label(rect, text, _labelStyle);
        }

        private void DrawStatusPanel(Simulation simulation)
        {
            Platoon selected = _controller.SelectedPlatoon;
            _textBuilder.Clear();
            _textBuilder.AppendLine($"{simulation.FormatClock(simulation.ElapsedSeconds)}   x{_controller.Speed:0}   {(_controller.IsPaused ? "■ 일시정지" : "▶ 진행")}");
            _textBuilder.AppendLine($"선택: {(selected != null ? $"{selected.Name} (사격 방침: {DisplayNames.Of(selected.FirePolicy)})" : "-")}");
            _textBuilder.AppendLine($"다음 이동 명령: {DisplayNames.Of(_controller.OrderMoveMode)}, 접촉 시 {DisplayNames.Of(_controller.OrderContactBehavior)}");
            _textBuilder.AppendLine($"실제 전황 표시: {(_controller.ShowTruth ? "켬" : "끔")}");
            _textBuilder.AppendLine(GetCursorText(simulation));
            _textBuilder.AppendLine();
            _textBuilder.AppendLine($"Space 일시정지 · 1~{_controller.SpeedStepCount} 배속 · Tab 소대 선택");
            _textBuilder.AppendLine("우클릭 이동 (Shift 경로 추가) · M 이동 방식 · C 접촉 시 행동");
            _textBuilder.AppendLine("W 마우스 방향 경계 · H 정지 · F 사격 방침 · T 실제 전황");

            GUI.Box(new Rect(10f, 10f, 430f, 178f), _textBuilder.ToString(), _panelStyle);
        }

        private void DrawLogPanel(Simulation simulation)
        {
            _logBuffer.Clear();
            IReadOnlyList<SimEvent> events = simulation.Events;
            for (int i = events.Count - 1; i >= 0 && _logBuffer.Count < _logLineCount; i--)
            {
                SimEvent simEvent = events[i];
                bool visible = _controller.ShowTruth || simEvent.Side == _controller.PlayerSide;
                if (!visible)
                {
                    continue;
                }

                string prefix = simEvent.Side == _controller.PlayerSide ? string.Empty : "[적] ";
                _logBuffer.Add($"{simulation.FormatClock(simEvent.ElapsedSeconds)} {prefix}{simEvent.Text}");
            }

            _textBuilder.Clear();
            for (int i = _logBuffer.Count - 1; i >= 0; i--)
            {
                _textBuilder.AppendLine(_logBuffer[i]);
            }

            float height = 20f + _logLineCount * 18f;
            GUI.Box(new Rect(10f, Screen.height - height - 10f, 560f, height), _textBuilder.ToString(), _panelStyle);
        }

        private static string GetStateText(Platoon platoon)
        {
            if (platoon.IsDestroyed)
            {
                return "(전투 불능)";
            }

            if (platoon.IsBroken)
            {
                return "(사기 붕괴)";
            }

            string state;
            if (platoon.IsMoving)
            {
                state = $"{DisplayNames.Of(platoon.CurrentMoveMode)} 이동";
            }
            else
            {
                state = platoon.IsWatching ? $"경계 {platoon.WatchBearingDegrees:000}" : "정지";
            }

            if (platoon.IsPinned)
            {
                state += ", 제압됨";
            }

            if (platoon.IsFiring)
            {
                state += ", 사격 중";
            }

            return $"({state})";
        }

        /// <summary>커서 아래 지점의 격자·고도·지형. 지형이 왜 시야를 막는지 확인하는 용도.</summary>
        private string GetCursorText(Simulation simulation)
        {
            if (!_controller.HasCursorMapPosition)
            {
                return "커서: -";
            }

            Vec2 cursor = _controller.CursorMapPosition;
            MapData map = simulation.Map;
            return $"커서: {map.GetGridReference(cursor)}  고도 {map.GetHeightAt(cursor):0}m  {GetTerrainText(map.GetTerrainAt(cursor))}";
        }

        private static string GetTerrainText(TerrainType terrain)
        {
            switch (terrain)
            {
                case TerrainType.Forest:
                    return "숲";
                case TerrainType.Road:
                    return "도로";
                default:
                    return "개활지";
            }
        }

        private static string GetCombatText(Platoon platoon)
        {
            return $"{platoon.Effective}/{platoon.InitialStrength}명 사기 {platoon.Morale:0} 제압 {platoon.Suppression:0}";
        }
    }
}
