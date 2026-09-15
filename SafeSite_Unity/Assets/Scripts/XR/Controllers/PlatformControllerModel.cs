using UnityEngine;

namespace SafeSite.XR.Controllers
{
    /// <summary>
    /// Replaces the generic XRI controller mesh with the vendor-accurate model for the headset
    /// this build targets. HeadsetBuildPipeline sets VR_META or VR_PICO as an extra scripting
    /// define for the Android player build only (see HeadsetBuildConfig.extraDefines) - neither
    /// symbol exists in the Editor, so the generic fallback model stays visible there and in Play
    /// Mode testing.
    /// </summary>
    public class PlatformControllerModel : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The generic XRI controller model to hide once a vendor-specific model loads.")]
        private GameObject fallbackModel;

        [SerializeField]
        [Tooltip("Meta's OVRControllerPrefab - contains meshes for every Touch controller variant and switches between them at runtime based on the connected headset.")]
        private GameObject questControllerPrefab;

        [SerializeField]
        [Tooltip("Pico's per-hand controller loader prefab (LeftControllerModel / RightControllerModel) - switches between Neo3/G3/PICO4/PICO4U meshes at runtime based on the connected headset.")]
        private GameObject picoControllerPrefab;

        [SerializeField]
        private bool isLeftHand;

private void Awake()
        {
#if VR_META || VR_PICO
            // "Left/Right Controller Visual" mirrors the generic XRI model onto the right hand by
            // negating local X scale (its own model child has no scale override, relying on that
            // parent flip). Vendor models are already correctly modelled per hand, so on the right
            // side we counter-scale the instantiated model by -1 to undo the parent's mirroring.
            var modelScale = isLeftHand ? Vector3.one : new Vector3(-1f, 1f, 1f);
#endif

#if VR_META
            if (questControllerPrefab != null)
            {
                var instance = Instantiate(questControllerPrefab, transform, false);
                instance.transform.localScale = modelScale;

                var helper = instance.GetComponentInChildren<OVRControllerHelper>(true);
                if (helper != null)
                    helper.m_controller = isLeftHand ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

                if (Object.FindFirstObjectByType<OVRManager>() == null)
                    new GameObject("OVR Manager").AddComponent<OVRManager>();

                if (fallbackModel != null)
                    fallbackModel.SetActive(false);
            }
#elif VR_PICO
            if (picoControllerPrefab != null)
            {
                var instance = Instantiate(picoControllerPrefab, transform, false);
                instance.transform.localScale = modelScale;

                if (fallbackModel != null)
                    fallbackModel.SetActive(false);
            }
#endif
        }
    }
}
