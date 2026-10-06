using System;
using UnityEngine;
using Unity.Collections;
using System.Collections;

public class SafeSiteAudioManager : MonoBehaviour
{
    // Simple for now. Expand later for ambience, volume settings etc
    public static SafeSiteAudioManager Instance { get; private set; }


    [SerializeField]
    private AudioSource voiceOverAudioSource;

    [SerializeField]
    private AudioSource sFXAudioSource;

    public bool IsVoiceOverPlayingCurrently => voiceOverAudioSource.isPlaying;

    private Coroutine currentVoiceOverRoutine;

    private void Awake()
    {
        if(Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this);
    }


    public void PlayVoiceOver(AudioClip voiceClip, Action onFinish = null) // Can pass event trigger on finish
    {
        if(currentVoiceOverRoutine != null) 
        {
            StopCoroutine(currentVoiceOverRoutine);
        }

        voiceOverAudioSource.Stop();
        voiceOverAudioSource.clip = voiceClip;
        voiceOverAudioSource.Play();

        StartCoroutine(WaitForVoiceToFinish(onFinish));
    }

    public void StopVoiceover()
    {
        voiceOverAudioSource.Stop();
    }

    
    public void PlaySFX(AudioClip clip)
    {
        sFXAudioSource.PlayOneShot(clip);
    }

    private IEnumerator WaitForVoiceToFinish( Action onFinished)
    {
        yield return new WaitWhile(() => voiceOverAudioSource.isPlaying);

        onFinished?.Invoke();
    }



}
