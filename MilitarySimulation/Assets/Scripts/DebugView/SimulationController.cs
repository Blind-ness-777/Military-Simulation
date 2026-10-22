using System.Collections.Generic;
using MilitarySimulation.Engine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MilitarySimulation.DebugView
{
    /// <summary>
    /// 디버그용 컨트롤러. 시뮬레이션 틱 진행, 일시정지·배속, 임시 명령 입력을 담당한다.
    /// 명령 입력은 엔진 검증용 임시 조작이며, 실제 명령 UI(드래그 경로, 경계 부채꼴)는 이후 단계에서 만든다.
    /// </summary>
    [RequireComponent(typeof(DebugMapView))]
    [RequireComponent(typeof(DebugUnitView))]
    public class SimulationController : MonoBehaviour
    {
        private static readonly float[] SpeedSteps = { 1f, 3f, 10f };
        private const int MaxTicksPerFrame = 50;
        private const float DefaultWatchArcDegrees = 90f;

        [SerializeField] private int _seed = 12345;

        private readonly List<Platoon> _friendlyPlatoons = new List<Platoon>();
        private Simulation _simulation;
        private DebugMapView _mapView;
        private DebugUnitView _unitView;
        private Camera _camera;
        private float _tickAccumulator;
        private int _speedIndex;
        private int _selectedIndex;

        public Simulation Simulation => _simulation;
        public Side PlayerSide => Side.Blue;
        public bool IsPaused { get; private set; } = true;
        public float Speed => SpeedSteps[_speedIndex];
        public MoveMode OrderMoveMode { get; private set; } = MoveMode.Normal;

        /// <summary>true면 실제 전황(적 위치)까지 표시한다. 디버그 전용.</summary>
        public bool ShowTruth { get; private set; }

        public Platoon SelectedPlatoon => _friendlyPlatoons.Count > 0 ? _friendlyPlatoons[_selectedIndex] : null;

        private void Awake()
        {
            _mapView = GetComponent<DebugMapView>();
            _unitView = GetComponent<DebugUnitView>();
            _simulation = TestScenario.Create(_seed);

            foreach (Platoon platoon in _simulation.Platoons)
            {
                if (platoon.Side == PlayerSide)
                {
                    _friendlyPlatoons.Add(platoon);
                }
            }
        }

        private void Start()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogError("[SimulationController] MainCamera 태그가 붙은 카메라가 없습니다.");
            }

            _mapView.Initialize(_simulation.Map, _camera);
            _unitView.Initialize(this, _camera);
        }

        private void Update()
        {
            HandleKeyboard();
            HandleMouse();
            AdvanceSimulation();
            _unitView.Refresh();
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                IsPaused = !IsPaused;
            }

            if (keyboard.digit1Key.wasPressedThisFrame) _speedIndex = 0;
            if (keyboard.digit2Key.wasPressedThisFrame) _speedIndex = 1;
            if (keyboard.digit3Key.wasPressedThisFrame) _speedIndex = 2;

            if (keyboard.tabKey.wasPressedThisFrame && _friendlyPlatoons.Count > 0)
            {
                _selectedIndex = (_selectedIndex + 1) % _friendlyPlatoons.Count;
            }

            if (keyboard.mKey.wasPressedThisFrame)
            {
                OrderMoveMode = (MoveMode)(((int)OrderMoveMode + 1) % 3);
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                ShowTruth = !ShowTruth;
            }

            Platoon selected = SelectedPlatoon;
            if (selected == null)
            {
                return;
            }

            if (keyboard.hKey.wasPressedThisFrame)
            {
                _simulation.OrderHold(selected);
            }

            if (keyboard.wKey.wasPressedThisFrame && TryGetMouseMapPosition(out Vec2 lookAt))
            {
                float bearing = Bearing.Between(selected.Position, lookAt);
                _simulation.OrderWatch(selected, bearing, DefaultWatchArcDegrees);
            }
        }

        private void HandleMouse()
        {
            Mouse mouse = Mouse.current;
            Platoon selected = SelectedPlatoon;
            if (mouse == null || selected == null || !mouse.rightButton.wasPressedThisFrame)
            {
                return;
            }

            if (!TryGetMouseMapPosition(out Vec2 destination))
            {
                return;
            }

            bool append = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            _simulation.OrderMove(selected, new[] { destination }, OrderMoveMode, append);
        }

        private void AdvanceSimulation()
        {
            if (IsPaused)
            {
                return;
            }

            _tickAccumulator += Time.deltaTime * Speed;
            int ticks = 0;
            while (_tickAccumulator >= Simulation.TickSeconds && ticks < MaxTicksPerFrame)
            {
                _simulation.Tick();
                _tickAccumulator -= Simulation.TickSeconds;
                ticks++;
            }

            // 프레임이 크게 밀리면 따라잡으려 하지 않고 버린다(한 프레임에 틱이 몰리는 것 방지).
            if (ticks == MaxTicksPerFrame)
            {
                _tickAccumulator = 0f;
            }
        }

        private bool TryGetMouseMapPosition(out Vec2 position)
        {
            position = Vec2.Zero;
            Mouse mouse = Mouse.current;
            if (mouse == null || _camera == null)
            {
                return false;
            }

            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter))
            {
                return false;
            }

            position = WorldMapping.ToEngine(ray.GetPoint(enter));
            return _simulation.Map.IsInside(position);
        }
    }
}
