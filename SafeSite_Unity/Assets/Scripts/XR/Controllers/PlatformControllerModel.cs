using UnityEngine;

namespace SafeSite.XR.Controllers
{
    /// <summary>
    /// Replaces the generic XRI controller mesh with the vendor-accurate model for the headset this
    /// build targets. HeadsetBuildPipeline sets VR_META or VR_PICO as an extra scripting define for
    /// the Android player build only (see HeadsetBuildConfig.extraDefines) - neither symbol exists
    /// in the Editor, so the generic fallback model stays visible there and in Play Mode testing.
    ///
    /// Three things had to line up here, and each failed in a distinct, visible way on device:
    ///
    /// 1. Space. modelAnchor must be a direct child of the XR Origin's Camera Offset.
    ///    TrackedPoseDriver writes localPosition/localRotation from a pose that is already in
    ///    tracking space, so nesting the anchor under the (also pose-driven) "Left Controller"
    ///    applied the grip pose on top of the aim pose and threw the mesh far from the hand while
    ///    the interaction ray stayed correct. PoseDriverValidator now fails the build on that.
    ///
    /// 2. Pose. The anchor tracks the OpenXR *grip* pose (devicePose), not the aim/pointer pose the
    ///    XRI controller object uses, because that is what vendor meshes are authored around.
    ///
    /// 3. Convention. The grip pose alone is still not where Meta's mesh belongs - see
    ///    k_GripToMetaModelOffset below. PICO needs no such correction.
    ///
    /// Mirroring is deliberately not involved. "Right Controller Visual" carries a localScale of
    /// (-1, 1, 1) - the XRI Starter Assets trick for reusing one generic mesh on both hands - which
    /// is correct for that mesh and left alone. Vendor models are modelled per hand, so they hang
    /// off modelAnchor, outside the mirrored visual entirely.
    /// </summary>
    public class PlatformControllerModel : MonoBehaviour
    {
#if VR_META
        /// <summary>
        /// Converts the OpenXR grip pose into the pose Meta's controller meshes are authored around.
        ///
        /// OVRCameraRig parents OVRControllerPrefab to an anchor sitting at
        /// OVRInput.GetLocalControllerPosition/Rotation, and OVRControllerHelper applies no transform
        /// of its own - it only toggles which mesh is visible. Those OVRInput calls read
        /// "devicePose/position" and "devicePose/rotation" (devicePose is aliased gripPose in Unity's
        /// OpenXR bindings) and compose them with the inverse of the offset below, copied verbatim
        /// from OVRInput.cs (_gripToVrapiOffsetInverse):
        ///
        ///     metaMeshPose = gripPose * gripToVrapiOffset.inverse
        ///
        /// That offset is a -60 degree rotation about X plus a few centimetres, so a mesh placed at
        /// the raw grip pose sits at a conspicuously wrong angle. Inverting it here reproduces Meta's
        /// own placement exactly. It is deliberately derived from Meta's constant rather than pasted
        /// as pre-multiplied numbers, so the derivation stays checkable, and it is the same for both
        /// hands - OVRInput applies this one constant to LTouch and RTouch alike.
        ///
        /// A static offset is preferred over driving the anchor from OVRInput every frame: the mesh
        /// and the interaction ray then share one TrackedPoseDriver update and cannot drift apart by
        /// a frame.
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
        [Tooltip("Transform the vendor model is parented to. Must be a direct child of Camera Offset and " +
                 "driven by the grip (device) pose. Falls back to this transform if unset, which will put " +
                 "the model in the wrong place.")]
        private Transform modelAnchor;

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

#if VR_META
        /// <summary>
        /// OVRControllerHelper reads OVRInput to decide which Touch mesh to show, and OVRInput is only
        /// pumped by OVRManager's update loop. OVRManager also expects to be the first OVR object
        /// alive, so it is created before the scene loads rather than from a MonoBehaviour Awake -
        /// creating it mid-Awake meant the helper could sample OVRInput before the plugin was ready
        /// and fall back to the wrong controller model.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureOvrManager()
        {
            if (Object.FindFirstObjectByType<OVRManager>() != null)
                return;

            var host = new GameObject("OVR Manager");
            DontDestroyOnLoad(host);
            host.AddComponent<OVRManager>();
        }
#endif

        private void Awake()
        {
#if VR_META || VR_PICO
            var parent = modelAnchor != null ? modelAnchor : transform;

            if (modelAnchor == null)
            {
                Debug.LogWarning(
                    $"[PlatformControllerModel] '{name}' has no modelAnchor assigned; the vendor controller " +
                    "model will inherit the aim pose and render in the wrong place.", this);
            }
#endif

#if VR_META
            if (questControllerPrefab != null)
            {
                var instance = Instantiate(questControllerPrefab, parent, false);
                ApplyLocalPose(instance.transform, k_GripToMetaModelOffset.GetPosition(),
                    k_GripToMetaModelOffset.rotation);

                var helper = instance.GetComponentInChildren<OVRControllerHelper>(true);
                if (helper != null)
                    helper.m_controller = isLeftHand ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

                if (fallbackModel != null)
                    fallbackModel.SetActive(false);
            }
#elif VR_PICO
            if (picoControllerPrefab != null)
            {
                // PICO needs no grip-to-model correction: the PICO Touch profile reports one pose for
                // both grip and aim ("For the PICO Touch device, this is both the grip and the pointer
                // rotation" - PICO4ControllerProfile), and its controller prefabs are authored to it.
                var instance = Instantiate(picoControllerPrefab, parent, false);
                ApplyLocalPose(instance.transform, Vector3.zero, Quaternion.identity);

                if (fallbackModel != null)
                    fallbackModel.SetActive(false);
            }
#endif
        }

#if VR_META || VR_PICO
        private void ApplyLocalPose(Transform model, Vector3 position, Quaternion rotation)
        {
            model.SetLocalPositionAndRotation(
                position + extraPositionOffset,
                rotation * Quaternion.Euler(extraRotationOffset));
        }
#endif
    }
}
