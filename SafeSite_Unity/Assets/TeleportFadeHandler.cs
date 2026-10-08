using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
public class TeleportFadeHandler : MonoBehaviour // Uses our screen fader in combination with XR toolkits built in delay to give a fading effect when teleporting!
{

    [SerializeField]
    private TeleportationProvider teleportationProvider;
    [SerializeField]
    private float teleportFadeDuration = .15f;

    private Coroutine fadeRoutine;

    private void OnEnable()
    {
        teleportationProvider.locomotionStateChanged += OnStateChange;
        teleportationProvider.locomotionEnded += OnTeleportFinished;

    }

    private void OnDisable()
    {
        teleportationProvider.locomotionStateChanged -= OnStateChange;
        teleportationProvider.locomotionEnded -= OnTeleportFinished;
    }

    private void OnStateChange(LocomotionProvider locomotionProvider,LocomotionState state)    
     {
       if(locomotionProvider.locomotionState == LocomotionState.Preparing)
             {
            // Fade to black 
       ScreenFadeController.Instance.FadeToBlack(teleportFadeDuration);
              }
    }

    private void OnTeleportFinished(LocomotionProvider locomotion)
    {
        ScreenFadeController.Instance.FadeOutOfBlack(teleportFadeDuration);
    }
}
