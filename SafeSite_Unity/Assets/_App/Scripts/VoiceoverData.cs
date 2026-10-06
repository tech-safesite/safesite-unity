using UnityEngine;
[CreateAssetMenu(fileName = "SafeSite_VoiceoverData",
    menuName = "SafeSite/VoiceoverData"
    )]

public class VoiceoverData : ScriptableObject
{

    public VoiceoverEntry[] voiceOverEntries;

    public VoiceoverEntry GetEntry(string id)
    {
        foreach(VoiceoverEntry voiceoverEntry in voiceOverEntries) 
        {
          if(voiceoverEntry.id == id )
                return voiceoverEntry;
        }

        Debug.LogWarning(" Dialogue Entry '{id}' not found");
        return null;
    }
}
