using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using imessages;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Language Manager, returns translated strings identified by a key (string) in the currently selected language
/// Note: the LLM should take care of most translations so this component should only be marginally used
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>

//Available languages for the AI Language Manager
public enum AppLanguage
{
    NL_Dutch,
    GB_English,
    US_English,
    AU_English,
    //Add languages here
}


public class AI_LanguageManager : MonoBehaviour, IAIComponent, ILangService       //note there is no interface for LanguageManagers, we only have one !
{

    private string[] langNames;
    private int curLangId = 0;

    public static AI_LanguageManager Instance { get; private set; }
    [SerializeField] private AppLanguage currentLanguage = AppLanguage.US_English;


    //Dictionaires to hold localized strings and string arrays
    private Dictionary<string, Dictionary<AppLanguage, object>> localizedData = new Dictionary<string, Dictionary<AppLanguage, object>>();

    const string DEBUG_PREFIX = "AI_LANGUAGEMANAGER ";
    [SerializeField] private bool debug;
    iMessage iM = new iMessage();

    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }


    //Here we can get & set the current language, which will trigger the OnLanguageChanged event
    public AppLanguage CurrentLanguage
    {
        get
        {
            return currentLanguage;
        }
        private set
        {
            if (currentLanguage != value)
            {
                currentLanguage = value;
                if (debug)
                    Debug.Log(DEBUG_PREFIX + "Language changed to " + currentLanguage);
            }
        }
    }


    public void InitLanguage(AppLanguage newLanguage)
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + "Set Language to " + newLanguage.ToString());
        CurrentLanguage = newLanguage;
    }


    //(Re)Sets the language id but also tells AI Orchestrator to change the language for TTS and STT components
    public void SetLanguage(string newLanguage)
    {
        for (int i = 0; i < langNames.Length; i++)
        {
            if (langNames[i] == newLanguage)
            {
                curLangId = i;
                currentLanguage = (AppLanguage)i;               //assign the enum value belonging to the current id
                CurrentLanguage = (AppLanguage)i;
            }
        }
        if (debug)
            Debug.Log(DEBUG_PREFIX + "Set Language String to " + newLanguage.ToString() + ", curLangId="+curLangId);
    }


    //Generic method to get localized strings by ID and type
    // This method will return the localized data for the given ID and current language.
    public T GetLocalized<T>(string id)
    {
        // Try to get the localized data for the given ID and current language
        if (localizedData.ContainsKey(id) && localizedData[id].ContainsKey(CurrentLanguage))
        {
            if (localizedData[id][CurrentLanguage] is T typedValue)
            {
                return typedValue;
            }
            else
                Debug.LogError(DEBUG_PREFIX + "Found data for " + id + " has unexpected type, expected "
                    + typeof(T).Name + " but found " + localizedData[id][CurrentLanguage].GetType().Name);
        }

        // Use English as fallback if the current language data is not found
        if (CurrentLanguage != AppLanguage.US_English && localizedData.ContainsKey(id) && localizedData[id].ContainsKey(AppLanguage.US_English))
        {
            if (localizedData[id][AppLanguage.US_English] is T typedValue)
            {
                Debug.LogWarning($"LocalizationManager: Data met ID '{id}' niet gevonden voor taal '{CurrentLanguage}', fallback naar Engels.");
                return typedValue;
            }
            else
                Debug.LogError($"LocalizationManager: Fallback data voor ID '{id}' is van onverwacht type. Verwacht: {typeof(T).Name}, Gevonden: {localizedData[id][AppLanguage.US_English].GetType().Name}");

        }

        Debug.LogWarning(DEBUG_PREFIX + "Data with ID " + id + "not found for language " + CurrentLanguage + ". Using English as fallback");
        return default(T);
    }


    //========================================================================================
    //This is the main function where you define all your localized strings and string arrays.
    // TODO: load from a JSON file & add more languages
    //========================================================================================
    private void LoadLocalizationData()
    {
        //LLM system prompt language response
        AddLocalizedData("responseLanguage", AppLanguage.US_English, "\nRespond in US English");
        AddLocalizedData("responseLanguage", AppLanguage.GB_English, "\nRespond in UK English");
        AddLocalizedData("responseLanguage", AppLanguage.AU_English, "\nRespond in Australian English");
        AddLocalizedData("responseLanguage", AppLanguage.NL_Dutch, "\nAntwoord ALTIJD UITSLUITEND in het Nederlands");

        //Weather degrees
        AddLocalizedData("degrees_celcius", AppLanguage.US_English, "degrees Celcius");
        AddLocalizedData("degrees_celcius", AppLanguage.GB_English, "degrees Celcius");
        AddLocalizedData("degrees_celcius", AppLanguage.AU_English, "degrees Celcius");
        AddLocalizedData("degrees_celcius", AppLanguage.NL_Dutch, "graden Celcius");

        AddLocalizedData("degrees_fahrenheit", AppLanguage.US_English, "degrees Fahrenheit");
        AddLocalizedData("degrees_fahrenheit", AppLanguage.GB_English, "degrees Fahrenheit");
        AddLocalizedData("degrees_fahrenheit", AppLanguage.AU_English, "degrees Fahrenheit");
        AddLocalizedData("degrees_fahrenheit", AppLanguage.NL_Dutch, "graden Fahrenheit");

        if (debug)
            Debug.Log(DEBUG_PREFIX + "Localization Data loaded");
    }


    //helper method to add localized data
    private void AddLocalizedData(string id, AppLanguage language, object data)
    {
        if (!localizedData.ContainsKey(id))
        {
            localizedData.Add(id, new Dictionary<AppLanguage, object>());
        }
        localizedData[id][language] = data;
    }


    //Called by LaunchUI when the next language is selected.
    public async Task NextLanguage()
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + "Next Language called");

        //Determine the next language
        curLangId = (curLangId + 1) % langNames.Length;                                                         //Update the selected lang id
        if (debug)
            Debug.Log(DEBUG_PREFIX + "Next Language " + langNames[curLangId]);

        SetLanguage(langNames[curLangId]);                                                                      //Update the current selected language in the component

        //We now push a preferences update to all whom it concerns
        await _messageBus.Publish<PreferenceRequestMessage>( new PreferenceRequestMessage
        {
            Command = MessageCommands.isPreferencesUpdate,
            selectedLanguage = langNames[curLangId],
            SenderId = ComponentId
        });

        //Audible feedback to the user
        await _messageBus.Publish<TTSRequestMessage>( new TTSRequestMessage
        {
            Command = MessageCommands.isTTSSay,
            Content = langNames[curLangId].Substring(2),
            SenderId = ComponentId
        });
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================

    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.Language_Manager;                      //Make yourself known

        Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<LangRequestMessage>(HandleLangRequestAsync);
        await _messageBus.Subscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + " started and subscribed to LangRequestMessage.");

        if (Instance == null)                                   //Only initialize if no other instance exists
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadLocalizationData();
            InitLanguage(AppLanguage.US_English);               //We always start with US Eng as default in case there are no prefs set
            langNames = Enum.GetNames(typeof(AppLanguage));
            if (debug)
                Debug.Log(DEBUG_PREFIX + langNames.Length + " languages loaded");
        }
        else
            Destroy(gameObject);
    }

    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<LangRequestMessage>(HandleLangRequestAsync);
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
        LangResponseMessage message = new LangResponseMessage()
        {
            Command = MessageCommands.isLangResponse,
            Content = content,
            SenderId = ComponentId,
            IsError = isError,
            TargetId = requestorId                                              //return to Request sender

        };
        await _messageBus.Publish<LangResponseMessage>(message);
    }


    private async Task HandleLangRequestAsync(LangRequestMessage message)
    {
        if (debug)
            iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isLangGetLocalized:
                string result = GetLocalized<string>(message.Content);          //Here we call the main method to get our data
                var reply = new LangResponseMessage
                {
                    Command = MessageCommands.isLangResponse,                   //Prepare the response message
                    Content = result,                                           //We send back the result in the Content 
                    IsError = false,
                    SenderId = ComponentId,                                     //Don't forget to return who sent this!
                    TargetId = message.SenderId,                                //Return to Request sender
                    key = message.Content                                       //The dictionary key we asked for, needed to store in the remote dictionary
                };
                await _messageBus.Publish<LangResponseMessage>(reply);          //Send the response async
                break;

            case MessageCommands.isLangNextLang:                                //Incurred by LaunchUI button event
                await NextLanguage();                                           //We call nextLanguage which in its turn pushes a prefs update
                break;

            default:
                Debug.LogWarning(DEBUG_PREFIX + "Received " + message.Command.ToString() + " - illegal command!");
                break;
        }
        await Task.Delay(0);    //keep the compiler quiet
        return;
    }


    //============================
    //Preference Manager Responses
    //============================
    private Task HandlePrefsResponseMessageAsync(PreferenceResponseMessage message) 
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content, message.TargetId);

        switch (message.Command) 
        {
            case MessageCommands.isPreferencesResponse:
                if (!string.IsNullOrEmpty(message.selectedLanguage))
                    SetLanguage(message.selectedLanguage);
                break;    
            
            default:
                Debug.LogError(DEBUG_PREFIX + "Error: unknown MessageCommand " + message.Command.ToString());
                break;
        }

        return Task.CompletedTask;
    }
}

