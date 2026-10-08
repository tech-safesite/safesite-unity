using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public class FallingObjectsScenarioController : MonoBehaviour
{
    [Header("Voiceover Dataset")]


    [SerializeField]
    private VoiceoverData fallingObjectsVoiceoverData;

    [Header("References")]

    [SerializeField]
    FallingLoadHandler fallingLoadHandler;

    [SerializeField]
    private Transform PlayerRig; // Add Auto grabber

    [SerializeField]
    private XROrigin playerXROrigin; // Add Auto grabber



    public enum ScenarioState // Update later
    {
        WrongWay_Intro, 
        WrongWay_TeleportToDrill,
        WrongWay_PickupDrill,
        WrongWay_ReturnWithDrill,
        WrongWay_CraneAccident,

        CorrectWay_Reset,
        CorrectWay_WaveToBanksMan,
        CorrectWay_SafeToCross,
        CorrectWay_BackAtStart
    }

    [SerializeField]
    [Header("Current State")]
    private ScenarioState currentState;

    [Header("Transforms / Nodes")]

    [SerializeField]
    private Transform playerStartingPositionTransform;

    [SerializeField]
    private GameObject startingTransformNode;

    [SerializeField]
    private GameObject middleAccidentNode;

    [SerializeField]
    private Transform correctWayStartingPosition;

    [SerializeField]
    private GameObject[] returnTeleportNodes;


    [Header("Load Drop / Accident")]

    [SerializeField]
    private CameraShaker cameraShaker; // Might Need Dedciated Global System

    [SerializeField]
    private float postAccidentSettleTime;

    [Header("Banksman")]


    [SerializeField]
    private BanksmanController banksmanController;

    [SerializeField]
    private GameObject temp_WaveTrigger;

    [Header("Crane")]
    [SerializeField]
    private CraneController craneController;

    // Events
    public UnityEvent finalTeleportNodeReached;

    // Temp
    private bool endNodeReached = false;

    private void OnEnable()
    {
      //  finalTeleportNodeReached.AddListener(returnTeleportNodes[1].GetComponent<TeleportationAnchor>().activated);
       // returnTeleportNodes[1].GetComponent<TeleportationAnchor>().teleporting.AddListener(finalTeleportNodeReached);
    }

    private void OnDisable()
    {
        
    }

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
            case ScenarioState.WrongWay_Intro:
            //    Invoke("StartIntro"); // Add delay for video preview
                StartIntro();
                break;
            case ScenarioState.WrongWay_TeleportToDrill:
                StartTeleportToDrill();
                break;
            case ScenarioState.WrongWay_PickupDrill:
                StartPickUpDrill();
                break;
            case ScenarioState.WrongWay_ReturnWithDrill:
                StartReturn();
                break;
            case ScenarioState.WrongWay_CraneAccident:
                StartAccident();
                break;
            case ScenarioState.CorrectWay_Reset:
                StartCorrectWayReset();
                break;
            case ScenarioState.CorrectWay_WaveToBanksMan:
                StartCorrectWayWaveToBanksman();
                break;
            case ScenarioState.CorrectWay_SafeToCross:
                StartCorrectWaySafeToCross();
                break;
            case ScenarioState.CorrectWay_BackAtStart:
                StartCorrectWayBackAtStart();
                break;
        }

        
        }


  
    private void StartIntro()
    {
        PlayerRig.transform.position = playerStartingPositionTransform.transform.position; // Eventually have fade / other polish
        PlayerRig.transform.rotation = playerStartingPositionTransform.transform.rotation;

        VoiceoverHandler.Instance.PlayVoiceOver("intro",IntroFinished);
    }

    private void StartTeleportToDrill()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("first_teleport");
        startingTransformNode.SetActive(true);
        craneController.MoveCraneOnStart();
    }

    private void StartPickUpDrill()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("grab_drill");
    }

    private void StartReturn()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("return_way");
        middleAccidentNode.SetActive(true);
        craneController.CraneAccidentMovement();
    }

    private void StartAccident()
    {
        if(currentState != ScenarioState.WrongWay_CraneAccident)
        {
            return;
        }
        Debug.Log("TRIGGER CRANE ACCIDENT");

        craneController.CraneLoadAtPoint();

        fallingLoadHandler.DropLoad();

        StartCoroutine(AccidentDialogueSequence());
    }

    private IEnumerator AccidentDialogueSequence()
    {
        yield return new WaitUntil(() => fallingLoadHandler.GetLoadDropped());

        VoiceoverHandler.Instance.PlayVoiceOver("post_accident_1");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently); // Add function to Voiceover Controller to stack??

        VoiceoverHandler.Instance.PlayVoiceOver("post_accident_2");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        yield return new WaitForSeconds(postAccidentSettleTime);

        VoiceoverHandler.Instance.PlayVoiceOver("one_more_step",StartCorrectWayReset);
    }

    private void StartCorrectWayReset()
    {
        // Any Cleanup / Hide Debris
        fallingLoadHandler.ResetLoad();
        // Player Move / Fade
         PlayerRig.SetPositionAndRotation(correctWayStartingPosition.position, correctWayStartingPosition.rotation); // 
        playerXROrigin.MatchOriginUpCameraForward(Vector3.up, correctWayStartingPosition.forward); // make reuseable helper? 

        Camera.main.transform.localPosition = (Vector3.zero);

        StartCoroutine(StartCorrectWayResetRoutine());
    }

    private IEnumerator StartCorrectWayResetRoutine()
    {
        // Voiceover Lucky One
        VoiceoverHandler.Instance.PlayVoiceOver("correct_almost_crushed");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);
        // Voice Look Around 
        VoiceoverHandler.Instance.PlayVoiceOver("correct_look_around_you");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        fallingLoadHandler.ShowExclusionZone();
        // Turn On Exclusion Zone

        VoiceoverHandler.Instance.PlayVoiceOver("correct_exclusion_zone");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);
        // Voice Over Explain Exclusion Zone 

        // Change State -> Wave To Banksmman
        CorrectWayWaveToBanksmanTrigger();
        yield return null;

    }


    private void StartCorrectWayWaveToBanksman()
    {
        StartCoroutine(WaveToBanksmanRoutine());    
    }

    private IEnumerator WaveToBanksmanRoutine()
    {
        banksmanController.HighlightBanksnan(true);

        VoiceoverHandler.Instance.PlayVoiceOver("correct_banksman_intro");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        VoiceoverHandler.Instance.PlayVoiceOver("correct_wave_to_banksman");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        temp_WaveTrigger.SetActive(true);

        yield return new WaitUntil(() => banksmanController.GetHandWave());
        temp_WaveTrigger.SetActive(false); // incase

        banksmanController.TurnTowardPlayerAndWave();

        VoiceoverHandler.Instance.PlayVoiceOver("correct_stay_where_you_are");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        // Play Banksman Audio
        craneController.CraneLoadCross();

        VoiceoverHandler.Instance.PlayVoiceOver("correct_waiting_speech_1");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        VoiceoverHandler.Instance.PlayVoiceOver("correct_waiting_speech_2");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        //FInish

        SetCurrentScenarioState(ScenarioState.CorrectWay_SafeToCross);

        yield return null;

    }

    private void StartCorrectWaySafeToCross()
    {
        // Set State Defaults to be safe?

        StartCoroutine(StartCorrectWaySafeToCrossRoutine());
  
    }

    IEnumerator StartCorrectWaySafeToCrossRoutine()
    {
        // You're good to cross
        VoiceoverHandler.Instance.PlayVoiceOver("correct_good_to_cross");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        // Middle Teleport Point Appears
        returnTeleportNodes[0].SetActive(true);
        // Teleport Back

        // Listen for onTeleport On Final One?

        // Wait for Back at Start 
        yield return new WaitUntil(() => endNodeReached);
        // end dialogue x3
        VoiceoverHandler.Instance.PlayVoiceOver("end_speech_1");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        VoiceoverHandler.Instance.PlayVoiceOver("end_speech_2");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        VoiceoverHandler.Instance.PlayVoiceOver("end_speech_3");
        yield return new WaitUntil(() => !SafeSiteAudioManager.Instance.IsVoiceOverPlayingCurrently);

        // Fade to Black
        // End of Scenario 
    }
    private void StartCorrectWayBackAtStart()
    {

    }

    ////////// Public for External Calls / Events
    // Seems bloated....Maybe bring all externally elements in as events and remove any outside wiring...
    public void IntroFinished()
    {
        SetCurrentScenarioState(ScenarioState.WrongWay_TeleportToDrill);
    }

    public void FirstTeleportNodeHit()
    {
        VoiceoverHandler.Instance.PlayVoiceOver("second_teleport");
    }

    public void DrillReached()
    {
        SetCurrentScenarioState(ScenarioState.WrongWay_PickupDrill);
    }

    public void DrillStore()
    {
        SetCurrentScenarioState(ScenarioState.WrongWay_ReturnWithDrill);
    }

    public void AccidentTriggered()
    {
        SetCurrentScenarioState(ScenarioState.WrongWay_CraneAccident);
    }

    public void CustomDebugTrigger()
    {
        Debug.Log("Attached Event Being Called");
    }

    public void CorrectWayResetTrigger()
    {
        SetCurrentScenarioState(ScenarioState.CorrectWay_Reset);
    }

    public void CorrectWayWaveToBanksmanTrigger()
    {
        SetCurrentScenarioState(ScenarioState.CorrectWay_WaveToBanksMan);
    }

    public void EndTrigger()
    {
        endNodeReached = true;
    }
}
