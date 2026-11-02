using Modules.ScenarioLauncher.External;
using SafeSite.Core.UI;
using System.Collections;
using System.Threading.Tasks;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SafeSite.Core.Scenarios
{

    
    public class TrainingScenarioLoader : MonoBehaviour
    {
        public Transform playerRig;

        public static TrainingScenarioLoader Instance { get; private set; }
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
        private AsyncOperationHandle<SceneInstance> sceneInstance;

        public GameObject[] menuObjectsToDisable; 

        public void LoadScenario(string sceneAddress)
        {
            StartCoroutine(LoadScenarioRoutine(sceneAddress));
        }
        public IEnumerator LoadScenarioRoutine(string sceneAddress)
        {
            ScreenFadeController.Instance.FadeToBlack(1);
            yield return new WaitForSeconds(1.1f);

            sceneInstance = Addressables.LoadSceneAsync(sceneAddress, LoadSceneMode.Additive);

            menuObjectsToDisable[0].SetActive(false); //TEMP

            yield return sceneInstance;

            if (sceneInstance.Status == AsyncOperationStatus.Succeeded)
            {
                Debug.Log("Scene Successfully Loaded");
                ScreenFadeController.Instance.FadeOutOfBlack(2);
            }

            else
            {
                Debug.Log("Scene Loading Failed");
            }

        }
        // Start is called once before the first execution of Update after the MonoBehaviour is created

        /// <summary>
        /// Plays a scenario downloaded from SafeSite (the launcher menu's Start). The download runs
        /// with the menu still showing its progress; the scene loads behind the fade.
        /// </summary>
        public void PlayDownloaded(string scenarioId)
        {
            var launcher = ScenarioLauncherServiceLocator.FindInScene();
            if (launcher != null)
                StartCoroutine(PlayDownloadedRoutine(launcher, scenarioId));
        }

        private IEnumerator PlayDownloadedRoutine(IScenarioLauncherService launcher, string scenarioId)
        {
            yield return WaitFor(launcher.DownloadAsync(scenarioId));
            if (!System.Linq.Enumerable.Any(launcher.Scenarios, s => s.Id == scenarioId && s.IsInstalled))
                yield break; // the menu shows what went wrong

            ScreenFadeController.Instance.FadeToBlack(1);
            yield return new WaitForSeconds(1.1f);
            menuObjectsToDisable[0].SetActive(false);

            yield return WaitFor(launcher.PlayAsync(scenarioId));
            if (launcher.State != LauncherState.Playing)
            {
                ResetPlayerPositionAndRotation();
                menuObjectsToDisable[0].SetActive(true);
            }
            ScreenFadeController.Instance.FadeOutOfBlack(2);
        }

        private static IEnumerator WaitFor(Task task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
        }

        /// <summary>Ends the playing scenario, built-in or downloaded, and returns to the menu.</summary>
        public void UnloadScenario()
        {
            if (sceneInstance.IsValid())
            {
                StartCoroutine(UnloadSceneRoutine());
                return;
            }

            var launcher = ScenarioLauncherServiceLocator.FindInScene();
            if (launcher != null && launcher.State == LauncherState.Playing)
                StartCoroutine(UnloadSceneRoutine(launcher.StopAsync));
        }

        private void Update()
        {

        }

        /// <param name="unload">Unloads a downloaded scenario; null unloads the built-in one.</param>
        private IEnumerator UnloadSceneRoutine(System.Func<Task> unload = null)
        {
            ScreenFadeController.Instance.FadeToBlack(2);
            yield return new WaitForSeconds(2);

            if (unload != null)
            {
                yield return WaitFor(unload());
            }
            else
            {
                var unloadOperation = Addressables.UnloadSceneAsync(sceneInstance);
                yield return unloadOperation;
            }

            ResetPlayerPositionAndRotation();
            menuObjectsToDisable[0].SetActive(true);

            yield return new WaitForSeconds(1);
            ScreenFadeController.Instance.FadeOutOfBlack(2);
            Debug.Log("SceneUnloaded");

            FindFirstObjectByType<InteractionStateManager>().SetMode(InteractionStateManager.InteractionMode.UIAndTeleport);
        }

        private void  ResetPlayerPositionAndRotation()
        {
            playerRig.position = new Vector3(0, playerRig.position.y, 0);
            playerRig.rotation = Quaternion.identity;
            playerRig.GetComponent<XROrigin>().MatchOriginUpCameraForward(Vector3.up, Vector3.forward);
        }

        public void BackToMenu()
        {
            UnloadScenario();
        }
    }

}
