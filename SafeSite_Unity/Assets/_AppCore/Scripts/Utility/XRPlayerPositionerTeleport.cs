using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

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