using UnityEngine;

public class FallingObjectsScenarioController : MonoBehaviour
{

    [SerializeField]
    FallingLoadHandler fallingLoadHandler;

    [SerializeField]
    private Transform PlayerRig; // Add Auto grabber
public enum ScenarioState // Update later
    {
        Intro, 
        TeleportToDrill,
        PickupDrill,
        ReturnWithDrill,
        CraneAccident
    }

    [SerializeField] 
    private ScenarioState currentState;

    [SerializeField]
    private Transform playerStartingPositionTransform;

    [SerializeField]
    private GameObject startingTransformNode;

    [SerializeField]
    private GameObject middleAccidentNode;


    // TEMP Audio CLips
    [SerializeField]
    private AudioClip[] placeHolderAudioClips;

    private void Start()
    {
        SetCurrentScenarioState(currentState);
    }
    private void SetCurrentScenarioState(ScenarioState newScenarioState)
    {

        currentState = newScenarioState;

        switch(currentState)
            {
            case ScenarioState.Intro:
                Invoke("StartIntro", 6); // Add delay for preview
               // StartIntro();
                break;
            case ScenarioState.TeleportToDrill:
                StartTeleportToDrill();
                break;
            case ScenarioState.PickupDrill:
                StartPickUpDrill();
                break;
            case ScenarioState.ReturnWithDrill:
                StartReturn();
                break;
            case ScenarioState.CraneAccident:
                StartAccident();
                break;
           }

        
        }


  
    private void StartIntro()
    {
        PlayerRig.transform.position = playerStartingPositionTransform.transform.position; // Eventually have fade / other polish
        PlayerRig.transform.rotation = playerStartingPositionTransform.transform.rotation;


        SafeSiteAudioManager.Instance.PlayVoiceOver(placeHolderAudioClips[0], IntroFinished);
    }

    private void StartTeleportToDrill()
    {
        startingTransformNode.SetActive(true);
    }

    private void StartPickUpDrill()
    {
        SafeSiteAudioManager.Instance.PlayVoiceOver(placeHolderAudioClips[3]);
    }

    private void StartReturn()
    {
        Debug.Log("Start Return. - Come Back The Way You Came");
        SafeSiteAudioManager.Instance.PlayVoiceOver(placeHolderAudioClips[4]);
        middleAccidentNode.SetActive(true);
    }

    private void StartAccident()
    {
        if(currentState != ScenarioState.CraneAccident)
        {
            return;
        }
        Debug.Log("TRIGGER CRANE ACCIDENT");
        fallingLoadHandler.DropLoad();
        // Trigger Audio?
        // Fade To Black Etc
        
    }


    ////////// Public for External Calls / Events
    
    public void IntroFinished()
    {
        SetCurrentScenarioState(ScenarioState.TeleportToDrill);
    }

    public void FirstTeleportNodeHit()
    {
        SafeSiteAudioManager.Instance.PlayVoiceOver(placeHolderAudioClips[1]);
    }

    public void DrillReached()
    {
        SetCurrentScenarioState(ScenarioState.PickupDrill);

    }

    public void DrillStore()
    {
        SetCurrentScenarioState(ScenarioState.ReturnWithDrill);
    }

    public void AccidentTriggered()
    {
        SetCurrentScenarioState(ScenarioState.CraneAccident);
    }

    public void CustomDebugTrigger()
    {
        Debug.Log("Attached Event Being Called");
    }
}
