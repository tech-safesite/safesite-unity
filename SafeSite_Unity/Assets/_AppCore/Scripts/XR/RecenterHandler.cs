using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace SafeSite.XR
{
    /// <summary>
    /// Keeps the user facing the content when the headset recentres. Sits on the XR Origin.
    ///
    /// A recentre (holding the Meta button, or the headset doing it by itself when it is put back on)
    /// turns the tracking space, so without this the user ends up looking somewhere else in the world
    /// and world-placed menus appear beside or behind them. After any recentre this turns the rig so
    /// the user faces the direction they were placed in (the spawn point, or the end of their last
    /// teleport) and stands at that spot.
    ///
    /// Runs before other LateUpdates so menus that re-place themselves after a recentre
    /// (GlobalMenu) see the corrected head pose.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XROrigin))]
    public class RecenterHandler : MonoBehaviour
    {
        private XROrigin origin;
        private TeleportationProvider teleportation;
        private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();

        // Where the user was last placed: floor position and the way they should face.
        private Vector3 homePosition;
        private Vector3 homeForward;

        // Frame of the last recentre; the head pose catches up a frame later.
        private int recenteredFrame = -1;

        private void Awake()
        {
            origin = GetComponent<XROrigin>();
            teleportation = GetComponentInChildren<TeleportationProvider>(true);
            homePosition = transform.position;
            homeForward = Flat(transform.forward);
        }

        private void OnEnable()
        {
            SubsystemManager.GetSubsystems(inputSubsystems);
            foreach (var subsystem in inputSubsystems)
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;

            if (teleportation != null)
                teleportation.locomotionEnded += OnTeleported;

#if VR_META
            OVRManager.HMDMounted += OnRecentered;
            if (OVRManager.display != null)
                OVRManager.display.RecenteredPose += OnRecentered;
#endif
        }

        private void OnDisable()
        {
            foreach (var subsystem in inputSubsystems)
                subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            inputSubsystems.Clear();

            if (teleportation != null)
                teleportation.locomotionEnded -= OnTeleported;

#if VR_META
            OVRManager.HMDMounted -= OnRecentered;
            if (OVRManager.display != null)
                OVRManager.display.RecenteredPose -= OnRecentered;
#endif
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem _) => OnRecentered();

        private void OnRecentered() => recenteredFrame = Time.frameCount;

        /// <summary>Wherever a teleport left the user is where a recentre puts them back.</summary>
        private void OnTeleported(LocomotionProvider _)
        {
            var head = origin.Camera != null ? origin.Camera.transform : null;
            if (head == null)
                return;
            homePosition = new Vector3(head.position.x, transform.position.y, head.position.z);
            var forward = Flat(head.forward);
            if (forward != Vector3.zero)
                homeForward = forward;
        }

        private void LateUpdate()
        {
            if (recenteredFrame < 0 || Time.frameCount <= recenteredFrame)
                return;
            recenteredFrame = -1;

            var head = origin.Camera != null ? origin.Camera.transform : null;
            if (head == null || homeForward == Vector3.zero)
                return;

            origin.MatchOriginUpCameraForward(Vector3.up, homeForward);
            origin.MoveCameraToWorldLocation(new Vector3(homePosition.x, head.position.y, homePosition.z));
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.zero : direction.normalized;
        }
    }
}
