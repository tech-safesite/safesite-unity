using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitleHandler : MonoBehaviour
{

    public static SubtitleHandler Instance;

    [SerializeField]
    private TMP_Text subtitleMain;
    [SerializeField]
    private GameObject subtitleShowHideRoot;

    [SerializeField]
    private float subtitleEndBufferTime = 1f;

    private Coroutine activeSubtitleRoutine;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
            
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ShowSubtitle(string subtitleText, float subtitleDuration)
    {
        if(activeSubtitleRoutine != null) 
        {
            StopCoroutine(activeSubtitleRoutine);
        }

        activeSubtitleRoutine = StartCoroutine(ShowSubtitleRoutine(subtitleText, subtitleDuration));   
    }

    private IEnumerator ShowSubtitleRoutine(string text, float subtitleDuration)
    {
        subtitleMain.text = text;
        subtitleShowHideRoot.SetActive(true);

        yield return  new WaitForSeconds(subtitleDuration + subtitleEndBufferTime);

        subtitleShowHideRoot.SetActive(false);
        activeSubtitleRoutine = null;
    }

    public void HideSubtitle()
    {
        if(activeSubtitleRoutine !=null)
        {
            StopCoroutine (activeSubtitleRoutine);
            activeSubtitleRoutine = null;
        }
        subtitleShowHideRoot.SetActive(false);
    }
}
