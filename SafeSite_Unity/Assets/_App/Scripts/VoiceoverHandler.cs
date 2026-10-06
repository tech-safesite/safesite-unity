using UnityEngine;

public class VoiceoverHandler : MonoBehaviour
{
   public static VoiceoverHandler Instance { get; private set; } // Currently just English. Update for Romanian Later

    [SerializeField]
    private VoiceoverData voiceoverData;

    private void Awake()
    {
        if(Instance != null && Instance !=this) 
        {
            Destroy(gameObject);
                return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
    }



    public void SetVoiceOverData(VoiceoverData voiceoverDataToSet)
    {
        voiceoverData = voiceoverDataToSet;
    }

    public void PlayVoiceOver(string id)
    {
        if(voiceoverData == null) 
        {
            Debug.LogWarning(" No voice over data has been assigned to Dialogue Handler.");
        }

        VoiceoverEntry entry = voiceoverData.GetEntry(id);

        if(entry == null)
        {
            Debug.LogWarning("No Entry Found Matching Voiceover ID");
            return;
        }

        SubtitleHandler.Instance.ShowSubtitle(entry.englishSubtitles, entry.englishAudio.length);

        if(entry.englishAudio !=null)
        {
            SafeSiteAudioManager.Instance.PlayVoiceOver(entry.englishAudio);
        }
    }

}
