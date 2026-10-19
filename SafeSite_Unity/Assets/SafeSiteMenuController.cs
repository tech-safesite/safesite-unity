using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SafeSiteMenuController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    [SerializeField]
    private Transform playerStartingTransform;

    [SerializeField]
    private GameObject[] menus;


    [SerializeField]
    private Image loadingBar;
    [SerializeField]
    private float fakeLoadDuration;

    [SerializeField]
    private AnimationCurve progressCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [SerializeField]
    private float noiseAmount = 0.01f;
    [SerializeField]
    private float noiseSpeed = 4f;

    void Start()
    {
        FindAnyObjectByType<XRPlayerPositioner>().MovePlayerTo(playerStartingTransform);
        ShowLoadingMenu();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ShowScenarioSelectionMenu()
    {
        HideAllMenus();
        menus[1].SetActive(true);
    }

    public void ShowLoadingMenu()
    {
        HideAllMenus();
        menus[0].SetActive(true);
        StartCoroutine(StartLoadingBar());

    }

    public void ShowScenarioSetupMenu()
    {
        HideAllMenus();
        menus[2].SetActive(true);
    }

    private void HideAllMenus()
    {
        foreach (GameObject go in menus) 
        {
            go.SetActive(false);
        }
    }

    private IEnumerator StartLoadingBar() 
    
    {
        float timeElapsed = 0f;
        
        while(timeElapsed < fakeLoadDuration) 
        
        {
            float noise = (Mathf.PerlinNoise (Time.time * noiseSpeed,0f) -0.5f);

            timeElapsed += Time.deltaTime;

            float t = (Mathf.Clamp01(timeElapsed / fakeLoadDuration));

            float curvedProgress = progressCurve.Evaluate(t);

            loadingBar.fillAmount = Mathf.Clamp01(curvedProgress + noise);

            yield return null;
        }
                
        loadingBar.fillAmount = 1f;

        yield return new WaitForSeconds(.5f);

        ShowScenarioSelectionMenu();
    }
}
