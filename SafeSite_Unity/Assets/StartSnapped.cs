using Oculus.Interaction;
using UnityEngine;

public class StartSnapped : MonoBehaviour
{
    [SerializeField] private SnapInteractor snapInteractor;
    [SerializeField] private SnapInteractable startSocket;

    private void Awake()
    {
        snapInteractor.InjectOptionalTimeOutInteractable(startSocket);
        snapInteractor.InjectOptionaTimeOut(0.01f);

        Debug.Log("StartSnapped from " + snapInteractor.name + " running Awake ()");
    }
}