
using UnityEngine;
using imessages;
using System.Threading.Tasks;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Loads and saves preferences and provides Get/Set methods
///  SOLELY for setting/reading key store, contains no control code!
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>

public class PreferencesManager : MonoBehaviour, IAIComponent
{
    //Preference names
    private const string KEY_CHARACTER = "CharacterName";
    private const string KEY_VOICE = "VoiceName";
    private const string KEY_STAGE = "StageId";
    private const string KEY_USERSNAME = "UsersName";
    private const string KEY_SKINNAME = "SkinType";
    private const string KEY_EYESNAME = "EyeColor";
    private const string KEY_LANGUAGENAME = "SelectedLanguage";
    private const string KEY_DEBUG = "DebugMode";
    
    //Variables that store the preferences, make sure to mirror this in iMessages!
    private string  _selectedCharacterName;      //from UI
    private string  _selectedVoiceName;          //from UI
    private int     _selectedStageId;            //from UI
    private string  _usersName;                  //from STT
    private string  _selectedSkinName;           //From UI
    private string  _selectedEyesName;           //From UI
    private string  _selectedLanguageName;       //From UI
    private int     _selectedDebugOnOff;         //From UI


    //Debug
    private const string DEBUG_PREFIX = "PREFERENCE MANAGER: ";
    [SerializeField] bool debug;

    //MessageBus
    iMessage iM = new iMessage();
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }


    //Here we actually load the preferences from the UNity preferences store, if available
    public void LoadPreferences()
    {
        //Visuals
        _selectedCharacterName = PlayerPrefs.GetString(KEY_CHARACTER, "GAIA");      //Selected avatar 
        _selectedSkinName = PlayerPrefs.GetString(KEY_SKINNAME, "Nordic");
        _selectedEyesName = PlayerPrefs.GetString(KEY_EYESNAME, "Brown");
        
        //Speech
        _selectedVoiceName = PlayerPrefs.GetString(KEY_VOICE, "carly");             //Assumes using Speechify! NOTE CAPTION!
        
        //Stage
        _selectedStageId = PlayerPrefs.GetInt(KEY_STAGE, 0);  

        //LLM
        _selectedDebugOnOff = PlayerPrefs.GetInt(KEY_DEBUG, 0);                     //0=off, 1=on
        _selectedLanguageName = PlayerPrefs.GetString(KEY_LANGUAGENAME, "US_English");
        _usersName = PlayerPrefs.GetString(KEY_USERSNAME, "none");
    
        if (debug)
            Debug.Log(DEBUG_PREFIX + _selectedCharacterName + ", " + _selectedVoiceName + ", " + _selectedStageId + ", " + _usersName
                + ", " + _selectedSkinName + ", " + _selectedEyesName 
                + ", " + _selectedLanguageName + ", " + (_selectedDebugOnOff == 0 ? "Debug off" : "Debug on"));
        Debug.Log(DEBUG_PREFIX + "Prefs ready event!");
    }


    //This stores all preferences 
    public void SavePreferences()
    {
        PlayerPrefs.SetString(KEY_CHARACTER, _selectedCharacterName);
        PlayerPrefs.SetString(KEY_VOICE, _selectedVoiceName);
        PlayerPrefs.SetInt(KEY_STAGE, _selectedStageId);
        PlayerPrefs.SetString(KEY_USERSNAME, _usersName);
        PlayerPrefs.SetString(KEY_SKINNAME, _selectedSkinName);
        PlayerPrefs.SetString(KEY_EYESNAME, _selectedEyesName);
        PlayerPrefs.SetString(KEY_LANGUAGENAME, _selectedLanguageName);
        PlayerPrefs.SetInt(KEY_DEBUG, _selectedDebugOnOff);
        PlayerPrefs.Save();

        if (debug)
            Debug.Log(DEBUG_PREFIX + "Preferences Saved, stageID=" + _selectedStageId);
    }
    

    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                           //Ensure we link to the active bus
        ComponentId = ComponentID.Preferences_Manager;                      //Make yourself known

        if (debug) Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<PreferenceRequestMessage>(HandlePrefsRequestAsync);
        
        LoadPreferences();

        Debug.Log(DEBUG_PREFIX + " started, loaded preferences and subscribed to PreferencesRequestMessage.");
    }

    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<PreferenceRequestMessage>(HandlePrefsRequestAsync);
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
        PreferenceResponseMessage message = new PreferenceResponseMessage()
        {
            Command = MessageCommands.isPreferencesResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish(message);
    }


    public async Task RespondMessage(string character, string skin, string eyes, string debug, string userName, 
                                     string lang, string voice, string stage, MessageCommands origMC, ComponentID requestorId, bool isError)
    {
        PreferenceResponseMessage message = new PreferenceResponseMessage()
        {
            Command = MessageCommands.isPreferencesResponse,
            SenderId = ComponentId,
            TargetId = requestorId,
            originatingMessageCommand = origMC,
            IsError = false,
            character = character,
            skinName = skin,
            eyesName = eyes,
            debugOnOff = debug,
            userName = userName,
            selectedLanguage = lang,
            selectedVoice = voice,
            selectedStage = stage,
        };
        await _messageBus.Publish(message);
    }
    

    private async Task HandlePrefsRequestAsync(PreferenceRequestMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        switch (message.Command)
        {
            //We get the preferences from the store if they are not yet initialized and broadcast them over the MessageBus
            case MessageCommands.isInitPreferences:       

                if (string.IsNullOrEmpty(_selectedCharacterName))   
                    LoadPreferences();

                await RespondMessage(_selectedCharacterName, _selectedSkinName, _selectedEyesName, _selectedDebugOnOff.ToString(), 
                                    _usersName, _selectedLanguageName, _selectedVoiceName, _selectedStageId.ToString(), message.Command, ComponentID.Broadcast, false);       
                break; 
            
            
            //Update received, broadcast ALL prefs to everyone again ===>>> MJUST CHECK WHETHER VALUE CHANGED TO AVOID MESSAGE STORM!
            case MessageCommands.isPreferencesUpdate:       
                _selectedCharacterName  = string.IsNullOrEmpty(message.character)   ? _selectedCharacterName    : message.character;
                _selectedSkinName       = string.IsNullOrEmpty(message.skinName)    ? _selectedSkinName         : message.skinName;  
                _selectedEyesName       = string.IsNullOrEmpty(message.eyesName)    ? _selectedEyesName         : message.eyesName;
                _selectedDebugOnOff     = string.IsNullOrEmpty(message.debugOnOff)  ? _selectedDebugOnOff       : int.Parse(message.debugOnOff);          //convert to Int
                _usersName              = string.IsNullOrEmpty(message.userName)    ? _usersName                : message.userName;
                _selectedLanguageName   = string.IsNullOrEmpty(message.selectedLanguage) ? _selectedLanguageName: message.selectedLanguage;
                _selectedVoiceName      = string.IsNullOrEmpty(message.selectedVoice) ? _selectedVoiceName      : message.selectedVoice;
                _selectedStageId        = string.IsNullOrEmpty(message.selectedStage) ? _selectedStageId        : int.Parse(message.selectedStage);     //convert to Int
                SavePreferences();
                
                await RespondMessage(message.character, message.skinName, message.eyesName, message.debugOnOff, message.userName, message.selectedLanguage,
                                        message.selectedVoice, message.selectedStage, message.Command, ComponentID.Broadcast, false); 
                break;

            default:
                Debug.LogError(DEBUG_PREFIX+ "Unknown message command received!");
                break;
        }
    }

}
