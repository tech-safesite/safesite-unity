using UnityEngine;
#if VR_META || VR_PICO
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
#endif

namespace SafeSite.XR.Controllers
{
    /// <summary>
    /// Swaps the generic XRI controller mesh for the real Quest or PICO controller model on device.
    /// Sits on "Left/Right Controller Visual"; nothing else needs wiring.
    ///
    /// HeadsetBuildPipeline defines VR_META or VR_PICO for the Android player build only, so in the
    /// Editor and Play Mode the generic model stays visible and this component does nothing.
    ///
    /// Where the model goes: vendor meshes are authored around the OpenXR grip pose, but the XRI
    /// "Left/Right Controller" object follows the aim (pointer) pose, and its Controller Visual is
    /// also turned 180 degrees for the generic mesh. Parenting a vendor mesh there makes it render
    /// backwards. So at startup this component creates its own grip-pose anchor as a direct child of
    /// the XR Origin's Camera Offset and parents the vendor model to that. The anchor is shown and
    /// hidden together with this component, so when XRInputModalityManager switches to hands, the
    /// vendor controller disappears too.
    /// </summary>
    public class PlatformControllerModel : MonoBehaviour
    {
#if VR_META
        /// <summary>
        /// Meta's meshes are not placed at the raw grip pose: OVRInput applies the inverse of this
        /// offset (a -60 degree rotation about X plus a few centimetres; _gripToVrapiOffset in
        /// OVRInput.cs), the same for both hands. Applying it here reproduces Meta's own placement.
        /// </summary>
        private static readonly Matrix4x4 k_GripToMetaModelOffset =
            Matrix4x4.TRS(
                new Vector3(0f, -0.03f, -0.04f),
                new Quaternion(-0.5f, 0f, 0f, 0.86603f),
                Vector3.one).inverse;
#endif

        [SerializeField]
        [Tooltip("The generic XRI controller model to hide once a vendor-specific model loads.")]
        private GameObject fallbackModel;

        [SerializeField]
        [Tooltip("Meta's OVRControllerPrefab - contains meshes for every Touch controller variant and " +
                 "switches between them at runtime based on the connected headset.")]
        private GameObject questControllerPrefab;

        [SerializeField]
        [Tooltip("PICO's per-hand controller loader prefab (LeftControllerModel / RightControllerModel) - " +
                 "switches between Neo3/G3/PICO4/PICO4U meshes at runtime based on the connected headset.")]
        private GameObject picoControllerPrefab;

        [SerializeField]
        private bool isLeftHand;

        [Header("Fine tuning")]
        [SerializeField]
        [Tooltip("Applied on top of the vendor-correct placement. Leave at zero unless a specific runtime " +
                 "still seats the model wrongly.")]
        private Vector3 extraPositionOffset;

        [SerializeField]
        [Tooltip("Applied on top of the vendor-correct placement, in degrees. Leave at zero unless a " +
                 "specific runtime still seats the model wrongly.")]
        private Vector3 extraRotationOffset;

#if VR_META || VR_PICO
        private GameObject gripAnchor;
#endif

#if VR_META
        /// <summary>
        /// OVRControllerHelper reads OVRInput to decide which Touch mesh to show, and OVRInput is only
        /// pumped by OVRManager. OVRManager expects to be the first OVR object alive, so it is created
        /// before the scene loads rather than from Awake.
        ///
        /// It is here only to drive the controller meshes. Tracking belongs to the XR Origin, so its
        /// own recentering is switched off: left on, OVRManager recenters by itself every time the
        /// headset is put back on, on top of the system's recenter, which moved the app's centre.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureOvrManager()
        {
            var manager = Object.FindFirstObjectByType<OVRManager>();
            if (manager == null)
            {
                var host = new GameObject("OVR Manager");
                DontDestroyOnLoad(host);
                manager = host.AddComponent<OVRManager>();
            }

            manager.AllowRecenter = false;
        }
#endif

        private void Awake()
        {
#if VR_META
            if (questControllerPrefab == null)
                return;

            var anchor = CreateGripAnchor();
            if (anchor == null)
                return;

            var instance = Instantiate(questControllerPrefab, anchor, false);
            ApplyLocalPose(instance.transform, k_GripToMetaModelOffset.GetPosition(), k_GripToMetaModelOffset.rotation);

            var helper = instance.GetComponentInChildren<OVRControllerHelper>(true);
            if (helper != null)
                helper.m_controller = isLeftHand ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

            if (fallbackModel != null)
                fallbackModel.SetActive(false);
#elif VR_PICO
            if (picoControllerPrefab == null)
                return;

            var anchor = CreateGripAnchor();
            if (anchor == null)
                return;

            // PICO reports one pose for grip and aim and its prefabs are authored to it: no correction.
            var instance = Instantiate(picoControllerPrefab, anchor, false);
            ApplyLocalPose(instance.transform, Vector3.zero, Quaternion.identity);

            if (fallbackModel != null)
                fallbackModel.SetActive(false);
#endif
        }

#if VR_META || VR_PICO
        private void OnEnable()
        {
            if (gripAnchor != null)
                gripAnchor.SetActive(true);
        }

        private void OnDisable()
        {
            if (gripAnchor != null)
                gripAnchor.SetActive(false);
        }

        private void OnDestroy()
        {
            if (gripAnchor != null)
                Destroy(gripAnchor);
        }

        /// <summary>
        /// A transform under Camera Offset that follows this hand's grip pose. Pose drivers must be
        /// direct children of Camera Offset: the pose they write is already in tracking space.
        /// </summary>
        private Transform CreateGripAnchor()
        {
            var origin = GetComponentInParent<XROrigin>(true);
            var cameraOffset = origin != null ? origin.CameraFloorOffsetObject : null;
            if (cameraOffset == null)
            {
                Debug.LogError($"[PlatformControllerModel] '{name}' is not under an XR Origin with a Camera Offset; " +
                               "keeping the generic controller model.", this);
                return null;
            }

            var hand = isLeftHand ? "{LeftHand}" : "{RightHand}";

            // Built inactive so the driver binds its actions once, after they are assigned.
            gripAnchor = new GameObject((isLeftHand ? "Left" : "Right") + " Controller Model (Grip)");
            gripAnchor.SetActive(false);
            gripAnchor.transform.SetParent(cameraOffset.transform, false);

            var driver = gripAnchor.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.positionInput = new InputActionProperty(
                new InputAction("Grip Position", binding: $"<XRController>{hand}/devicePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(
                new InputAction("Grip Rotation", binding: $"<XRController>{hand}/deviceRotation", expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(
                new InputAction("Grip Tracking State", binding: $"<XRController>{hand}/trackingState", expectedControlType: "Integer"));

            // OnEnable (which runs right after Awake) shows it.
            return gripAnchor.transform;
        }

        private void ApplyLocalPose(Transform model, Vector3 position, Quaternion rotation)
        {
            model.SetLocalPositionAndRotation(
                position + extraPositionOffset,
                rotation * Quaternion.Euler(extraRotationOffset));
        }
#endif
    }
}
