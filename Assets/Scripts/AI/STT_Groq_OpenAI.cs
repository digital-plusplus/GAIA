using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;


/// <summary>
/// STT_Groq_OpenAI - Speech to Text Component
/// - adapted for Project GAIA to support WebGL PTT and Release events instead of XR Controller buttons
/// - Updated: 20250128 Sealand IT Services
/// </summary>
public class STT_Groq_OpenAI : MonoBehaviour
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
    AI_STT_Text_Filter aiSTTTextFilter;
    API_Keys api_Keys;

    NPCClickHandler npcClickHandler;                    //Link to component that captures click & release events 
    AudioSource aud;
    private AudioClip clip;
    bool processing;                                    //To block for new requests whilst we're processing

    [SerializeField] bool debug;                        //Debug information
    private bool micInitialized = false;                //Checking whether the microphone was initialized, required for WebGL
    const string DEBUG_PREFIX = "STT_GROQ: ";


    public void Init()
    {
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
        aiSTTTextFilter = GetComponent<AI_STT_Text_Filter>();    //Connect with Text Filter

        //Link to the NPC Click Handler component
        npcClickHandler = GetComponent<NPCClickHandler>();
        if (!npcClickHandler) 
            Debug.LogError("STT: Cannot find the NPC Click Handler component");
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
    
    public void StopSpeaking()
    {
        if (debug) 
            Debug.Log("STT: StopSpeaking called");
        clip = null;
        Microphone.End(null);
    }


    private void Update()
    {        
        //Start talking event - work for WebGL as well
        if ((npcClickHandler.isRecording)&&(!clip))
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

                //Turn on listening expression!
                GetComponent<SyncAllBlendShapes>().SetListen(true);
            }
        }

        //Stop talking event
        if ((!npcClickHandler.isRecording)&&clip)
        {
            if (!processing)
            {
                processing = true;                          //State change, we do this once per talk event!
                wavObject = new AI_WAV();                   //Start with a clean stream
                if (debug) 
                    Debug.Log("STT: Detected a stop recording event");

                if (clip)
                {
                    wavObject.ConvertClipToWav(clip);       //wavObject now holds the WAV stream data
                    StartCoroutine(STT());                  //Call STT cloudsvc  
                }
                else
                    Debug.LogError("STT: Whoops nothing was recorded!");

                //Turn off listening expression!
                GetComponent<SyncAllBlendShapes>().SetListen(false);
            }
        }
    }


    void OnDestroy()
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
    }
   

    //REST API Call using the converted WAV stream buffer
    IEnumerator STT()
    {   
        //Groq STT doesnt use JSON but http forms
        WWWForm form = new WWWForm();
        form.AddField("model", selectedSTTString);
        form.AddField("language", selectedSTTLang);
        form.AddBinaryData("file", wavObject.stream.GetBuffer(), "audio.wav", "audio/wav" );          //push the data into a http form field
        UnityWebRequest request = UnityWebRequest.Post(GROQ_API_URI, form);                 //slightly different, not using JSON but Form to send parameters
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

        //Headers
        request.SetRequestHeader("Authorization", "Bearer " + GROQ_API_KEY);                //Don't add a header Content-Type: Application/json here as it uses a http form

        // Send the request and decompress the multimedia response
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            CleanupForNextQuestion();

            string responseText = request.downloadHandler.text;
            SpeechToTextData sttResponse = JsonUtility.FromJson<SpeechToTextData>(responseText);

            // Extract the "Content" section, text
            if (debug) 
                Debug.Log("STT service responded with: " + sttResponse.text);

            //Now analyze the text and direct to LLM or TTI or....
            aiSTTTextFilter.DirectToCloudProviders(sttResponse.text);
        }
        else Debug.LogError("API request failed: " + request.error);
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
}
