using SafeSite.Core.UI;
using System.Collections;
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

        [SerializeField]
        private NearFarInteractor nearFarHand;
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

        public void UnloadScenario()
        {
            if(!sceneInstance.IsValid())
            {
                return;
            }

            StartCoroutine(UnloadSceneRoutine());
        }

        private void Update()
        {

        }

        private IEnumerator UnloadSceneRoutine()
        {
            ScreenFadeController.Instance.FadeToBlack(2);
            yield return new WaitForSeconds(2);

            var unloadOperation   = Addressables.UnloadSceneAsync(sceneInstance);
            yield return unloadOperation;

            ResetPlayerPositionAndRotation();

            yield return new WaitForSeconds(1);
            ScreenFadeController.Instance.FadeOutOfBlack(2);
            Debug.Log("SceneUnloaded");

            menuObjectsToDisable[0].SetActive(true); //TEMP
        }

        private void  ResetPlayerPositionAndRotation()
        {
            nearFarHand.enabled = true;
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
