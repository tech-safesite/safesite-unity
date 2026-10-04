using UnityEngine;

public class FallingObjectsScenarioController : MonoBehaviour
{
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
    private GameObject startingTransformNode;

    private void SetCurrentScenarioState(ScenarioState newScenarioState)
    {

        currentState = newScenarioState;

        switch(currentState)
            {
            case ScenarioState.Intro:
                StartIntro();
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
        // Set All Objects To Default State 

        // Call Audio / Sequence / Intro Routine, 


        // Remove
        Invoke("StartTeleportToDrill", 5); // for testing
    }

    private void StartTeleportToDrill()
    {
        // Turn On First Teleport Node
        startingTransformNode.SetActive(true);
    }

    private void StartPickUpDrill()
    {

    }

    private void StartReturn()
    {

    }

    private void StartAccident()
    {
        Debug.Log("TRIGGER CRANE ACCIDENT");
    }


    ////////// Public for External Calls / Events
    
    public void IntroFinished()
    {

    }

    public void DrillReached()
    {

    }

    public void DrillStore()
    {

    }

    public void AccidentTriggered()
    {

    }

}
