using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;


/// <summary>
/// AI Post-STT Text filter/switching
/// Updated to use AI Orchestrator component
/// </summary>
public class AI_STT_Text_Filter : MonoBehaviour
{
    //Classes of content for selecting either SD, LLM etc..
    public enum PreFilterClass { isImage, isSpeech, is3DEdit, is3DCreate, is3DDelete, is3DLOD, is3DSpecifier, isCode, isVision, isNextCam, isWebSearch };      //add more when needed

    string[] keyWords = new string[] { "image", "picture", "photo", "create", "generate", "edit", "change","update","delete", //8
                                     "clear", "mesh", "model", "detail", "increase", "decrease", //14
                                     "what", "who", "how", "when", "why", "where", "see", "look", "view", "switch", "search", "find", "web", "online"};
    int NUMKEYWORDS;
    bool[] containsKeyWord;                             //Keyword detection
    [SerializeField] bool debug;                        //Debug this component by setting this bool to true in Inspector
    AI_Orchestrator aiO;                                //Link to the AI Orchestrator so we can access the configured other public AI components
    const string DEBUG_PREFIX = "AI_STT_Text_Filter: "; //prefix we use for debugging


    private void Start()
    {
        aiO = GetComponent<AI_Orchestrator>();
        if (!aiO)
        {
            Debug.LogError("AI Orchestrator component not found!");
            return;
        }

        NUMKEYWORDS = keyWords.Length;
        containsKeyWord = new bool[NUMKEYWORDS];        //Array to check whether a keyword was identified in the prompt
    }


    private PreFilterClass Analyze(string input)
    {
        PreFilterClass rtn = PreFilterClass.isSpeech;       //default

        //Lexical scan for keywords
        for (int i = 0; i < keyWords.Length; i++)
        {
            containsKeyWord[i] = false;
            if (input.ToLower().Contains(keyWords[i]))
                containsKeyWord[i] = true;
        }

        //Web Search has highest prio
        if (containsKeyWord[25] || containsKeyWord[26] || containsKeyWord[27] || containsKeyWord[28])
                return PreFilterClass.isWebSearch;

        //Then we want to know whether this is speech with vision, higher prio than speech
        if (containsKeyWord[21] || containsKeyWord[22] || containsKeyWord[23])
            return PreFilterClass.isVision;

        //Now we know whether any keywords were found and now we use some simple regex
        if (containsKeyWord[15] || containsKeyWord[16] || containsKeyWord[17] ||
            containsKeyWord[18] || containsKeyWord[19] || containsKeyWord[20])
            return PreFilterClass.isSpeech;                                         //RAG questions have top prio->speech

        //Switch cameras
        if (containsKeyWord[24])
            return PreFilterClass.isNextCam;

        return rtn;
    }


    //Note we need to make this async as we need to connect to RAG which is an async process
    public async void DirectToCloudProviders(string sttResponseText)
    {
        //string trimmed;

        switch (Analyze(sttResponseText))
        {
            case PreFilterClass.isSpeech:

                string context = "";                    //Will hold the context retrieved from RAG

                //Now send the text to LLM
                if (debug)
                    Debug.Log(DEBUG_PREFIX + "IS_SPEECH");

                if (debug)
                    Debug.Log(DEBUG_PREFIX + "Prompt:" + sttResponseText + ", Context:" + context);

                //Now send the text to LLM via the AI Orchestrator
                aiO.TextToLLM(sttResponseText, context);
                break;

            case PreFilterClass.isVision:
                aiO.TextWithVisionToLLM(sttResponseText);
                break;

            case PreFilterClass.isNextCam:
                aiO.NextCamera();
                break;

            case PreFilterClass.isWebSearch:
                if (debug) Debug.Log(DEBUG_PREFIX + "WEB SEARCH");
                string trimmed = Trim(Trim(Trim(sttResponseText, "for"), "about"), "on");   //search related words to trim after

                context = await aiO.RAGGoogleWebSearch(Trim(sttResponseText, "for"));
                aiO.TextToLLM(sttResponseText, context);    //Now send the text to LLM via the AI Orchestrator
                break;

            default:
                Debug.LogWarning("Don't know what to do with this request!");
                aiO.Say("I have no clue what you want from me, I am really sorry!");
                break;
        } 
    }


    //Trims the string and returns the part after the keyword
    private string Trim(string input, string keyword)
    {
        int idx;
        string rtn;

        rtn = input.ToLower();
        idx = rtn.LastIndexOf(keyword);             //last keyword

        if (idx != -1)
            rtn = rtn.Substring(idx + keyword.Length).Trim();   //Only part of string after the keyword                                                                                                    
        else rtn = input;                            //not found - returns original string

        return rtn;
    }
}
