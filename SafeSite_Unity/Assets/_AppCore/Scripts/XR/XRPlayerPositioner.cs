using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Moves the user to a spot in the world, facing that spot's forward. Sits on the rig's Locomotion
/// object. Goes through XRI teleporting, so the screen fade and RecenterHandler both see it.
/// See Assets/_AppCore/Player/README.md.
/// </summary>
public class XRPlayerPositioner : MonoBehaviour
{
    [SerializeField]
    private TeleportationProvider teleportationProvider;

    public void MovePlayerTo(Transform target)
    {
        TeleportRequest request = new TeleportRequest
        {
            destinationPosition = target.position,
            destinationRotation = target.rotation,
            matchOrientation = MatchOrientation.TargetUpAndForward
        };

        teleportationProvider.QueueTeleportRequest(request);
    }
}