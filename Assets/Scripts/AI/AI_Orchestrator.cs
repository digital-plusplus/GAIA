using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Main placeholder for all AI related components
/// </summary>
public class AI_Orchestrator : MonoBehaviour
{
    [Header("Speech to Text")]
    [SerializeField] public STT_Groq_OpenAI sttGroqOpenAI;
    [SerializeField] public STT_HF_OpenAI sttHFOpenAI;
    [SerializeField] public STT_11_Labs stt11labs;
    
    [Header("LLM")]
    [SerializeField] public LLM_Groq llmGroq;
    [SerializeField] public LLM_Google llmGoogle;
    [SerializeField] public LLM_Google_Vision llmGoogleVision;  //Special extended version of llmGoogle that takes 3 inputs: (question, context, image)
    [SerializeField] public LLM_Ollama llmOllama;

    [Header("RAG")]
    [SerializeField] public RAG_MariaDB ragMariaDB;
    [SerializeField] public RAG_Google_WebSearch ragGWS;
    [SerializeField] public int maxResults;
    
    [Header("Text to Speech")]
    [SerializeField] public TTS_RA_OpenAI ttsRAOpenAI;
    [SerializeField] public TTS_RA_Speach ttsRASpeach;
    [SerializeField] public TTS_SF_Simba ttsSFSimba;
    [SerializeField] public TTS_11_Labs tts11Labs;

    [Header("Text to Image")]
    [SerializeField] public TTI_HF_SDXLB ttiHFSDXLB;
    [SerializeField] public TTI_Google_FIG ttiGoogleFIG;        //Not yet implemented as ImaGen3 isn't in the free tier yet 

    //[Header("Text to Mesh")]
    //[SerializeField] public TTM_Sloyd_API ttmSloyd;           //Deprecated, Sloyd API is no longer available


    public void Init()
    {
        if (llmGoogle) llmGoogle.Init();
        if (llmGoogleVision) llmGoogleVision.Init();
        if (llmGroq) llmGroq.Init();
        if (llmOllama) llmOllama.Init();

        //if (ragMariaDB) ragMariaDB.Init();
        if (ragGWS) ragGWS.Init();                              //Google Web Search RAG    

        if (sttGroqOpenAI) sttGroqOpenAI.Init();
        if (sttHFOpenAI) sttHFOpenAI.Init();
        if (stt11labs) stt11labs.Init();                        //11 Labs STT

        if (ttiHFSDXLB) ttiHFSDXLB.Init();

        //if (ttmSloyd) ttmSloyd.Init();                        //Deprecated, Sloyd API is no longer available    

        if (tts11Labs) tts11Labs.Init();
        if (ttsRAOpenAI) ttsRAOpenAI.Init();
        if (ttsRASpeach) ttsRASpeach.Init();
        if (ttsSFSimba) ttsSFSimba.Init();
    }


    //Generalized Say command - Expand here for new services!
    public void Say(string input)
    {
        if (ttsSFSimba)     ttsSFSimba.Say(input);
        if (ttsRAOpenAI)    ttsRAOpenAI.Say(input);
        if (ttsRASpeach)    ttsRASpeach.Say(input);
        if (tts11Labs)      tts11Labs.Say(input);
    }


    //Generalized TextToLLM command - Expand here for new services!
    public void TextToLLM(string input, string context)
    {
        if (llmGroq)            llmGroq.TextToLLM(input, context);
        if (llmGoogle)          llmGoogle.TextToLLM(input, context);                //use either of the 2 Google LLM options
        if (llmGoogleVision)    llmGoogleVision.TextToLLM(input, context);          //use either of the 2 Google LLM options
        if (llmOllama)          llmOllama.TextToLLM(input, context);
    }

    //This will let the LLM look at the webcam image
    public void TextWithVisionToLLM(string input)
    {
        if (llmGoogleVision) llmGoogleVision.TextToLLMWithVision(input);
    }


    //Camera controls
    public void NextCamera()
    {
        if (llmGoogleVision) llmGoogleVision.NextCamera();
    }

    public void CameraOff()
    {
        if (llmGoogleVision) llmGoogleVision.CameraOff();
    }


    //Non-async call ro retrieve Context from a RAG database
    // - all RAG systems must implement a .GetContext method
    // - add new services here to ensure consistent calls via aiO.RAGGetContext
    public async Task<string> RAGGetContext(string prompt, int numberOfResults)
    {
        if (ragMariaDB)
        {
            return await ragMariaDB.GetContext(prompt, numberOfResults);
        }
        else return null;
    }

    public async Task<string> RAGGoogleWebSearch(string question)
    {
        if (ragGWS) 
        {
            return await ragGWS.GetTopLinks(question, maxResults); 
        }
        else return null;
    }


    public bool InitializeMicrophone(int duration)
    {
        if (sttGroqOpenAI) return(sttGroqOpenAI.InitializeMicrophone(duration));
        if (sttHFOpenAI) return(sttHFOpenAI.InitializeMicrophone(duration));
        if (stt11labs) return(stt11labs.InitializeMicrophone(duration));
        return (false);
    }


    //Check whether to use RAG or not 
    public bool RAGConfigured()
    {
        return (ragMariaDB == null ? false : true);
    }



    //Generalized TextToImage command - Expand here for new services!
    public void TextToImage(string input)
    {
        if (ttiHFSDXLB)     ttiHFSDXLB.GetImage(input);
    }

    /*
    //Generalized TextToMesh commands - Expand here for new services!
    public void TTMCreate(string input)
    {
        if (ttmSloyd)       ttmSloyd.Create(input);
    }

    public void TTMEdit(string input)
    {
        if (ttmSloyd)       ttmSloyd.Edit(input);
    }

    public void TTMDelete()
    {
        if (ttmSloyd)       ttmSloyd.Delete();
    }
    */
}
