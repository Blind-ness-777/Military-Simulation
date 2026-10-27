using System.Collections.Generic;
using MilitarySimulation.Engine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MilitarySimulation.DebugView
{
    /// <summary>
    /// 디버그용 컨트롤러. 시뮬레이션 틱 진행, 일시정지·배속, 명령 입력을 담당한다.
    /// 명령은 지도 마우스 조작과 DebugUnitView의 명령 패널 버튼에서 들어온다.
    /// 패널 버튼은 IMGUI(OnGUI)에서 눌리므로, 명령 처리 메서드를 public으로 열어 두고 뷰가 호출한다.
    /// </summary>
    [RequireComponent(typeof(DebugMapView))]
    [RequireComponent(typeof(DebugUnitView))]
    public class SimulationController : MonoBehaviour
    {
        public enum ScenarioKind
        {
            /// <summary>엔진 검증용 기본 시나리오(적은 정해진 명령만 따름).</summary>
            Test,

            /// <summary>적 지휘관 AI 검증용 방어 시나리오.</summary>
            Defense
        }

        private const int MaxTicksPerFrame = 50;
        private const int MaxSpeedSteps = 9;
        private const float DefaultWatchArcDegrees = 90f;

        /// <summary>좌클릭으로 소대를 고를 때 허용하는 화면상 거리(픽셀).</summary>
        private const float SelectPickRadiusPixels = 24f;

        [SerializeField] private int _seed = 12345;

        /// <summary>실행할 시나리오. 방어 시나리오는 적 본부·예비대와 적 지휘관 AI가 있다.</summary>
        [SerializeField] private ScenarioKind _scenario = ScenarioKind.Test;

        /// <summary>
        /// 배속 단계(실제 1초당 게임 시간 초). 숫자키 1부터 순서대로 대응하며 최대 9단계.
        /// 배속은 시간 흐름만 바꾸고 판정 결과에는 영향이 없다(틱 단위로 진행하므로).
        /// </summary>
        [SerializeField] private float[] _speedSteps = { 2f, 5f, 10f, 20f };
        [SerializeField] private int _startSpeedIndex = 1;

        // 단축키. 키는 여기 한곳에서만 정한다(패널·도움말 표시도 이 값을 읽는다).
        // 배속은 숫자키 1~9 고정. 플레이어가 게임 안에서 바꾸는 키 설정은 설정 메뉴가 생길 때 입력 액션으로 옮긴다.
        [SerializeField] private Key _pauseKey = Key.Space;
        [SerializeField] private Key _nextPlatoonKey = Key.Tab;
        [SerializeField] private Key _watchKey = Key.F;
        [SerializeField] private Key _holdKey = Key.H;
        [SerializeField] private Key _truthKey = Key.T;
        [SerializeField] private Key _legendKey = Key.L;
        [SerializeField] private Key _cancelKey = Key.Escape;

        private readonly List<Platoon> _friendlyPlatoons = new List<Platoon>();
        private Simulation _simulation;
        private DebugMapView _mapView;
        private DebugUnitView _unitView;
        private Camera _camera;
        private float _tickAccumulator;
        private int _speedIndex;
        private int _selectedIndex;
        private bool _missionEndHandled;

        public Simulation Simulation => _simulation;
        public Side PlayerSide => Side.Blue;
        public IReadOnlyList<Platoon> FriendlyPlatoons => _friendlyPlatoons;
        public bool IsPaused { get; private set; } = true;
        public float Speed => _speedSteps[_speedIndex];
        public int SpeedStepCount => Mathf.Min(_speedSteps.Length, MaxSpeedSteps);

        /// <summary>다음 이동 명령에 붙는 이동 방식. 이미 내린 명령에는 영향이 없다.</summary>
        public MoveMode OrderMoveMode { get; private set; } = MoveMode.Normal;

        /// <summary>다음 이동 명령에 붙는 접촉 시 행동. 이미 내린 명령에는 영향이 없다.</summary>
        public ContactBehavior OrderContactBehavior { get; private set; } = ContactBehavior.TakeCover;

        /// <summary>true면 실제 전황(적 위치)까지 표시한다. 디버그 전용.</summary>
        public bool ShowTruth { get; private set; }

        /// <summary>화면 기호 설명(범례) 표시 여부.</summary>
        public bool ShowLegend { get; private set; } = true;

        public Key PauseKey => _pauseKey;
        public Key NextPlatoonKey => _nextPlatoonKey;
        public Key WatchKey => _watchKey;
        public Key HoldKey => _holdKey;
        public Key TruthKey => _truthKey;
        public Key LegendKey => _legendKey;
        public Key CancelKey => _cancelKey;

        /// <summary>경계 버튼을 누른 뒤, 지도에서 방향을 찍기를 기다리는 중인지.</summary>
        public bool IsTargetingWatch { get; private set; }

        /// <summary>
        /// 본부가 전멸해 관전 모드인지. 명령은 막히고, 실제 전황이 보이고, 최고 배속이 된다.
        /// 남은 소대는 마지막 명령과 자체 판단 모드로 움직인다.
        /// </summary>
        public bool IsSpectating { get; private set; }

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
            _simulation = _scenario == ScenarioKind.Defense ? DefenseScenario.Create(_seed) : TestScenario.Create(_seed);

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
            CheckSpectateMode();
            CheckMissionEnd();
            _unitView.Refresh();
        }

        /// <summary>
        /// 플레이어(본부)가 이 소대를 실시간으로 모르면 마지막 보고를 돌려준다.
        /// 실시간 보고 중이거나 관전 모드면 false(실제 상태를 그대로 그린다).
        /// </summary>
        public bool TryGetStaleReport(Platoon platoon, out FriendlyReport report)
        {
            report = null;
            if (IsSpectating || _simulation.IsReportingLive(platoon))
            {
                return false;
            }

            return _simulation.TryGetLastReport(platoon, out report);
        }

        /// <summary>플레이어 화면에 그리는 소대 위치. 보고가 끊긴 소대는 마지막 보고 위치다.</summary>
        public Vec2 GetKnownPosition(Platoon platoon)
        {
            return TryGetStaleReport(platoon, out FriendlyReport report) ? report.Snapshot.Position : platoon.Position;
        }

        private void CheckSpectateMode()
        {
            if (IsSpectating || !_simulation.IsHeadquartersLost(PlayerSide))
            {
                return;
            }

            IsSpectating = true;
            IsTargetingWatch = false;
            ShowTruth = true;
            _speedIndex = SpeedStepCount - 1;
        }

        /// <summary>판이 끝나면 실제 전황을 한 번 켜서, 내가 본 것과 실제를 비교할 수 있게 한다(AAR 전 임시).</summary>
        private void CheckMissionEnd()
        {
            if (_missionEndHandled || !_simulation.IsFinished)
            {
                return;
            }

            _missionEndHandled = true;
            IsTargetingWatch = false;
            ShowTruth = true;
        }

        public void SelectPlatoon(Platoon platoon)
        {
            int index = _friendlyPlatoons.IndexOf(platoon);
            if (index < 0)
            {
                return;
            }

            _selectedIndex = index;
            IsTargetingWatch = false;
        }

        public void SetOrderMoveMode(MoveMode moveMode)
        {
            OrderMoveMode = moveMode;
        }

        public void SetOrderContactBehavior(ContactBehavior contactBehavior)
        {
            OrderContactBehavior = contactBehavior;
        }

        /// <summary>사격 방침은 다음 이동 명령에 붙지 않고, 선택한 소대에 바로 발신한다(통신을 거쳐 도착).</summary>
        public void SetSelectedFirePolicy(FirePolicy firePolicy)
        {
            Platoon selected = SelectedPlatoon;
            if (IsSpectating || selected == null || selected.FirePolicy == firePolicy)
            {
                return;
            }

            _simulation.OrderFirePolicy(selected, firePolicy);
        }

        public void BeginWatchTargeting()
        {
            if (!IsSpectating && SelectedPlatoon != null)
            {
                IsTargetingWatch = true;
            }
        }

        public void CancelWatchTargeting()
        {
            IsTargetingWatch = false;
        }

        /// <summary>
        /// 선택한 소대가 지금 마우스 커서 방향을 경계한다(클릭 단계 없음).
        /// 방향은 플레이어가 보는 소대 위치 기준(보고가 끊긴 소대는 마지막 보고 위치).
        /// </summary>
        public void WatchTowardCursor()
        {
            Platoon selected = SelectedPlatoon;
            if (IsSpectating || selected == null || !HasCursorMapPosition)
            {
                return;
            }

            Vec2 origin = GetKnownPosition(selected);
            if (Vec2.Distance(origin, CursorMapPosition) < 1f)
            {
                return;
            }

            IsTargetingWatch = false;
            _simulation.OrderWatch(selected, Bearing.Between(origin, CursorMapPosition), DefaultWatchArcDegrees);
        }

        public void HoldSelected()
        {
            Platoon selected = SelectedPlatoon;
            if (!IsSpectating && selected != null)
            {
                IsTargetingWatch = false;
                _simulation.OrderHold(selected);
            }
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard[_pauseKey].wasPressedThisFrame)
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

            if (keyboard[_nextPlatoonKey].wasPressedThisFrame && _friendlyPlatoons.Count > 0)
            {
                SelectPlatoon(_friendlyPlatoons[(_selectedIndex + 1) % _friendlyPlatoons.Count]);
            }

            if (keyboard[_truthKey].wasPressedThisFrame)
            {
                ShowTruth = !ShowTruth;
            }

            if (keyboard[_legendKey].wasPressedThisFrame)
            {
                ShowLegend = !ShowLegend;
            }

            if (keyboard[_cancelKey].wasPressedThisFrame)
            {
                CancelWatchTargeting();
            }

            if (keyboard[_holdKey].wasPressedThisFrame)
            {
                HoldSelected();
            }

            if (keyboard[_watchKey].wasPressedThisFrame)
            {
                WatchTowardCursor();
            }
        }

        private void HandleMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            bool leftPressed = mouse.leftButton.wasPressedThisFrame;
            bool rightPressed = mouse.rightButton.wasPressedThisFrame;
            if (!leftPressed && !rightPressed)
            {
                return;
            }

            // 패널 위의 클릭은 IMGUI 버튼이 처리한다. 지도 명령으로 새지 않게 막는다.
            Vector2 screenPosition = mouse.position.ReadValue();
            if (_unitView.IsPointerOverUi(screenPosition))
            {
                return;
            }

            if (IsTargetingWatch)
            {
                HandleWatchTargetingClick(leftPressed);
                return;
            }

            if (leftPressed)
            {
                TrySelectPlatoonAt(screenPosition);
                return;
            }

            Platoon selected = SelectedPlatoon;
            if (IsSpectating || selected == null || !HasCursorMapPosition)
            {
                return;
            }

            bool append = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            _simulation.OrderMove(selected, new[] { CursorMapPosition }, OrderMoveMode, append, OrderContactBehavior);
        }

        /// <summary>경계 방향 지정 중: 좌클릭은 그 방향으로 경계, 우클릭은 취소.</summary>
        private void HandleWatchTargetingClick(bool leftPressed)
        {
            Platoon selected = SelectedPlatoon;
            IsTargetingWatch = false;
            if (!leftPressed || selected == null || !HasCursorMapPosition)
            {
                return;
            }

            // 플레이어가 보는 위치 기준으로 방향을 잡는다(보고가 끊긴 소대는 마지막 보고 위치).
            float bearing = Bearing.Between(GetKnownPosition(selected), CursorMapPosition);
            _simulation.OrderWatch(selected, bearing, DefaultWatchArcDegrees);
        }

        private void TrySelectPlatoonAt(Vector2 screenPosition)
        {
            if (_camera == null)
            {
                return;
            }

            Platoon nearest = null;
            float nearestDistance = SelectPickRadiusPixels;
            foreach (Platoon platoon in _friendlyPlatoons)
            {
                Vector3 screen = _camera.WorldToScreenPoint(WorldMapping.ToWorld(GetKnownPosition(platoon)));
                if (screen.z < 0f)
                {
                    continue;
                }

                float distance = Vector2.Distance(screenPosition, new Vector2(screen.x, screen.y));
                if (distance < nearestDistance)
                {
                    nearest = platoon;
                    nearestDistance = distance;
                }
            }

            if (nearest != null)
            {
                SelectPlatoon(nearest);
            }
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
