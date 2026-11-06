using Modules.ScenarioLauncher.External;
using SafeSite.Core.UI;
using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace SafeSite.Core.Scenarios
{
    /// <summary>
    /// Plays scenarios around the main menu: fades, player reset and hiding the menu. Built-in
    /// scenarios load by Addressables address; downloaded ones through the launcher. Also the
    /// <see cref="IScenarioHost"/> the in-scenario menu uses to restart, leave or switch.
    /// </summary>
    public class TrainingScenarioLoader : MonoBehaviour, IScenarioHost
    {
        public Transform playerRig;

        public static TrainingScenarioLoader Instance { get; private set; }

        public GameObject[] menuObjectsToDisable;

        private AsyncOperationHandle<SceneInstance> sceneInstance;
        private bool busy;
        private string currentScenarioId;
        private string currentBuiltInAddress;

        private InteractionStateManager.InteractionMode? modeBeforeOverlay;

        public bool IsPlaying => currentScenarioId != null || currentBuiltInAddress != null;
        public bool IsBusy => busy;
        public string CurrentScenarioId => currentScenarioId;
        public string CurrentBuiltInAddress => currentBuiltInAddress;

        public event Action Changed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        private void Start()
        {
            ResetPlayerPositionAndRotation();
        }

        // ---- Starting

        public void LoadScenario(string sceneAddress)
        {
            if (busy)
                return;
            StartCoroutine(LoadScenarioRoutine(sceneAddress));
        }

        public IEnumerator LoadScenarioRoutine(string sceneAddress)
        {
            SetBusy(true);
            ScreenFadeController.Instance.FadeToBlack(1);
            yield return new WaitForSeconds(1.1f);

            yield return LoadBuiltInWhileBlack(sceneAddress);

            ScreenFadeController.Instance.FadeOutOfBlack(2);
            SetBusy(false);
        }

        /// <summary>
        /// Plays a scenario downloaded from SafeSite (the launcher menu's Start). The download runs
        /// with the menu still showing its progress; the scene loads behind the fade.
        /// </summary>
        public void PlayDownloaded(string scenarioId)
        {
            var launcher = ScenarioLauncherServiceLocator.FindInScene();
            if (launcher != null && !busy)
                StartCoroutine(PlayDownloadedRoutine(launcher, scenarioId));
        }

        private IEnumerator PlayDownloadedRoutine(IScenarioLauncherService launcher, string scenarioId)
        {
            SetBusy(true);
            yield return WaitFor(launcher.DownloadAsync(scenarioId));
            if (!IsInstalled(launcher, scenarioId))
            {
                SetBusy(false);
                yield break; // the menu shows what went wrong
            }

            ScreenFadeController.Instance.FadeToBlack(1);
            yield return new WaitForSeconds(1.1f);

            yield return PlayDownloadedWhileBlack(launcher, scenarioId);

            ScreenFadeController.Instance.FadeOutOfBlack(2);
            SetBusy(false);
        }

        /// <summary>Loads a built-in scene; the screen is black. On failure the menu comes back.</summary>
        private IEnumerator LoadBuiltInWhileBlack(string sceneAddress)
        {
            SetMenuVisible(false);
            sceneInstance = Addressables.LoadSceneAsync(sceneAddress, LoadSceneMode.Additive);
            yield return sceneInstance;

            if (sceneInstance.Status == AsyncOperationStatus.Succeeded)
            {
                Debug.Log("Scene Successfully Loaded");
                currentBuiltInAddress = sceneAddress;
                Raise();
            }
            else
            {
                Debug.Log("Scene Loading Failed");
                Addressables.Release(sceneInstance);
                sceneInstance = default;
                ResetPlayerPositionAndRotation();
                SetMenuVisible(true);
                SetInteractionMode(InteractionStateManager.InteractionMode.UIAndTeleport);
            }
        }

        /// <summary>Loads an installed downloaded scenario; the screen is black. On failure the menu comes back.</summary>
        private IEnumerator PlayDownloadedWhileBlack(IScenarioLauncherService launcher, string scenarioId)
        {
            SetMenuVisible(false);
            yield return WaitFor(launcher.PlayAsync(scenarioId));
            if (launcher.State == LauncherState.Playing)
            {
                currentScenarioId = scenarioId;
                Raise();
            }
            else
            {
                ResetPlayerPositionAndRotation();
                SetMenuVisible(true);
                SetInteractionMode(InteractionStateManager.InteractionMode.UIAndTeleport);
            }
        }

        // ---- Leaving

        /// <summary>Ends the playing scenario, built-in or downloaded, and returns to the menu.</summary>
        public void UnloadScenario()
        {
            if (busy || !HasLoadedScene())
                return;
            StartCoroutine(UnloadSceneRoutine());
        }

        public void BackToMenu()
        {
            UnloadScenario();
        }

        private IEnumerator UnloadSceneRoutine()
        {
            SetBusy(true);
            ScreenFadeController.Instance.FadeToBlack(2);
            yield return new WaitForSeconds(2);

            yield return UnloadWhileBlack();
            SetMenuVisible(true);

            yield return new WaitForSeconds(1);
            ScreenFadeController.Instance.FadeOutOfBlack(2);
            Debug.Log("SceneUnloaded");

            SetInteractionMode(InteractionStateManager.InteractionMode.UIAndTeleport);
            SetBusy(false);
        }

        /// <summary>Unloads whatever scenario is loaded and puts the player back at the start; the screen is black.</summary>
        private IEnumerator UnloadWhileBlack()
        {
            if (sceneInstance.IsValid())
            {
                var unloadOperation = Addressables.UnloadSceneAsync(sceneInstance);
                yield return unloadOperation;
                sceneInstance = default;
            }
            else
            {
                var launcher = ScenarioLauncherServiceLocator.FindInScene();
                if (launcher != null && launcher.State == LauncherState.Playing)
                    yield return WaitFor(launcher.StopAsync());
            }

            currentScenarioId = null;
            currentBuiltInAddress = null;
            ResetPlayerPositionAndRotation();
            Raise();
        }

        private bool HasLoadedScene()
        {
            if (sceneInstance.IsValid())
                return true;
            var launcher = ScenarioLauncherServiceLocator.FindInScene();
            return launcher != null && launcher.State == LauncherState.Playing;
        }

        // ---- IScenarioHost: the in-scenario menu

        public void ExitScenario() => UnloadScenario();

        public void RestartScenario()
        {
            if (currentBuiltInAddress != null)
                SwitchToBuiltIn(currentBuiltInAddress);
            else if (currentScenarioId != null)
                SwitchToDownloaded(currentScenarioId);
        }

        public void SwitchToBuiltIn(string address)
        {
            if (busy || string.IsNullOrEmpty(address))
                return;
            if (!HasLoadedScene())
            {
                LoadScenario(address);
                return;
            }
            StartCoroutine(SwitchRoutine(() => LoadBuiltInWhileBlack(address)));
        }

        public void SwitchToDownloaded(string scenarioId)
        {
            var launcher = ScenarioLauncherServiceLocator.FindInScene();
            if (busy || launcher == null || string.IsNullOrEmpty(scenarioId))
                return;
            if (!HasLoadedScene())
            {
                PlayDownloaded(scenarioId);
                return;
            }
            StartCoroutine(SwitchRoutine(() => SwitchToDownloadedWhileBlack(launcher, scenarioId)));
        }

        /// <summary>Fades out, ends the playing scenario, runs <paramref name="load"/> behind the fade and fades back in.</summary>
        private IEnumerator SwitchRoutine(Func<IEnumerator> load)
        {
            SetBusy(true);
            ScreenFadeController.Instance.FadeToBlack(1);
            yield return new WaitForSeconds(1.1f);

            yield return UnloadWhileBlack();
            // The next scenario starts as it would from the menu.
            SetInteractionMode(InteractionStateManager.InteractionMode.UIAndTeleport);
            yield return load();

            ScreenFadeController.Instance.FadeOutOfBlack(2);
            SetBusy(false);
        }

        private IEnumerator SwitchToDownloadedWhileBlack(IScenarioLauncherService launcher, string scenarioId)
        {
            if (IsInstalled(launcher, scenarioId))
            {
                yield return PlayDownloadedWhileBlack(launcher, scenarioId);
                yield break;
            }

            // Needs a download first: back to the menu, which shows its progress, then play from there.
            SetMenuVisible(true);
            ScreenFadeController.Instance.FadeOutOfBlack(2);
            yield return WaitFor(launcher.DownloadAsync(scenarioId));
            if (!IsInstalled(launcher, scenarioId))
                yield break; // the menu shows what went wrong; SwitchRoutine fades in (already in)

            ScreenFadeController.Instance.FadeToBlack(1);
            yield return new WaitForSeconds(1.1f);
            yield return PlayDownloadedWhileBlack(launcher, scenarioId);
        }

        public void SetOverlayOpen(bool open)
        {
            var interaction = FindFirstObjectByType<InteractionStateManager>();
            if (interaction == null)
                return;

            if (open)
            {
                if (modeBeforeOverlay == null)
                    modeBeforeOverlay = interaction.CurrentMode;
                interaction.SetMode(InteractionStateManager.InteractionMode.UIAndTeleport);
            }
            else if (modeBeforeOverlay != null)
            {
                interaction.SetMode(modeBeforeOverlay.Value);
                modeBeforeOverlay = null;
            }
        }

        // ---- Helpers

        private static bool IsInstalled(IScenarioLauncherService launcher, string scenarioId) =>
            System.Linq.Enumerable.Any(launcher.Scenarios, s => s.Id == scenarioId && s.IsInstalled);

        private static IEnumerator WaitFor(Task task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
        }

        private void SetMenuVisible(bool visible)
        {
            if (menuObjectsToDisable != null && menuObjectsToDisable.Length > 0 && menuObjectsToDisable[0] != null)
                menuObjectsToDisable[0].SetActive(visible);
        }

        private static void SetInteractionMode(InteractionStateManager.InteractionMode mode)
        {
            var interaction = FindFirstObjectByType<InteractionStateManager>();
            if (interaction != null)
                interaction.SetMode(mode);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            Raise();
        }

        private void Raise() => Changed?.Invoke();

        private void ResetPlayerPositionAndRotation()
        {
            playerRig.position = new Vector3(0, playerRig.position.y, 0);
            playerRig.rotation = Quaternion.identity;
            playerRig.GetComponent<XROrigin>().MatchOriginUpCameraForward(Vector3.up, Vector3.forward);
        }
    }
}
