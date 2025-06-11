using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// Main scripts that handles the UI buttons and toggles during the various stages of signin (client and host)
///     Also detects Left Controller Select+Activate button to (un)hide the UI
///     Debounces UI (un)hiding as well
///     Updated: 20241101 - moved some UI code from ClientServerJoinProtocol.cs
/// </summary>
public class LaunchUI : MonoBehaviour
{
    //Initial signin button
    [SerializeField] private Button CamSelectButton;
    [SerializeField] private Button StartButton;
    [SerializeField] Vector3 ObjectSpawnerPosition; //GRAB
    [SerializeField] public bool isVisible;         //visible by default or not?
    [SerializeField] private GameObject NPC;        //Control enable/disable NPC after Menu is completed

    private Vector3 vZero = new Vector3(0, 0, 0);    //use to show/hide the menu
    private Vector3 vOne = new Vector3(1, 1, 1);


    private void Awake()
    {
       
        //TEMPORARY AVATAR SELECTORS, WILL BE REPLACED WITH FUTURE PIN CODE MECHANISM
        CamSelectButton.onClick.AddListener(() =>
        {
            NPC.GetComponent<AI_Orchestrator>().NextCamera();
        });


        //TEMPORARY AVATAR SELECTORS, WILL BE REPLACED WITH FUTURE PIN CODE MECHANISM
        StartButton.onClick.AddListener(() =>
        {
            //For WebGL it is crucial that the user first interacts with the canvase before the Microphone can be activated
            //-> this is why we activate the microphone here, after Start was pressed
            if (NPC.GetComponent<AI_Orchestrator>().InitializeMicrophone(1))
            {
                TurnOnOffUI(false);
                NPC.SetActive(true);
            }
        });
    }


    public void TurnOnOffUI(bool turnOn)
    {
        StartButton.transform.localScale = (turnOn ? vOne : vZero);
    }


    private void Start()
    {
        //Now we turn off the NPC until the menu-workflow is completed
        if (!NPC) Debug.LogError("LaunchUI: no NPC configured!");
        NPC.SetActive(false);
    }

}
