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
            // Vendor models are authored in the frame of the tracked controller pose itself (Meta's
            // OVR controller anchor is the OpenXR aim pose - see OVRRuntimeController, which adds a
            // -60 deg X offset only for grip-authored glTF models - and the XRI "Left/Right Controller"
            // TrackedPoseDriver is bound to pointerPosition/pointerRotation, i.e. that same aim pose).
            // So they must sit at identity directly under the tracked controller. This component's own
            // transform ("Left/Right Controller Visual") is NOT that frame: it carries a 180 deg Y turn,
            // a -5 cm Z offset and (on the right) a -1 X mirror that exist only to fit XRI's generic
            // mesh. Parenting under it is what rendered the vendor models facing backwards.
            var trackedController = transform.parent != null ? transform.parent : transform;
#endif

#if VR_META
            if (questControllerPrefab != null)
            {
                var instance = Instantiate(questControllerPrefab, trackedController, false);

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
                Instantiate(picoControllerPrefab, trackedController, false);

                if (fallbackModel != null)
                    fallbackModel.SetActive(false);
            }
#endif
        }
    }
}
