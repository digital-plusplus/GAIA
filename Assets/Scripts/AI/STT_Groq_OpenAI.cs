using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Threading.Tasks;
using imessages;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Speech To Text service using GroqCloud - NON-streaming!
///  MessageBus:    YES
///  Tool-Calling:  NO, driven by PTT button
/// </summary>


public class STT_Groq_OpenAI : MonoBehaviour, ISttService, IAIComponent
{
    private string GROQ_API_KEY;
    const string GROQ_API_URI = "https://api.groq.com/openai/v1/audio/transcriptions";      //POST URI

    private enum STTModel { whisper_large_v3, whisper_large_v3_turbo, distil_whisper_large_v3_en };
    private enum STTLang { en, nl };

    [SerializeField] private STTModel selectedModel;    //What model did we select
    string selectedSTTString;

    [SerializeField] private STTLang selectedLanguage;
    string selectedSTTLang;

    AI_WAV wavObject;                                   //Object that holds stream and methods for WAV
    API_Keys api_Keys;

    NPCClickHandler npcClickHandler;                    //Link to component that captures click & release events 
    private bool isRecording;
    AudioSource aud;
    private AudioClip clip;
    bool processing;                                    //To block for new requests whilst we're processing

    [SerializeField] bool debug;                        //Debug information
    iMessage iM = new iMessage();

    private bool micInitialized = false;                //Checking whether the microphone was initialized, required for WebGL
    const string DEBUG_PREFIX = "STT_GROQ: ";

    private bool sttReady = false;                      //REMOVE

    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }


    public void SelectLanguage(string langCode)
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + " Setting STT Language to " + langCode);

        switch (langCode.ToLower().Substring(0,2))
        {
            case "nl":
                selectedLanguage = STTLang.nl;
                break;

            default:    //English is the default
                if (debug) Debug.Log(DEBUG_PREFIX + "Using English as default language for STT");
                selectedLanguage = STTLang.en;
                break;
        }
        selectedSTTLang = selectedLanguage.ToString();
    }


    //Initial kick of the microphone to enforce the Microphone approval in WebGL on browsers!
    //Called by: AI_Orchestrator, called by LaunchUI
    public bool InitializeMicrophone(int seconds)
    {
        if (micInitialized) return true;

        if (debug)
            Debug.Log("STT: Initializing Microphone");

        AudioClip tmpClip = Microphone.Start("", false, seconds, 11025);
        if (tmpClip)
        {
            micInitialized = true;
            if (debug)
                Debug.Log("STT: Microphone initialized");
            StopSpeaking();     //close the mic
            return true;
        }
        return false;
    }


    //=========================================================================
    //Event handlers initiate the AI Conversation
    //=========================================================================
    private void StopSpeaking()
    {
        if (debug)
            Debug.Log("STT: StopSpeaking called");
        clip = null;
        Microphone.End(null);
    }


    private async void Update()
    {
        if (!sttReady) return;                                    //Avoid we run before Init is completed

        //Start talking event - works for WebGL as well
        if ((isRecording) && (!clip))
        {
            Microphone.End(null);                                 //Just to be sure we close this mic
            clip = Microphone.Start("", false, 30, 11025);        //use default mic

            if (!clip)                                            //NO BROWSER PERMISSION!        
            {
                Debug.LogError("STT: Awaiting Microphone approval");
                return;
            }
            else
            {
                if (debug)
                    Debug.Log("STT: Connected to the Microphone, clip created...");
                aud.clip = clip;
                processing = false;
            }
        }

        //Stop talking event
        if ((!isRecording) && clip)
        {
            if (!processing)
            {
                processing = true;                          //State change, we do this once per talk event!
                wavObject = gameObject.AddComponent<AI_WAV>();
                if (debug)
                    Debug.Log("STT: Detected a stop recording event");

                if (clip)
                {
                    wavObject.ConvertClipToWav(clip);       //wavObject now holds the WAV stream data
                    await STT();
                    Destroy(wavObject);
                }
                else
                    Debug.LogError("STT: Whoops nothing was recorded!");
            }
        }
    }


    //REST API Call using the converted WAV stream buffer
    private async Task STT()
    {
        //Groq STT doesnt use JSON but http forms
        WWWForm form = new WWWForm();
        form.AddField("model", selectedSTTString);
        form.AddField("language", selectedSTTLang);
        form.AddBinaryData("file", wavObject.stream.GetBuffer(), "audio.wav", "audio/wav");          //push the data into a http form field
        UnityWebRequest request = UnityWebRequest.Post(GROQ_API_URI, form);                 //slightly different, not using JSON but Form to send parameters
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

        //Headers
        request.SetRequestHeader("Authorization", "Bearer " + GROQ_API_KEY);                //Don't add a header Content-Type: Application/json here as it uses a http form

        // Send the request and decompress the multimedia response
        await request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            CleanupForNextQuestion();

            string responseText = request.downloadHandler.text;
            SpeechToTextData sttResponse = JsonUtility.FromJson<SpeechToTextData>(responseText);

            // Extract the "Content" section, text
            if (debug)
                Debug.Log("STT service responded with: " + sttResponse.text);

            //Now respond back to the Director that we have some actual spoken text
            await RespondMessage(MessageCommands.isSTTTranscription, sttResponse.text, ComponentID.Broadcast, false);       //Special version, as this is a response to all
        }
        else Debug.LogError("API request failed: " + request.error);
        
        request.Dispose();
    }


    //In WebGL this is crucial otherwise we can only speak once
    private void CleanupForNextQuestion()
    {
        processing = false;     //Open for new question
        Microphone.End(null);   //This is crucial otherwise only the first microphone event will work on WebGL!
        Destroy(clip);          //Cleanup
        clip = null;            //avoid Update() thinks we need to fire another event
        aud.clip = null;        //Even more cleanup
    }


    //JSON Output Class representation
    [Serializable]
    public class SpeechToTextData
    {
        public string text;
    }
    

    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.STT_Service;                      //Make yourself known

        Debug.Log(DEBUG_PREFIX+"Initializing");
        await _messageBus.Subscribe<STTRequestMessage>(HandleSTTRequestAsync);
        await _messageBus.Subscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + " started and subscribed to STTRequestMessage.");

        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else GROQ_API_KEY = api_Keys.GetAPIKey("Groq_API_Key");

        if (GROQ_API_KEY == null)
            Debug.LogWarning(DEBUG_PREFIX + "Warning: STT API key not found, check API Key File!");

        //Connect to the Audio Source component
        aud = GetComponent<AudioSource>();

        //Reinstate original non-alphanumerical characters
        selectedSTTString = selectedModel.ToString().Replace('_', '-').Replace('X', '.');
        selectedSTTLang = selectedLanguage.ToString();

        //Link to the WAV output source & Text Filter
        wavObject = GetComponent<AI_WAV>();                      //Start with a clean stream
    
        //Link to the NPC Click Handler component
        npcClickHandler = GetComponent<NPCClickHandler>();
        if (!npcClickHandler)
            Debug.LogError("STT: Cannot find the NPC Click Handler component");

        sttReady = true;        //REMOVE

        await Task.Delay(0);    //dummy to keep the compiler happy for now.
    }


    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<STTRequestMessage>(HandleSTTRequestAsync);
        await _messageBus.Unsubscribe<PreferenceResponseMessage>(HandlePrefsResponseMessageAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        if (clip != null)
        {
            Destroy(clip);
            clip = null;
        }

        if (aud != null)
        {
            Destroy(aud);
            aud = null;
        }
        
        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }


    //=====================================================================================
    // MESSAGE BUS HANDLERS
    //=====================================================================================

    //Outgoing messages
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        STTResponseMessage message = new STTResponseMessage()
        {
            Command = MessageCommands.isSTTResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<STTResponseMessage>(message);

    }
    
    //Explicit MessageCommand overload
    public async Task RespondMessage(MessageCommands command,  string content, ComponentID requestorId, bool isError)
    {
        STTResponseMessage message = new STTResponseMessage()
        {
            Command = command,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<STTResponseMessage>(message);

    } 


    // Handles the incoming STT request messages.
    private async Task HandleSTTRequestAsync(STTRequestMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            //Start Recording
            case MessageCommands.isSTTStartRecording:
                isRecording = true;         //This triggers the STT service via Update()!
                break;

            //Stop recording
            case MessageCommands.isSTTStopRecording:
                isRecording = false;        //This triggers the STT service via Update()!
                break;

            //Select language, do some addl. checks whether the language exists
            case MessageCommands.isSTTSelectLanguage:
                if (message.Content.Length < 2)
                    await RespondMessage("Unknown language:" + message.Content, message.SenderId, true); //return an error
                else
                {
                    SelectLanguage(message.Content.Substring(0, 2));
                }
                break;

            //Initialize Microphone
            case MessageCommands.isSTTInitializeMicrophone:
                InitializeMicrophone(1);
                break;

            //We received nonsense.
            default:
                await RespondMessage( "Unknown message type", message.SenderId, true);
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
                
                //Avatar
                if (!string.IsNullOrEmpty(message.selectedLanguage))
                    SelectLanguage(message.selectedLanguage);                   //Set the preference language for the STT component
                break;
            
            default:
                Debug.LogError(DEBUG_PREFIX + "Error, unknown MessageCommand received!");
                break;
        }
        return Task.CompletedTask;
    }
}
