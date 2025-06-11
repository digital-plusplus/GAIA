using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;

public class TTS_SF_Simba : MonoBehaviour
{
    //Variables
    private string SPEECHIFY_API_KEY;

    private enum SelectVoice
    {
        NL_daan, NL_lotte, US_henry, US_carly, US_kyle, US_kristy, US_oliver, US_tasha, US_joe, US_lisa,
        US_george, US_emily, US_rob, GB_russell, GB_benjamin, GB_michael, AU_kim, IN_ankit, IN_arun,
        GB_carol, GB_helen, US_julie, AU_linda, US_mark, US_nick, NG_elijah, GB_beverly, GB_collin,
        US_erin, US_jack, US_jesse, US_keenan, US_lindsey, US_monica, GB_phil, GB_declan, US_stacy,
        GB_archie, US_evelyn, GB_freddy, GB_harper, US_jacob, US_james, US_mason, US_victoria
    }

    private enum SelectModel
    {
        _base, _english, _multilingual, _turbo
    }

    [SerializeField]
    private SelectVoice selectVoice;

    [SerializeField]
    private SelectModel selectModel;

    const string TTS_API_URI = "https://api.sws.speechify.com/v1/audio/stream";      //POST URI, streaming API
    private string sfVoice;
    private string sfModel;
    Animator avtAnimator;
    API_Keys api_Keys;

    [SerializeField] private bool debug;
    const string DEBUG_PREFIX = "TTS_SF_SIMBA: ";


    // Start is called before the first frame update
    public void Init()
    {
        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else SPEECHIFY_API_KEY = api_Keys.GetAPIKey("Speechify_API_Key");

        if (SPEECHIFY_API_KEY == null)
            Debug.LogWarning(DEBUG_PREFIX + "Warning: TTS API key is empty, check Inspector!");

        avtAnimator = GetComponent<Animator>();

        sfVoice = selectVoice.ToString().Substring(3);
        sfModel = "simba-"+selectModel.ToString().Substring(1);
        Debug.Log("You have selected voice " + sfVoice + " and model "+sfModel);
    }


    public void Say(string textInput)
    {
        StartCoroutine(PlayTTS(textInput));
    }


    IEnumerator PlayTTS(string mesg)
    {
        //JSON
        TextToSpeechData ttsData = new TextToSpeechData();
        ttsData.input = SimpleCleanText(mesg);
        ttsData.voice_id = sfVoice;
        ttsData.model = sfModel;
        string jsonPrompt = JsonUtility.ToJson(ttsData);

        //WebRequest
        UnityWebRequest request = new UnityWebRequest(TTS_API_URI, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonPrompt));
        request.downloadHandler = new DownloadHandlerAudioClip(TTS_API_URI, AudioType.MPEG);

        //Headers
        request.SetRequestHeader("content-type", "application/json");
        request.SetRequestHeader("accept", "audio/mpeg");
        request.SetRequestHeader("Authorization", SPEECHIFY_API_KEY);
        if (debug)
        {
            Debug.Log(DEBUG_PREFIX+jsonPrompt);
            //Debug.Log(request.ToString());
        }
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            avtAnimator.SetBool("isTalking", true);

            //================================================
            //Some manual simple facial expression changes => move to separate component later!
            GetComponent<SyncAllBlendShapes>().SetSmile(true);    //I take all questions seriously!
            //================================================

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);

            //The below replaces PlayOneShot(), used for WebGL compatibility
            GetComponent<AudioSource>().clip = clip;
            GetComponent<AudioSource>().loop = false;
            GetComponent<AudioSource>().Play();

            StartCoroutine(WaitForTalkingFinished());
        }
        else Debug.LogError(DEBUG_PREFIX+"TTS API Request failed: " + request.error);
    }


    IEnumerator WaitForTalkingFinished()
    {
        while (GetComponent<AudioSource>().isPlaying)
        {
            yield return null;
        }

        //The below replaces PlayOneShot(), used for WebGL compatibility
        GetComponent<AudioSource>().clip = null;
        GetComponent<AudioSource>().Stop();

        avtAnimator.SetBool("isTalking", false);
        GetComponent<SyncAllBlendShapes>().SetSmile(false);         //smile OFF
        GetComponent<SyncAllBlendShapes>().SetSerious(false);       //Serious off
        //Add any code here that has to be sure the speech is completed, eg. animations
    }


    //JSON Support Classes
    [Serializable]
    public class TextToSpeechData
    {
        public string input;
        public string voice_id;
        public string model;
    }


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
                default:
                    result += msg[i];       //simply pass on everything else
                    break;
            }
        }
        return result;
    }
}
