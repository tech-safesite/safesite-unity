using System.Collections.Generic;
using Modules.SafeSiteApiService.External;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>
    /// Builds the testbench canvas: header, tabbed panels, and the log console docked at the bottom.
    /// Screen-space overlay in the Editor; on a headset (or via the header button) it becomes a
    /// world-space canvas in front of the XR camera, usable with an XRI ray interactor.
    /// Needs an <see cref="ISafeSiteApiService"/> component in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestbenchRoot : MonoBehaviour
    {
        private const string LogPrefix = "[SafeSiteApiTestbench]";

        [SerializeField] private Vector2 referenceResolution = new Vector2(1600, 1000);
        [SerializeField] private bool worldSpaceWhenXrActive = true;
        [SerializeField] private float worldSpaceDistance = 1.5f;
        [SerializeField] private float worldSpaceWidthMeters = 1.3f;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private HeaderBar _header;
        private readonly List<(Button tab, GameObject page)> _tabs = new List<(Button, GameObject)>();
        private bool _worldSpace;

        private void Start()
        {
            EnsureEventSystem();
            EnsureCamera();
            BuildCanvas();

            var service = SafeSiteApiServiceLocator.FindInScene();
            if (service == null)
            {
                Debug.LogError($"{LogPrefix} No ISafeSiteApiService in the scene. Add the SafeSiteApiService component to a scene object.");
                Ui.Label(_canvas.transform, "No SafeSiteApiService in the scene.", 28, Ui.Bad);
                return;
            }

            BuildLayout(new TestbenchState(service));

            if (worldSpaceWhenXrActive && UnityEngine.XR.XRSettings.isDeviceActive)
                SetWorldSpace(true);
        }

        private void BuildCanvas()
        {
            var go = new GameObject("TestbenchCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _scaler = go.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = referenceResolution;
            _scaler.matchWidthOrHeight = 0.5f;
        }

        private void BuildLayout(TestbenchState state)
        {
            var root = Ui.Column(_canvas.transform, 6, 8, "Root");
            Ui.Stretch(root);
            Ui.Fill(root, Ui.Background);

            // Header
            var headerRect = Ui.Column(root, 4, 6, "Header");
            Ui.Fill(headerRect, Ui.Panel);
            _header = headerRect.gameObject.AddComponent<HeaderBar>();
            _header.ToggleWorldSpace = () => SetWorldSpace(!_worldSpace);
            _header.Initialize(state, headerRect);

            // Tabs
            var tabBar = Ui.Row(root, 4, "Tabs");
            var pages = Ui.Rect("Pages", root);
            Ui.Layout(pages, minHeight: 200, flexibleHeight: 65);

            AddPage<AdminPanel>(state, tabBar, pages);
            AddPage<EnrollPanel>(state, tabBar, pages);
            AddPage<ConfigPanel>(state, tabBar, pages);
            AddPage<ContentPanel>(state, tabBar, pages);
            AddPage<HeartbeatPanel>(state, tabBar, pages);
            AddPage<SessionsPanel>(state, tabBar, pages);
            AddPage<SmokeTestPanel>(state, tabBar, pages);
            SelectTab(0);

            // Log console, docked at the bottom (~35%).
            var logRect = Ui.Column(root, 4, 6, "LogConsole");
            Ui.Fill(logRect, Ui.Panel);
            Ui.Layout(logRect, minHeight: 160, flexibleHeight: 35);
            logRect.gameObject.AddComponent<LogConsolePanel>().Initialize(state, logRect);
        }

        private void AddPage<T>(TestbenchState state, Transform tabBar, Transform pages) where T : TestbenchPanel
        {
            var content = Ui.ScrollView(pages, out var scroll, typeof(T).Name);
            Ui.Stretch((RectTransform)scroll.transform);
            var panel = scroll.gameObject.AddComponent<T>();
            panel.Initialize(state, content);

            var index = _tabs.Count;
            var tab = Ui.Button(tabBar, panel.Title, () => SelectTab(index));
            _tabs.Add((tab, scroll.gameObject));
        }

        private void SelectTab(int index)
        {
            // Hidden pages stay active so their timers (e.g. auto heartbeat) keep running.
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (!_tabs[i].page.TryGetComponent(out CanvasGroup group))
                    group = _tabs[i].page.AddComponent<CanvasGroup>();
                group.alpha = i == index ? 1f : 0f;
                group.interactable = i == index;
                group.blocksRaycasts = i == index;
                _tabs[i].tab.GetComponent<Image>().color = i == index ? Ui.Accent : Ui.Field;
            }
        }

        private void SetWorldSpace(bool worldSpace)
        {
            _worldSpace = worldSpace;
            var rect = (RectTransform)_canvas.transform;
            if (worldSpace)
            {
                var camera = Camera.main;
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = camera;
                _scaler.enabled = false;
                rect.sizeDelta = referenceResolution;
                rect.localScale = Vector3.one * (worldSpaceWidthMeters / referenceResolution.x);

                if (camera != null)
                {
                    var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                    if (forward == Vector3.zero)
                        forward = Vector3.forward;
                    rect.position = camera.transform.position + forward * worldSpaceDistance;
                    rect.rotation = Quaternion.LookRotation(forward, Vector3.up);
                }

                if (!_canvas.TryGetComponent<TrackedDeviceGraphicRaycaster>(out _))
                    _canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            else
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _scaler.enabled = true;
                rect.localScale = Vector3.one;
            }

            _header.SetWorldSpaceLabel(worldSpace);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;

            // XRUIInputModule drives both the mouse (Editor) and XR ray interactors (headset).
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            DontDestroyOnLoad(go);
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null)
                return;

            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            go.transform.position = new Vector3(0, 1.6f, 0);
            go.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            go.GetComponent<Camera>().backgroundColor = Color.black;
        }
    }
}
