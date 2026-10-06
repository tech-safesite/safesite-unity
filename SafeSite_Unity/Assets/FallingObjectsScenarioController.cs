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

    [SerializeField]
    private VoiceoverData fallingObjectsVoiceoverData;

    private void Awake()
    {
    }
    private void Start()
    {
        VoiceoverHandler.Instance.SetVoiceOverData(fallingObjectsVoiceoverData);
        SetCurrentScenarioState(currentState);
    }
    private void SetCurrentScenarioState(ScenarioState newScenarioState)
    {

        currentState = newScenarioState;

        switch(currentState)
            {
            case ScenarioState.Intro:
                Invoke("StartIntro", 6); // Add delay for video preview
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

        VoiceoverHandler.Instance.PlayVoiceOver("intro");
    }

    private void StartTeleportToDrill()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("first_teleport");
        startingTransformNode.SetActive(true);
    }

    private void StartPickUpDrill()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("grab drill");
    }

    private void StartReturn()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("return_way");
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
        VoiceoverHandler.Instance.PlayVoiceOver("second_teleport");
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
