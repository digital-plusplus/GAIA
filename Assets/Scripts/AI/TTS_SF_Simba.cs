using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Linq;
using imessages;
using System.Collections.Generic;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Speech To Text service using Speechify
///  MessageBus:    YES
///  Tool-Calling:  NO, called when LLM response is received that is not a function call
/// </summary>


public class TTS_SF_Simba : MonoBehaviour, ITtsService, IAIComponent
{
    //Variables
    private string SPEECHIFY_API_KEY;

    //Speechify voices are language-specific, so we use a country code to filter the voices
    private enum SelectVoice                                                        //reduced to female voices only for GAIA
    {
        US_carly, NL_lotte, US_kristy, US_tasha, US_lisa,                  //The default is US_carly!
        US_emily, AU_kim, NL_lieke, NL_maud,
        GB_carol, GB_helen, US_julie, AU_linda, GB_beverly,
        US_erin, US_lindsey, US_monica, US_stacy, GB_harper,
        US_evelyn, US_victoria
    }

    string[] voiceNames;                //Array is easier to manage
    private int currentVoiceId;         //For rotating voice selection in Settings menu

    private enum SelectModel
    {
        _base, _english, _multilingual, _turbo
    }

    [SerializeField]
    private SelectVoice selectVoice;

    [SerializeField]
    private SelectModel selectModel;

    private string selectedLanguage = null;

    const string TTS_API_URI = "https://api.sws.speechify.com/v1/audio/stream";      //POST URI, streaming API
    private string sfVoice;
    private string sfModel;
    private Queue<string> speechQueue = new Queue<string>();                        //Avoid ongoing speech cutting out
    private Coroutine processingCoroutine = null;                                   //not null while there are things to be spoken out 

    API_Keys api_Keys;

    [SerializeField] private bool debug;
    iMessage iM = new iMessage();
    const string DEBUG_PREFIX = "TTS_SF_SIMBA: ";

    private bool ttsReady = false;

    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }


    //This call resets the available voices and sets the first available voice as the default.
    // called when a new language is selected from the UI, called by AI Orchestrator
    // Mandatory method for all TTS components
    public void SelectLanguage(string langID)
    {
        voiceNames = null;
        voiceNames = GetVoicesByCountry(langID);                                    //Creates a new array with voices for the selected country only
        currentVoiceId = 0;                                                         //New language so we reset the voice id to the first one for that country
        sfVoice = voiceNames[currentVoiceId].Substring(3);                          //Select the first voice
        selectedLanguage = langID.ToLower();

        Debug.Log(DEBUG_PREFIX + "Selected language " + selectedLanguage);
    }


    //Returns a string array of available voices for the country
    private string[] GetVoicesByCountry(string countryCode)
    {
        string searchPrefix = countryCode.ToUpper() + "_";

        // Gebruik LINQ om de enum-waarden te filteren en om te zetten
        string[] filteredVoices = Enum.GetValues(typeof(SelectVoice))               
                                    .Cast<SelectVoice>()                            
                                    .Select(v => v.ToString())                      
                                    .Where(name => name.StartsWith(searchPrefix))   
                                    .ToArray();                                     

        if (debug)
            Debug.Log(DEBUG_PREFIX + "I found " + filteredVoices.Length + " voices for language " + countryCode);

        return filteredVoices;
    }


    //Retrieve the voice sequence nr in the voiceNames array
    private int GetVoiceId(string voice)
    {
        int rtnValue = -1;
        int cnt = 0;

        foreach (var voiceName in voiceNames)
        {
            if (voiceName.ToString().Substring(3) == voice)
            {
                rtnValue = cnt;
                break;
            }
            cnt++;
        }
        return rtnValue;
    }


    //Generic public method that all TTS components must implement to switch to the next voice
    public string NextVoice()
    {
        currentVoiceId = (currentVoiceId + 1) % voiceNames.Length;      //rotate voices
        sfVoice = voiceNames[currentVoiceId].Substring(3);
        if (debug)
            Debug.Log(DEBUG_PREFIX + "NextVoice=" + sfVoice);
        _=Say(voiceNames[currentVoiceId].Substring(2));                 //Audible feedback for the selected voice
        return sfVoice;                                                 //Return new voice for the preference manager                      
    }


    //Used for initial preferences loading
    public void SetVoice(string selectedVoiceName)
    {
        currentVoiceId = GetVoiceId(selectedVoiceName);
        if (debug)
            Debug.Log(DEBUG_PREFIX + "current voice = " + selectedVoiceName + ", currentVoiceId=" + currentVoiceId);
        sfVoice = selectedVoiceName;
    }


    //used by AI Orchestrator to get the initial voice name when the preferences are loaded for the first time (default voice)
    public string GetCurrentVoice()
    {
        return sfVoice;                                                  //Return the current voice name
    }


    //Called once when a new message arrives, kickstarts the queue processing
    private void Update()
    {
        if (speechQueue.Count > 0 && processingCoroutine == null)
            processingCoroutine = StartCoroutine(ProcessCoroutine());
    }


    private IEnumerator WaitForTask(Task task)
    {
        while (!task.IsCompleted)
            yield return null;
        if (task.IsFaulted)
            Debug.LogError(DEBUG_PREFIX + "TTS Task resulted in a fault:" + task.Exception);          
    }
    

    //Runs as long as there are messages in the speech queue!
    private IEnumerator ProcessCoroutine()
    {
        while (speechQueue.Count > 0)
        {
            string tTS = speechQueue.Dequeue();
            Task sayTask = Say(tTS);
            yield return StartCoroutine(WaitForTask(sayTask));
        }
        processingCoroutine = null;    

        //Confrm we're done talking so the LLM can enable the PTT button again
        _=RespondMessage("TTS Ready", ComponentID.Broadcast, false);                                          
    }


    //Generic public method that all TTS components must implement to say something
    public async Task Say(string textInput)
    {
        if (!ttsReady) return;                                           //await Init
        await PlayTTS(textInput);
    }


    public static async Task WaitUntil(Func<bool> condition)
    {
        while (!condition())
        {
            await Task.Yield(); // Wacht op de volgende frame
        }
    }


    private async Task PlayTTS(string mesg)
    {
        //JSON
        TextToSpeechData ttsData = new TextToSpeechData();
        ttsData.input = SimpleCleanText(mesg);
        ttsData.voice_id = sfVoice;
        ttsData.model = sfModel;
        ttsData.language = selectedLanguage;
        string jsonPrompt = JsonUtility.ToJson(ttsData);

        UnityWebRequest request = new UnityWebRequest(TTS_API_URI, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonPrompt));
        request.downloadHandler = new DownloadHandlerAudioClip(TTS_API_URI, AudioType.MPEG);

        //Headers
        request.SetRequestHeader("content-type", "application/json");
        request.SetRequestHeader("accept", "audio/mpeg");
        request.SetRequestHeader("Authorization", SPEECHIFY_API_KEY);

        if (debug) Debug.Log(DEBUG_PREFIX + jsonPrompt);

        await request.SendWebRequest();

        if (debug) Debug.Log(DEBUG_PREFIX + "received response from TTS API");
        if (request.result == UnityWebRequest.Result.Success)
        {       
            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);      

            //The below replaces PlayOneShot(), used for WebGL compatibility
            GetComponent<AudioSource>().clip = clip;
            GetComponent<AudioSource>().loop = false;
            GetComponent<AudioSource>().Play();

            await WaitUntil(() => !GetComponent<AudioSource>().isPlaying);      //wait until we're done talking
            GetComponent<AudioSource>().clip = null;
            GetComponent<AudioSource>().Stop();
        }
        else
            Debug.LogError(DEBUG_PREFIX + "TTS API Request failed: " + request.error);

        request.Dispose();
    }
    

    //JSON Support Classes
    [Serializable]
    public class TextToSpeechData
    {
        public string input;
        public string voice_id;
        public string model;
        public string language;
    }


    //TODO: MOVE THIS TO A SEPARATE POST LLM TEXT FILTER CLASS
    string SimpleCleanText(string msg)     //just a barebone filter 
    {
        string result = "";

        for (int i = 0; i < msg.Length; i++)
        {
            switch (msg[i])
            {
                case '+':
                    result += " plus ";
                    break;
                case ':':
                    result += ", ";
                    break;
                case '*':
                    result += ", ";
                    break;
                case '=':
                    result += " equals ";
                    break;
                case '-':
                    result += " ";
                    break;
                case '#':
                    result += " hash ";
                    break;
                case '&':
                    result += " and ";
                    break;
                case '\n':
                    result += "       ";
                    break;
                case '°':
                    if ((i < msg.Length - 1) && (msg[i + 1] == 'C'))
                        result += AI_LanguageManager.Instance.GetLocalized<string>("degrees_celcius");
                    else if ((i < msg.Length - 1) && (msg[i + 1] == 'F'))
                        result += AI_LanguageManager.Instance.GetLocalized<string>("degrees_fahrenheit");
                    i++;
                    break;
                default:
                    result += msg[i];       //simply pass on everything else
                    break;
            }
        }
        return result;
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================

    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.TTS_Service;                      //Make yourself known

        Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<TTSRequestMessage>(HandleTTSRequestAsync);
        await _messageBus.Subscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + " started and subscribed to TTSRequestMessage.");


        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else SPEECHIFY_API_KEY = api_Keys.GetAPIKey("Speechify_API_Key");

        if (SPEECHIFY_API_KEY == null)
            Debug.LogWarning(DEBUG_PREFIX + "Warning: TTS API key is empty, check Inspector!");

        voiceNames = Enum.GetNames(typeof(SelectVoice));                            //names in an array of strings, easier to manage
        sfVoice = selectVoice.ToString().Substring(3);
        currentVoiceId = GetVoiceId(sfVoice);                                       //initial selected voice, will be overwritten when the preferences start

        sfModel = "simba-" + selectModel.ToString().Substring(1);
        if (debug)
            Debug.Log(DEBUG_PREFIX + "You have selected voice " + sfVoice + " and model " + sfModel);

        ttsReady = true;

        await Task.Delay(0);    //dummy for now
    }


    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<TTSRequestMessage>(HandleTTSRequestAsync);
        await _messageBus.Unsubscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        //Cleanup

        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }


    //=====================================================================================
    // MESSAGE BUS HANDLERS
    //=====================================================================================

    //Outgoing messages
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        TTSResponseMessage message = new TTSResponseMessage()
        {
            Command = MessageCommands.isTTSResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<TTSResponseMessage>(message);
    }

    public async Task RespondMessage(MessageCommands originatingMessageCommand, string content, ComponentID requestorId, bool isError)
    {
        TTSResponseMessage message = new TTSResponseMessage()
        {
            Command = MessageCommands.isTTSResponse,
            originatingMessageCommand = originatingMessageCommand,          //to detect which Request this is a Response to 
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<TTSResponseMessage>(message);
    }

    
    private async Task HandleTTSRequestAsync(TTSRequestMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isTTSSay:
                if (!string.IsNullOrEmpty(message.Content))
                    speechQueue.Enqueue(message.Content);                                   //Use a queue to avoid audio cutouts
                break;
            
            case MessageCommands.isTTSSelectLanguage:
                SelectLanguage(message.Content);                                            //Call select lang method with content parameter
                break;

            case MessageCommands.isTTSNextVoice:
                NextVoice();                                                                //Action
                break;

            case MessageCommands.isTTSSetVoice:
                SetVoice(message.Content);                                                  //Action
                break;

            //We received nonsense.
            default:
                await RespondMessage(message.Command, "Unknown message type", message.SenderId, true);
                break;
        }
    }


    //Incoming preference changes pushed by Preference Manager
    private Task HandlePrefsResponseMessageAsync(PreferenceResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isPreferencesResponse:
                
                //Language and voice setting
                if (!string.IsNullOrEmpty(message.selectedLanguage)) 
                {
                    SelectLanguage(message.selectedLanguage.Substring(0,2));        //In case we get a new language, we must reset the selected voice to the first, done by SelectLanguage()!
                    break;
                }
                
                if (!string.IsNullOrEmpty(message.selectedVoice))
                    SetVoice(message.selectedVoice);
                break;
            
            default:
                Debug.LogError(DEBUG_PREFIX + "Error, unknown MessageCommand received!");
                break;
        }
        return Task.CompletedTask;
    }
}
