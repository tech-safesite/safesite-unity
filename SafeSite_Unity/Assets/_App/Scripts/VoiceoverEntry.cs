using UnityEngine;

[System.Serializable]
public class VoiceoverEntry
{
    public string id;


    [Header("English")]
    [TextArea(2, 5)]
    public string englishSubtitles;
    public AudioClip englishAudio;

    [Header("Romanian")]
    [TextArea(2,5)]
    public string romanianSubtitles;
    public AudioClip romanianAudio;
}

