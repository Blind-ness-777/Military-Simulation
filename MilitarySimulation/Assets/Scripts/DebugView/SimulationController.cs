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
        private const int MaxTicksPerFrame = 50;
        private const int MaxSpeedSteps = 9;
        private const float DefaultWatchArcDegrees = 90f;

        [SerializeField] private int _seed = 12345;

        /// <summary>
        /// 배속 단계(실제 1초당 게임 시간 초). 숫자키 1부터 순서대로 대응하며 최대 9단계.
        /// 배속은 시간 흐름만 바꾸고 판정 결과에는 영향이 없다(틱 단위로 진행하므로).
        /// </summary>
        [SerializeField] private float[] _speedSteps = { 2f, 5f, 10f, 20f };
        [SerializeField] private int _startSpeedIndex = 1;

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
        public float Speed => _speedSteps[_speedIndex];
        public int SpeedStepCount => Mathf.Min(_speedSteps.Length, MaxSpeedSteps);
        public MoveMode OrderMoveMode { get; private set; } = MoveMode.Normal;
        public ContactBehavior OrderContactBehavior { get; private set; } = ContactBehavior.TakeCover;

        /// <summary>true면 실제 전황(적 위치)까지 표시한다. 디버그 전용.</summary>
        public bool ShowTruth { get; private set; }

        public Platoon SelectedPlatoon => _friendlyPlatoons.Count > 0 ? _friendlyPlatoons[_selectedIndex] : null;

        /// <summary>마우스 커서가 지도 위에 있는지. HUD의 커서 정보(고도·지형) 표시에 쓴다.</summary>
        public bool HasCursorMapPosition { get; private set; }
        public Vec2 CursorMapPosition { get; private set; }

        private void Awake()
        {
            _mapView = GetComponent<DebugMapView>();
            _unitView = GetComponent<DebugUnitView>();

            // Inspector에서 배열을 비우면 배속 접근이 바로 터지므로, 여기서 한 번만 바로잡는다.
            if (_speedSteps == null || _speedSteps.Length == 0)
            {
                Debug.LogError("[SimulationController] 배속 단계가 비어 있어 1배속 하나로 대체합니다.");
                _speedSteps = new[] { 1f };
            }

            _speedIndex = Mathf.Clamp(_startSpeedIndex, 0, SpeedStepCount - 1);
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
            UpdateCursor();
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

            // Key.Digit1 ~ Key.Digit9는 열거형에서 연속된 값이다.
            for (int i = 0; i < SpeedStepCount; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    _speedIndex = i;
                }
            }

            if (keyboard.tabKey.wasPressedThisFrame && _friendlyPlatoons.Count > 0)
            {
                _selectedIndex = (_selectedIndex + 1) % _friendlyPlatoons.Count;
            }

            if (keyboard.mKey.wasPressedThisFrame)
            {
                OrderMoveMode = (MoveMode)(((int)OrderMoveMode + 1) % 3);
            }

            if (keyboard.cKey.wasPressedThisFrame)
            {
                OrderContactBehavior = (ContactBehavior)(((int)OrderContactBehavior + 1) % 3);
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

            if (keyboard.fKey.wasPressedThisFrame)
            {
                var nextPolicy = (FirePolicy)(((int)selected.FirePolicy + 1) % 3);
                _simulation.OrderFirePolicy(selected, nextPolicy);
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
            _simulation.OrderMove(selected, new[] { destination }, OrderMoveMode, append, OrderContactBehavior);
        }

        private void UpdateCursor()
        {
            HasCursorMapPosition = TryGetMouseMapPosition(out Vec2 position);
            CursorMapPosition = position;
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
