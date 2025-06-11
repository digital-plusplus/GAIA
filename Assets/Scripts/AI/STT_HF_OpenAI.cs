using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;


public class STT_HF_OpenAI : MonoBehaviour
{
    private string HF_INF_API_KEY;
    const string STT_API_URI_t = "https://api-inference.huggingface.co/models/openai/whisper-tiny";             //POST URI
    const string STT_API_URI_L = "https://api-inference.huggingface.co/models/openai/whisper-large-v3";         //another POST URI
    const string STT_API_URI_T = "https://api-inference.huggingface.co/models/openai/whisper-large-v3-turbo";   //another POST URI

    private enum STTModel { tiny, large, turbo };
    
    [SerializeField] private STTModel selectedModel;
    string selectedSTTString;

    AI_WAV wavObject;                                   //Object that holds stream and methods for WAV
    AI_STT_Text_Filter aiSTTTextFilter;
    API_Keys api_Keys;

    NPCClickHandler npcClickHandler;                    //Link to component that captures click & release events 
    AudioSource aud;
    private AudioClip clip;
    bool processing;
    private bool micInitialized = false;

    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "STT_HF: ";

    public void Init()
    {
        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else HF_INF_API_KEY = api_Keys.GetAPIKey("HF_API_Key");

        if (HF_INF_API_KEY == null)
            Debug.LogWarning(DEBUG_PREFIX + "Warning: STT API key is empty, check Inspector!");

        switch (selectedModel)
        {
            case STTModel.tiny:
                selectedSTTString = STT_API_URI_t;
                break;
            case STTModel.large:
                selectedSTTString = STT_API_URI_L;
                break;
            case STTModel.turbo:
                selectedSTTString = STT_API_URI_T;
                break;

            default:
                Debug.LogError("STT: ILLEGAL STT MODEL SELECTED!");
                break;
        }

        //Connect to the Audio Source component
        aud = GetComponent<AudioSource>();

        //Note: you can't use new to allocate memory for MonoBehavior objects
        wavObject = GetComponent<AI_WAV>();                      //Start with a clean stream
        aiSTTTextFilter = GetComponent<AI_STT_Text_Filter>();    //Connect with Text Filter

        //Link to the NPC Click Handler component
        npcClickHandler = GetComponent<NPCClickHandler>();
        if (!npcClickHandler)
            Debug.LogError("STT: Cannot find the NPC Click Handler component");
    }


    //=========================================================================
    //Event handlers initiate the AI Conversation
    //=========================================================================
    public void StopSpeaking()
    {
        if (debug)
            Debug.Log("STT: StopSpeaking called");
        Microphone.End(null);
    }


    private void Update()
    {
        if ((npcClickHandler.isRecording) && (!clip))
        {
            clip = Microphone.Start("", false, 30, 11025);        //use default mic
            if (!clip)                                      //NO BROWSER PERMISSION!        
            {
                Debug.LogError("STT: Browser does not permit me to use the microphone!");
                return;
            }
            else
            {
                if (debug)
                    Debug.Log("STT: Connected to the Microphone, clip created...");
                aud.clip = clip;
                processing = false;

                //Turn on listening expression!
                GetComponent<SyncAllBlendShapes>().SetSmile(true);
            }
        }

        if ((!npcClickHandler.isRecording) && clip)
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
                GetComponent<SyncAllBlendShapes>().SetSmile(false);
            }
        }
    }


    //Initial kick of the microphone to enforce the Microphone approval in WebGL on browsers!
    public bool InitializeMicrophone(int seconds)
    {
        if (micInitialized) return true;

        Debug.Log("STT: Initializing Microphone");
        AudioClip tmpClip = Microphone.Start("", false, seconds, 11025);
        if (tmpClip)
        {
            micInitialized = true;
            Debug.Log("STT: Microphone initialized");
            return true;
        }
        return false;
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
        //JSON
        SpeechToTextData sttData = new SpeechToTextData();
        
        //Set up the UnityWebRequest
        UnityWebRequest request = new UnityWebRequest(selectedSTTString, "POST");

        //Audio must be converted to WAV before this is called!
        request.uploadHandler = new UploadHandlerRaw(wavObject.stream.GetBuffer());
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

        //Headers
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer "+ HF_INF_API_KEY);

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

    //Input data is MP3, FLAC, WAV etc, no JSON wrapper required

}
