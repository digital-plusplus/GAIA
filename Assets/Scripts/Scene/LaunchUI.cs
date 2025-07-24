using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using imessages;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Main scripts that handles ALL UI elements!
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>

public class LaunchUI : MonoBehaviour, IAIComponent
{
    //Menu button GameObjects 
    [Header("UI Objects")]
    [SerializeField] private Button CamSelectButton;
    [SerializeField] private Button StartButton;
    [SerializeField] private Button SettingsButton;
    [SerializeField] private Button ExitSettingsButton;
    [SerializeField] private Button DebugButton;                //Whether we want to show the debug panel or not
    [SerializeField] private GameObject iGDC;                   //Debug Console

    //Note the talk button is dumb 
    [SerializeField] private Button SkinSettingsButton;
    [SerializeField] private Button VoiceSettingsButton;
    [SerializeField] private Button StageSettingsButton;
    [SerializeField] private Button EyeSettingsButton;
    [SerializeField] private Button LanguageButton;

    //GameObjects needed to control the UI and the NPC
    [Header("UI Reference Objects")]
    [SerializeField] private GameObject StudioBackGround;       //Control the texture of the background
    [SerializeField] private GameObject settingsPanel;          //Settings UI panel
    [SerializeField] private DeviceOrientationManager dOM;      //Link to the Device Orientation Manager component
    [SerializeField] private Image flagImage;                   //Current language flag texture
    [SerializeField] private AI_LanguageManager aiLM;           //Link to the current language

    [Header("NPC Reference Objects")]
    [SerializeField] private GameObject NPC;                    //Control enable/disable NPC after Menu is completed
    [SerializeField] private NPC_Looks npc_Looks;               //Link to control the character selection and call public methods in that component
    
    [HideInInspector] public bool isRecording;

    [Header("Settings Menu")]
    [SerializeField] private float settingsZDisposition;        //Temporary z-value to move the UI out of the way when Settings is closed
    float SettingsMenuOriginalZPosition;                        //placeholder to return the menu in its original position
    private bool isDebugEnabled;                                //For Debugging visibility

    //Start button sizing
    private Vector3 vZero = new Vector3(0, 0, 0);               //use to show/hide the menu
    private Vector3 vOne = new Vector3(0.1f, 0.1f, 0.1f);

    //Debug stuff
    [Header("Debug")]
    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "LAUCHUI: ";

    //MessageBus
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }
    iMessage iM = new iMessage();


    private void SetFlagTexture(string curLang)
    {
        Texture2D loadedTexture = Resources.Load<Texture2D>("Languages/" + curLang);

        if (loadedTexture == null)
            Debug.LogError(DEBUG_PREFIX + "Cannot load flag " + curLang + " check Resources/Languages");
        else
            flagImage.sprite = Sprite.Create(loadedTexture, new Rect(0, 0, loadedTexture.width, loadedTexture.height), new Vector2(0.5f, 0.5f));
    }


    //Only turns the Start button on or off, Start is required for WebGL only
    public void TurnOnOffUI(bool turnOn)
    {
        StartButton.transform.localScale = (turnOn ? vOne : vZero);
    }


    //Turn the NPC on or off during startup
    public void TurnOnOffNPC(bool turnOn)
    {
        NPC.transform.localScale = (turnOn ? Vector3.one : Vector3.zero);
    }


    //Activate/deactivate the camera - depends on whether the selected LLM model supports vision
    public void TurnOnOffCamera(bool turnOn)
    {
        CamSelectButton.interactable = turnOn;
    }


    //Turn on/off the InGameDebugConsole
    private void SetInGameDebugConsole(int newVal)
    {
        if (newVal==0) 
        {
            iGDC.SetActive(false);
            DebugButton.GetComponent<Image>().color = new Color(1f, 1f, 1f, 1f);                                //Button in Settings menu white
        }
        else 
        {
            iGDC.SetActive(true);
            DebugButton.GetComponent<Image>().color = new Color(0.2f, 1f, 0.2f, 1f);                            //Button in Settings menu green
        }
    }


    private void Start()
    {
        TurnOnOffNPC(false);                                                                                    //We start with the NPC disabled to avoid it "flips" during InitPreferences
    }    


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                                                               //Ensure we link to the active bus
        ComponentId = ComponentID.UI_Manager;                                                                   //Make yourself known

        if (debug) Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<UIRequestMessage>(HandleUIRequestAsync);
        await _messageBus.Subscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + " started and subscribed to UIRequestMessage.");

        //The settings moves behind the camera when we click the Settings button
        SettingsMenuOriginalZPosition = settingsPanel.transform.localPosition.z;

        if (debug) Debug.Log(DEBUG_PREFIX + "START");

        //Device Orientation
        if (!dOM)
        {
            Debug.LogError(DEBUG_PREFIX + "DeviceOrientationManager not set, please check Inspector!");         //We need this to handle the device orientation
            return;
        }

        //We now ensure we initialize the orientation variables
        dOM.InitializeDeviceOrientation();

        if (!NPC)
        {
            Debug.LogError(DEBUG_PREFIX + "no NPC configured!, please check Inspector!");
            return;
        }

        //For WebGL we start with a Start button and no NPC to enforce UI interaction => triggers Mic/Camera approval
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            TurnOnOffUI(true);
            NPC.SetActive(false);
        }
        else TurnOnOffUI(false);                                                                                //no need for a Start button for anything other than WebGL

        //Camera selection
        CamSelectButton.onClick.AddListener(async () =>
        {
            await SendMessage<LLMRequestMessage>(MessageCommands.isLLMNextCamera,"");
        });


        //Settings button
        SettingsButton.onClick.AddListener(() =>
        {
            settingsPanel.transform.localPosition = new Vector3(settingsPanel.transform.localPosition.x,        //move the settings in front of the camera
                settingsPanel.transform.localPosition.y, SettingsMenuOriginalZPosition + settingsZDisposition);
        });


        //Changing the skin of the NPC
        SkinSettingsButton.onClick.AddListener(async () =>
        {
            await SendMessage<NPCLooksRequestMessage>(MessageCommands.isNPCNextSkin, "");                       //Tell NPCLooks to switch to next skin
            HideSettingsPanel();
        });


        //GPS BUTTON IS DEPRECATED!


        //Change the Eyes of the NPC
        EyeSettingsButton.onClick.AddListener(async () =>
        {
            await SendMessage<NPCLooksRequestMessage>(MessageCommands.isNPCNextEyes,"");                        //Tell NPCLooks to switch to next Eyes
            HideSettingsPanel();
        });


        //VoiceSettings button
        VoiceSettingsButton.onClick.AddListener(async () =>
        {
            await SendMessage<TTSRequestMessage>(MessageCommands.isTTSNextVoice,"");                            //Tell the TTS component to go to the next available voice
        });


        //StageSettings button
        StageSettingsButton.onClick.AddListener(async () =>
        {
            //change stage, tell the background manager to flip to the next stage and send a prefs update message
            await _messageBus.Publish<PreferenceRequestMessage>( new PreferenceRequestMessage
            {
                Command = MessageCommands.isPreferencesUpdate,
                selectedStage = StudioBackGround.GetComponent<BackGroundManager>().NextBackGround().ToString(),
                SenderId = ComponentId
            });
            HideSettingsPanel();
        });

        //Exit menu, hide it behind the camera again
        ExitSettingsButton.onClick.AddListener(() =>
        {
            HideSettingsPanel();
        });


        //Debug button
        DebugButton.onClick.AddListener(async () =>
        {
            isDebugEnabled = isDebugEnabled ? false : true;                                                     //Toggle!

            await _messageBus.Publish<PreferenceRequestMessage>( new PreferenceRequestMessage                   //Tell prefs manager to update the preferences, this will send a response!
            {
                Command = MessageCommands.isPreferencesUpdate,
                debugOnOff = (isDebugEnabled ? "1" : "0"),                                                      //Convert
                SenderId = ComponentId
            });

            HideSettingsPanel();          
        });


        //Language button clicked
        LanguageButton.onClick.AddListener(async () =>
        {
            await SendMessage<LangRequestMessage>(MessageCommands.isLangNextLang,"");                           //Incurs a preferences update to all components
        });


        //Move the settings behind the camera
        void HideSettingsPanel()
        {
            settingsPanel.transform.localPosition = new Vector3(settingsPanel.transform.localPosition.x,
                settingsPanel.transform.localPosition.y, SettingsMenuOriginalZPosition);
        }
    }


     // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<UIRequestMessage>(HandleUIRequestAsync);
        await _messageBus.Unsubscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }


    //=====================================================================================
    // MESSAGE BUS HANDLERS
    //=====================================================================================

    //Outgoing messages
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        UIResponseMessage message = new UIResponseMessage()
        {
            Command = MessageCommands.isUIResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish(message);
    }


    //We define a generic SendMessage method that can publish multiple Request message types, each must inherit from the RequestMessage template/abstract
    // this massively simplifies the command to send a request message to the message bus!
    public async Task SendMessage<T>(MessageCommands command, string content) where T : class, IContentMessage, new()
    {
        // Creëer een nieuw object van het opgegeven type
        var message = new T
        {
            Command = command,
            Content = content,
            SenderId = ComponentId
        };

        // Publiseer het generieke bericht
        await _messageBus.Publish<T>(message);
    }
    

    //Incoming Request Messages must be analyzed and forwarded to the corresponding method
    private Task HandleUIRequestAsync(UIRequestMessage message)
    {
        //Always log the incoming message
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Split out all the possible commands
        switch (message.Command)
        {
            case MessageCommands.isUIOnOff:             //UI 
                TurnOnOffUI(message.isOn);              // We pass on the value in the message to the method
                break;

            case MessageCommands.isUINPCOnOff:          //NPC
                TurnOnOffNPC(message.isOn);             // We pass on the value in the message to the method        
                break;

            case MessageCommands.isCameraOnOff:         //Camera Icon
                TurnOnOffCamera(message.isOn);          
                break;

            default:
                Debug.LogError(DEBUG_PREFIX + "Error, undefined command received: " + message.Command.ToString());
                break;
        }
        return Task.CompletedTask;
    }


    //Incoming preference changes pushed by Preference Manager
    private Task HandlePrefsResponseMessageAsync(PreferenceResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isPreferencesResponse:
                
                //Initialize/Set the Avatar
                if (!string.IsNullOrEmpty(message.selectedLanguage))
                    SetFlagTexture(message.selectedLanguage);

                //Initialize/Set the Stage
                if (!string.IsNullOrEmpty(message.selectedStage))
                    StudioBackGround.GetComponent<BackGroundManager>().SetBackGround(int.Parse(message.selectedStage));

                //Changed the debug settings
                if(!string.IsNullOrEmpty(message.debugOnOff))
                    SetInGameDebugConsole(int.Parse(message.debugOnOff));
                
                break;
            
            default:
                Debug.LogError(DEBUG_PREFIX + "Error, unknown MessageCommand received!");
                break;
        }
        return Task.CompletedTask;
    }
}
