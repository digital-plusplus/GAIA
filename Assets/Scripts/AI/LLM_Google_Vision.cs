using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.Networking;


/// <summary>
/// Component for Google Gemini 2.0 models with Vision API
/// </summary>
public class LLM_Google_Vision : MonoBehaviour
{
    private string apiKey;
    const string apiURI = "https://generativelanguage.googleapis.com/v1beta/models/";
    
    //public RawImage rawImage;                               //to capture the webcam
    private List<WebCamDevice> webCamDevices = new List<WebCamDevice>();
    private WebCamTexture webCamTexture;
    private Texture2D texture;
    private int currentCamera = 0;                      

    private enum LLMModel
    {
        gemini_2X0_flash, gemini_2X0_flash_lite, gemini_2X5_pro_preview_03_25
    }

    [SerializeField]
    private LLMModel selectedModel;
    string selectedLLMString;
    private string LLMresult = "Waiting";

    [SerializeField]
    private int maxNumberOfWords = 0;                     //0 means no limits to the length of the response

    //NEW!
    [SerializeField]
    private string whoAmI = "nobody";

    [SerializeField]
    private string context;

    [SerializeField]
    private bool closedContext;

    List<Content> messageHistory;
    AI_Orchestrator aiO;
    API_Keys api_Keys;

    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "LLM_GOOGLE_VISION: ";      //prefix we use for debugging

    Content systemInstruction = new Content();              //Google does not have a system message but rather uses a system_instruction preceeding the regular contents array        


    public void Init()
    {
        string prompt;
        DateTime currentDate = DateTime.Now;

        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else apiKey = api_Keys.GetAPIKey("Google_API_Key");

        if (apiKey == null)
            Debug.LogWarning(DEBUG_PREFIX + "Warning: API key not found, check API Key File!");

        //Now find the Orchestrator
        aiO = GetComponent<AI_Orchestrator>();
        if (!aiO)
        {
            Debug.LogError(DEBUG_PREFIX + "AI Orchestrator component not found!");
            return;
        }

        selectedLLMString = selectedModel.ToString().Replace('_', '-').Replace('X', '.');
        if (debug)
            Debug.Log(DEBUG_PREFIX + "You have selected LLM: " + selectedLLMString);

        //CONSTRUCT PROMPT - STEP 1: WHO IS THIS
        prompt = "You are " + whoAmI;

        //STEP 2: HOW LONG CAN THE RESPONSE BE
        if (maxNumberOfWords > 0)
            prompt += "\nAnswer all questions in maximum " + maxNumberOfWords + " words\n";

        //STEP 3: GIVE IT A NOTION OF TIME & AVOID IT REINTRODUCING ITSELF
        prompt += "\nToday is " + currentDate.ToShortDateString();
        prompt += "\nYou can only mention your name once in your anwsers, unless you are specifically asked for your name.\n";    //to avoid it keeps introducing itself

        //STEP4: NOW WE ADD THE CONTEXT
        prompt += CreatePromptContext(context);

        if (debug)
            Debug.Log(DEBUG_PREFIX + prompt);

        messageHistory = new List<Content>();

        //UPDATED: Initialize the conversation history with a system_message, different than Groq cloud!
        systemInstruction.role = "model";
        systemInstruction.parts = new Part[]
        {
            new Part { text = prompt }
        };

        //Now we setup the Vision part - first we get all available webcams
        WebCamDevice[] devices = WebCamTexture.devices;
        webCamDevices.AddRange(devices);

        webCamTexture = new WebCamTexture();

        SetCamera();       //DEBUG
    }


    public void CameraOff()
    {
        currentCamera = 0;          //force to state OFF
        SetCamera();
    }


    //Selects the active webcam, eg. on handheld devices there are more than one camera
    public void SetCamera()
    {
        int curCam = currentCamera - 1;         //we reduce by 1, if the value == -1 then the camera must be turned OFF

        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
        }

        if (debug)
        {
                Debug.Log(DEBUG_PREFIX + "Number of cameras detected:" + webCamDevices.Count);
                for (int i = -1; i < webCamDevices.Count; i++)
                    Debug.Log( DEBUG_PREFIX + (i==-1 ? "OFF" : " " + i + " " + webCamDevices[i].name) + (i == curCam ? "<SELECTED" : ""));
            
        }

        //Camera ON, select the vailable camera
        if (curCam != -1)
        {
            webCamTexture = new WebCamTexture(webCamDevices[curCam].name);
            webCamTexture.Play();

            GameObject genImg = GameObject.Find("ImageFrame(Clone)");           //We want to display what the camera is seeing on screen
            if (!genImg)
            {
                genImg = Resources.Load<GameObject>("ImageFrame");
                genImg = Instantiate(genImg, new Vector3(-5.876f, 1.5f, 6.481f), Quaternion.Euler(7.44f, 0, 0));
            }

            if (!genImg) Debug.Log("Can't load the ImageFrame for the TTI output");
            else
            {
                //set texture
                Material myNewMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                myNewMaterial.SetTexture("_BaseMap", webCamTexture);
                genImg.GetComponent<MeshRenderer>().material = myNewMaterial;
            }
        }
        else    //Camera OFF and destroy the on-screen image, we will recreate it on TTI or next camera event
        {
            GameObject genImg = GameObject.Find("ImageFrame(Clone)");
            if (genImg) Destroy(genImg);
        }
    }


    //Public method accessed by the AI Orchestrator
    public void TextToLLM(string mesg, string context)
    {
        StartCoroutine(TalkToLLM(mesg, context));
    }

    //Public method accessed by the AI Orchestrator
    public void TextToLLMWithVision(string mesg)
    {
        StartCoroutine(TalkToLLMWithVision(mesg));
    }


    //Changes the camera to the next camera, rotating with the #of cameras
    public void NextCamera()
    {
        currentCamera = (currentCamera + 1) % (webCamDevices.Count+1);      //we add one "camera OFF" state
        SetCamera();

        Debug.Log(DEBUG_PREFIX + " switching camera to " + currentCamera);
    }


    //Creates the context for the initial System message and for any consecutive RAG contexts if applicable
    private string CreatePromptContext(string input)
    {
        string prompt = "";
        if (input != "")
        {
            prompt += "\nAnswer the question based on the following context:\n===\n";
            prompt += input;
            prompt += "\n===";

            if (closedContext)
                prompt += "\nIf you can't find the answer in the context then you respond with: 'I really have no idea!' or 'I don't know, sorry!' or 'Uuuhm, dunno!'";
        }
        return prompt;
    }


    private IEnumerator TalkToLLM(string mesg, string context)
    {
        RequestData requestBody = new RequestData();

        //Now we check for context! Gemini has a 1M token window so we can safely amend all previous "user" and "model" messages
        string tmpContext = CreatePromptContext(context);
        string promptWithContext = mesg;

        if (debug) Debug.Log(DEBUG_PREFIX + "MESSAGE=" + mesg + "CONTEXT=" + tmpContext);

        if (tmpContext != "") promptWithContext += tmpContext;      //If there was context, add it to the prompt
        AppendConversation(promptWithContext, "user", null);        //Add the context to the prompt for RAG

        requestBody.system_instruction = systemInstruction;         //Initialize the conversation with a system_instruction which is similar to a system message in LLaMa models
        requestBody.contents = messageHistory.ToArray();            //Add the complete conversation history

        //Crucial: need to fix the Google API inconsistency - only accepts either text or inline_data not both!
        string jsonRequestBody = RewriteJSON(JsonUtility.ToJson(requestBody));
       
        LLMresult = "Waiting";
        if (debug)
            Debug.Log(DEBUG_PREFIX + jsonRequestBody);

        string toSend = apiURI + selectedLLMString + ":generateContent?key=" + apiKey;                      //Google sends the API key as a PUT parameter vs a http header!
        UnityWebRequest request = new UnityWebRequest(toSend, "POST");
        if (debug)
            Debug.Log(DEBUG_PREFIX + " using URI: " + toSend);

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequestBody);
        request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        //Ready to fire off the HTTP request to the API!
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            GeminiResponse geminiCS = JsonUtility.FromJson<GeminiResponse>(responseText);
            LLMresult = geminiCS.candidates[0].content.parts[0].text;                                       //Assuming there is 1 Candidate and 1 Part in the response!
            if (debug)
                Debug.Log(DEBUG_PREFIX + LLMresult);

            //now lets call TTS via a single call to the central AI Orchestrator!
            aiO.Say(LLMresult);
        }
        else Debug.LogError(DEBUG_PREFIX + "LLM API Request failed: " + request.error);

        //replace last message by removing the context and keep the prompt only - to avoid LLM prompt overload
        AppendConversation(LLMresult, "model", null);                                                             //In Gemini we have a 1M token window so we store both user and model history!
    }


    private IEnumerator TalkToLLMWithVision(string mesg)
    {
        RequestData requestBody = new RequestData();

        //Now we check for context! Gemini has a 1M token window so we can safely amend all previous "user" and "model" messages
        string promptWithContext = mesg;

        if (debug) 
            Debug.Log(DEBUG_PREFIX + "MESSAGE=" + mesg + " PLUS WEBCAMIMAGE!");

        if (!webCamTexture)
        {
            aiO.Say("I see nothing, can you click the Camera icon to select a camera and then ask me again?");
            yield return null;
        }
        else
        {

            AppendConversation(promptWithContext, "user", WebCamTextureToBase64String(webCamTexture));

            requestBody.system_instruction = systemInstruction;         //Initialize the conversation with a system_instruction which is similar to a system message in LLaMa models
            requestBody.contents = messageHistory.ToArray();            //Add the complete conversation history

            //Crucial: need to fix the Google API inconsistency - only accepts either text or inline_data not both!
            string jsonRequestBody = RewriteJSON(JsonUtility.ToJson(requestBody));

            LLMresult = "Waiting";
            if (debug)
                Debug.Log(DEBUG_PREFIX + jsonRequestBody);

            string toSend = apiURI + selectedLLMString + ":generateContent?key=" + apiKey;                      //Google sends the API key as a PUT parameter vs a http header!
            UnityWebRequest request = new UnityWebRequest(toSend, "POST");
            if (debug)
                Debug.Log(DEBUG_PREFIX + " using URI: " + toSend);

            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequestBody);
            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            //Ready to fire off the HTTP request to the API!
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                GeminiResponse geminiCS = JsonUtility.FromJson<GeminiResponse>(responseText);
                LLMresult = geminiCS.candidates[0].content.parts[0].text;                                       //Assuming there is 1 Candidate and 1 Part in the response!
                if (debug)
                    Debug.Log(DEBUG_PREFIX + LLMresult);

                //now lets call TTS via a single call to the central AI Orchestrator!
                aiO.Say(LLMresult);
            }
            else Debug.LogError(DEBUG_PREFIX + "LLM API Request failed: " + request.error);

            //Add the LLM answer to the Conversation History. In Gemini we have a 1M token window so we store both user and model history!
            // we do want to erase the image data though as this will quickly fill up the prompt and slow down the E2E experience!
            if (messageHistory.Count > 0)
                messageHistory[messageHistory.Count - 1].parts[0].inline_data = null;

            //Now for Vision we don't want to resend earlier messages, just the responses so the LLM still has a clue what it saw
            RemoveLastMessageFromConversation();
            AppendConversation(LLMresult, "model", null);         //note that the LLM never returns an image             
        }
    }


    //=CRUCIAL!===================================================================================================================================
    //Hard-recode empty inline_data and text entries! JsonUtility generates these & Google does not accept these empty fields in their Gemini API!
    private string RewriteJSON(string input)
    {
        string jsonRequestBody = input; 
        
        jsonRequestBody = jsonRequestBody.Replace(",\"inline_data\":{\"mime_type\":\"\",\"data\":\"\"}", "");       //remove empty inline fields
        jsonRequestBody = jsonRequestBody.Replace("\"text\":\"\",", "");                                            //remove empty text fields
        jsonRequestBody = jsonRequestBody.Replace(",{\"text\":\"\"}", "");                                          //remove empty text fields
        
        return jsonRequestBody;
    }


    private void AppendConversation(string mesg, string myRole, string base64Image)
    {

        Content newMesg = new Content
        {
            role = myRole,
            parts = new Part[]
            {
                //The API wants to see a SEPARATE part for the text and the inline_data!
                new Part
                {
                    text=mesg,
                    inline_data=null
                },
                new Part
                {
                    text=null,
                    inline_data = string.IsNullOrEmpty(base64Image) ? null : new InlineData()       //if base64Image is empty we just create a text field in the Part 
                    {
                        mime_type = "image/jpeg",
                        data = base64Image
                    }
                }
                
            } 
        };
        
        messageHistory.Add(newMesg);
    }


    //We don't want to store the last image
    private void RemoveLastMessageFromConversation()
    {
        messageHistory.Remove(messageHistory[messageHistory.Count-1]);
    }


    //Convert the texture to a BASE64 encoded string so we can send it to the API
    public string WebCamTextureToBase64String(WebCamTexture webCamTexture)
    {
        if (webCamTexture == null)
        {
            Debug.LogError("WebCamTexture is null.");
            return null;
        }

        // Create a Texture2D with the same dimensions as the WebCamTexture
        texture = new Texture2D(webCamTexture.width, webCamTexture.height);

        // Copy the pixel data from WebCamTexture to Texture2D
        texture.SetPixels(webCamTexture.GetPixels());
        texture.Apply();

        // Encode the Texture2D to PNG and convert to base64
        return TextureToBase64String(texture);
    }

    public string TextureToBase64String(Texture2D texture)
    {
        if (texture == null)
        {
            Debug.LogError("Texture is null.");
            return null;
        }

        byte[] textureBytes = texture.EncodeToJPG();
        string base64String = Convert.ToBase64String(textureBytes);

        return base64String;
    }



    /// ================================
    /// JSON structures for Google API
    /// ================================

    //REQUESTS
    [Serializable]                                  // PARTS section
    public class Part
    {
        public string text;
        public InlineData inline_data;
    }

    [Serializable]                                  // PARTS are encapsulated by multiple CONTENT
    public class Content
    {
        public string role;
        public Part[] parts;                        // Array to match JSON [ 
    }

    [Serializable]
    public class InlineData
    {
        public string mime_type;                    //For Vision, provides the image type, mostly image/jpeg!
        public string data;
    }

    [Serializable]                                  // array of CONTENT
    public class RequestData
    {
        public Content system_instruction;          // Single system_instruction field
        public Content[] contents;                  // Array of Parts and roles
    }


    //RESPONSES
    [Serializable]
    public class ResponsePart
    {
        public string text;
    }

    [Serializable]
    public class ResponseContent
    {
        public ResponsePart[] parts;
        public string role;
    }

    [Serializable]
    public class Candidate
    {
        public ResponseContent content;
        // Add other fields if needed: finishReason, index, safetyRatings, etc.
    }

    [Serializable]
    public class GeminiResponse
    {
        public Candidate[] candidates;
        // Add promptFeedback if needed
    }
}
