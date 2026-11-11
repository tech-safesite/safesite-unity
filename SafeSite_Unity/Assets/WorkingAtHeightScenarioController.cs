using SafeSite.Core.Audio;
using SafeSite.Core.UI;
using SafeSite.Core.Voiceover;
using System.Collections;
using UnityEngine;

public class WorkingAtHeightScenarioController : MonoBehaviour
{
    [Header("Voiceover Dataset")]
    [SerializeField]
    private VoiceoverData workingAtHeightVoiceoverData;

    [SerializeField]
    private Transform playerRig;
    [SerializeField]
    private GameObject rigLocomotor;

    [Header("Teleportation Nodes")]
    [SerializeField]
    GameObject[] teleportNodes;

    [SerializeField]
    GameObject[] ladders; 

    [Header("Teleportation Nodes")]
    [SerializeField]
    private GameObject [] nails;

    [SerializeField]
    private Animator accidentLadderAnimator;

    [SerializeField]
    private GameObject accidentAudioGroup;

    [SerializeField]
    private float slowMotionTimescale = 35f;

    private bool ladderTeleportNodeHit = false;
    private bool ladderPickedUp = false;
    private bool ladderPlaced = false;
    private int nailsCompleted = 0;

    private ScenarioState currentState;
    public enum ScenarioState
    {
        Intro,
        LadderSetup,
        WorkingAtHeight,
        Accident,
        Consequence,
        WrongWayComplete
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        VoiceoverHandler.Instance.SetVoiceOverData(workingAtHeightVoiceoverData);

        SetState(ScenarioState.Intro);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void SetState (ScenarioState state)
    {
        currentState = state;
        switch (state) 
        {
            case ScenarioState.Intro:
                StartCoroutine(BeginIntroSequence());
                break;

            case ScenarioState.LadderSetup:
                StartCoroutine(LadderSetupSequence());
                break;

            case ScenarioState.WorkingAtHeight:
                StartCoroutine(WorkingAtHeightSequence());
                break;

            case ScenarioState.Accident:
                StartCoroutine(BeginAccidentSequence());
                break;
        }
    }

    private IEnumerator BeginIntroSequence()
    {
        ScreenFadeController.Instance.FadeOutOfBlack(3); // 
        yield return VoiceoverHandler.Instance.PlayAndWait("ladder_intro");
        teleportNodes[0].SetActive(true);

        TriggerLadderSetupSequence();
    }

    private IEnumerator LadderSetupSequence()
    {
        yield return VoiceoverHandler.Instance.PlayAndWait("ladder_teleport");

        yield return new WaitUntil(() => ladderTeleportNodeHit);

        yield return VoiceoverHandler.Instance.PlayAndWait("ladder_pick_up_ladder");
        teleportNodes[0].SetActive(true);

        yield return new WaitUntil(() => ladderPickedUp);

        yield return VoiceoverHandler.Instance.PlayAndWait("ladder_let_go");

        yield return new WaitUntil(() => ladderPlaced);
        yield return new WaitForSeconds(.5f); // allow ladder to snap
        SwapToScriptedLadder();

        yield return VoiceoverHandler.Instance.PlayAndWait("ladder_now_climb");

        teleportNodes[0].SetActive(false);
        teleportNodes[1].SetActive(true);
    }

    private IEnumerator WorkingAtHeightSequence()
    {
        teleportNodes[1].SetActive(false); // Temp until climbing decided 

        yield return VoiceoverHandler.Instance.PlayAndWait("ladder_take_hammer_out");
        //yield return VoiceoverHandler.Instance.PlayAndWait("ladder_fingers_touch");

        yield return new WaitUntil(() => (nailsCompleted ==2));

        // Activate third nail 
        nails[2].SetActive(true);

        yield return new WaitUntil(() => (nailsCompleted == 3));
        SetState(ScenarioState.Accident);

    }

    private IEnumerator BeginAccidentSequence()
    {
        yield return null;

        // MoveOVRRig As child of Ladder Animation 
        playerRig.transform.transform.parent = ladders[1].transform;
        rigLocomotor.gameObject.SetActive(false);

        accidentLadderAnimator.SetTrigger("LadderFall");

        Time.timeScale = slowMotionTimescale;
        yield return new WaitForSecondsRealtime(5f);
        Time.timeScale = 1;
        // Trigger Ladder Animation 
        yield return new WaitForSecondsRealtime(2f);


        ScreenFadeController.Instance.FadeToBlack(0.2f);

        // Impact audio / crash 
        accidentAudioGroup.SetActive(true);

        playerRig.transform.transform.parent = null;

        // hold black for 2.5 seconds
        // play voiceover broken back

        // wait another 2 seconds 

        // reset wrong way scenario 
    }

    private void SwapToScriptedLadder() // less confusing to just use seperate named GOs?
    {
        ladders[1].transform.SetPositionAndRotation(ladders[0].transform.position, ladders[0].transform.rotation);
        ladders[0].SetActive(false);
        ladders[1].SetActive(true);
    }

    // EXTERNAL / PUBLIC  TRIGGERS 

    public void TriggerIntroSequence ()
    {
        SetState(ScenarioState.Intro);
    }

    public void TriggerLadderSetupSequence()
    {
        SetState(ScenarioState.LadderSetup);
    }

    public void TriggerWorkingAtHeightSequence()
    {
        SetState(ScenarioState.WorkingAtHeight);
    }

    public void TriggerAccidentSequence()
    {
        SetState((ScenarioState)ScenarioState.Accident);
    }

    public void LadderTeleportNodeReached()
    {
        ladderTeleportNodeHit = true;
    }

    public void LadderPlaced()
    {
        ladderPlaced = true;
    }

    public void LadderPickup()
    {
        ladderPickedUp = true;
    }

    public void NailComplete()
    {
        nailsCompleted++;
    }
}
