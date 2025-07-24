using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using imessages;
using Newtonsoft.Json;
using System.Collections.Concurrent;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

///**********************************************************************************
///DEPRECATED *** As per January 2026 Gemini API is no longer updated *** DEPRECATED
///**********************************************************************************
/// <summary> 
/// Component for Google Gemini 2.0 models with Vision API and Tool-Calling
/// </summary>

public class LLM_Google_Vision : MonoBehaviour, ILlmVisionService, IAIComponent
{
    //==========================
    // PROPERTIES
    //==========================

    //Local devices
    private List<WebCamDevice> webCamDevices = new List<WebCamDevice>();
    private WebCamTexture webCamTexture;
    private Texture2D texture;
    private int currentCamera = 0;
    private int numWebcamDevices = 0;
    [SerializeField] int maxCameras = 2;                    //Hard limit to allow only 1 front and 1 rear camera


    //LLM configuration
    const string apiURI = "https://generativelanguage.googleapis.com/v1beta/models/";
    private enum LLMModel
    {
        //gemini_2X0_flash, gemini_2X0_flash_lite, gemini_2X5_flash_lite, gemini_2X5_flash, gemini_2X5_pro, gemini_3_pro_preview
        gemini_3_flash_preview, gemini_2X5_flash_lite, gemini_2X5_flash, gemini_2X5_pro, gemini_3_pro
    }

    [SerializeField] private LLMModel selectedModel;
    string selectedLLMString;
    private string LLMresult = "Waiting";
    List<Content> messageHistory;                           //This stores the message history for the LLM in object format, must Serialize to JSON before sending
    Content systemInstruction = new Content();              //Google does not have a system message but rather uses a system_instruction preceeding the regular contents array        
    string systemInstructionString = "";
    private ConcurrentDictionary<string, object> langMgrGetLocalizedDict = new ConcurrentDictionary<string, object>();     //LanguageManager response cache, thread safe version
    private string langMgrLangSuffix=null;                  //Last received language from Prefs Mgs
    private string prefMgrCurrentLang = null;               //Last received language from Pref Mgr

    //Direct connections to Unity Components
    private AI_Director aiD;                                //To store generic platform-independent settings
    [SerializeField] DeviceOrientationManager dOM;          //Link to send textures to the imageFrame
    API_Keys api_Keys;
    private string apiKey;


    //Debugging
    [SerializeField] bool debug;
    iMessage iM = new iMessage();
    const string DEBUG_PREFIX = "LLM_GOOGLE_VISION: ";      //prefix we use for debugging


    //MessageBus    
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }


    //==========================
    // METHODS
    //==========================

    //Forces the camera into the OFF state 
    public void CameraOff()
    {
        currentCamera = 0;          //force to state OFF
        SetCamera();
    }


    //Mandatory public method for Vision enabled LLMs
    // Used by AI Orchestrator to determine whether the camera is active
    public bool isCameraOn() { return currentCamera != 0; }


    //Selects the active webcam, eg. on handheld devices there are more than one camera
    public void SetCamera()
    {
        int curCam = currentCamera - 1;         //we reduce by 1, if the value == -1 then the camera must be turned OFF

        if (debug)
            Debug.Log(DEBUG_PREFIX + " SetCamera to " + curCam);

        //If there still is an active webcamtexture then remove it 
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
            webCamTexture = null;
        }

        //Now we activate the new camera
        if (curCam != -1)       //Camera ON, select the available camera
        {
            webCamTexture = new WebCamTexture(webCamDevices[curCam].name);
            webCamTexture.Play();

            //Camera changed, now send the texture to the image frame and tell which camera we are using to ensure proper orientation
            // front webcam is "upside-down" so Portrait mode image must be inverted for front cam
            dOM.ShowImageFrame(webCamTexture, curCam);
        }
        else                    //Camera OFF and destroy the on-screen image, we will recreate it on TTI or next camera event
        {
            dOM.DestroyImageFrame();
        }
    }


    // Especially needed for Android as its sloppy with releasing resources !
    void OnApplicationQuit()
    {
        if (debug) Debug.Log(DEBUG_PREFIX + "Application quitting. Stopping webcam.");
        if (webCamTexture != null)
        {
            if (webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }
            // Dispose of the texture. This is crucial for releasing native resources.
            Destroy(webCamTexture);
            webCamTexture = null;
        }
    }


    //Changes the camera to the next camera, rotating with the #of cameras
    public void NextCamera()
    {
        currentCamera = (currentCamera + 1) % (numWebcamDevices + 1);     //we add one "camera OFF" state

        if (debug)
            Debug.Log(DEBUG_PREFIX + "NextCamera: CurrentCamera is now " + currentCamera);

        SetCamera();
    }


    //===================
    //Prompt handling
    //===================
    public void AppendSystemInstruction(string message)
    {
        //UPDATED: Initialize the conversation history with a system_message, different than Groq cloud!
        if (systemInstruction.parts == null)
            systemInstruction.parts = new Part[] { new Part { text = message } };
        else
            systemInstruction.parts[0].text += message;
        systemInstructionString += message;
    }


    //Creates a new system prompt and adds the suffix, eg. to tell the LLM to respond in another language
    public void InitializeSystemPrompt()
    {
        string prompt;
        DateTime currentDate = DateTime.Now;

        //CONSTRUCT PROMPT - STEP 1: WHO IS THIS
        prompt = "Your name is " + aiD.whoAmI;

        //STEP 2: HOW LONG CAN THE RESPONSE BE
        if (aiD.maxNumberOfWords > 0)
            prompt += "\nAnswer all questions in maximum " + aiD.maxNumberOfWords + " words\n";

        // STEP 3: GIVE IT A NOTION OF TIME & AVOID IT REINTRODUCING ITSELF
        prompt += "\nToday is " + currentDate.ToShortDateString();
        prompt += "\nAssume the user already knows your name and only tell the user your location or the time when explicitly asked.";

        //STEP4: NOW WE ADD THE CONTEXT FROM THE UI
        prompt += CreatePromptContext(aiD.context);

        if (debug)
            Debug.Log(DEBUG_PREFIX + prompt);

        //Initialize the message History and post the system message
        messageHistory = new List<Content>();                       //empty List
        AppendSystemInstruction(prompt);                            //fills global variable systemInstruction
    }


    //Creates the context for the initial System message and for any consecutive RAG contexts if applicable
    private string CreatePromptContext(string input)
    {
        string prompt = "";
        if (input != "")
        {
            prompt += "\nAdditional context:\n===\n";
            prompt += input;
            prompt += "\n===";
        }
        return prompt;
    }


    private void AppendConversation(string mesg, string myRole, string base64Image)
    {
        List<Part> parts = new List<Part>();

        if (!string.IsNullOrEmpty(mesg))
        {
            parts.Add(new Part { text = mesg, inline_data = null });
        }

        if (!string.IsNullOrEmpty(base64Image))
        {
            parts.Add(new Part
            {
                text = null,
                inline_data = new InlineData()
                {
                    mime_type = "image/jpeg",
                    data = base64Image
                }
            });
        }

        if (parts.Count == 0)
        {
            Debug.LogError(DEBUG_PREFIX + "Attempted to append an empty message to history, ignoring.");
            return;
        }

        Content newMesg = new Content
        {
            role = myRole,
            parts = parts.ToArray()
        };

        messageHistory.Add(newMesg);
    }


    //We don't want to store the last image
    private void RemoveLastMessageFromConversation()
    {
        messageHistory.Remove(messageHistory[messageHistory.Count - 1]);
    }


    //All possible context info is captured in global variables and sent to the LLM when relevant
    // Mechanism to deal with stateless request/response message architecture
    // Hence clear irrelevant context as soon as its used!
    private async Task<string> BuildCompleteContextString()
    {
        string currentContext = "";
        DateTime currentDate = DateTime.Now;

        if (aiD.lat != -1f && aiD.lon != -1f)
        {
            currentContext += $"\nThe current lattitude is {aiD.lat} and longitude is {aiD.lon}. ";
        }

        if (!string.IsNullOrEmpty(aiD.ragWebContext))
        {
            currentContext += $"\nRecent web information: {aiD.ragWebContext}. ";
            aiD.ragWebContext = ""; // Reset de context na gebruik
        }

        if (!string.IsNullOrEmpty(aiD.ragWeatherContext))
        {
            currentContext += $"\nCurrent weather information: {aiD.ragWeatherContext}. ";
            aiD.ragWeatherContext = ""; // Reset de context na gebruik
        }

        //We need to determine the language suffix for the LLM, we use a synchronous MessageBus call option, this will first try to pull from the cache to avoid message overhead!
        langMgrLangSuffix =  await LanguageManagerGetLocalized("responseLanguage");                                   
        currentContext += langMgrLangSuffix;                                                                          //add to the context explicitly for each query

        return currentContext;
    }


    //======================
    // REST API HANDLING
    //======================

    //Now has a functionCall/Tool Call parameter 
    private async Task TalkToLLM(string mesg, bool withVision = false, bool isToolCallLoop = false) //no more context parameter, ensure all context is actual in global parameters!
    {
        RequestData requestBody = new RequestData();

        if (debug) Debug.Log(DEBUG_PREFIX+mesg);

        //Now we check for context! Gemini has a 1M token window so we can safely amend all previous "user" and "model" messages
        string context = await BuildCompleteContextString();
        string promptWithContext = mesg + context;

        //Add the context to the prompt for RAG, but only if this is a user message and not an internal tool call message to the LLM
        if (!isToolCallLoop)
        {
            string base64Image = null;
            if (withVision)
            {
                if (!webCamTexture)
                {
                    string seeNothing = await LanguageManagerGetLocalized("see_nothing");                   //Synchronous MessageBus call option
                    seeNothing = (seeNothing == null ? "Please turn on your camera!" : seeNothing);
                    await SendMessage<TTSRequestMessage>(MessageCommands.isTTSSay, seeNothing);
                    return;
                }
                base64Image = WebCamTextureToBase64String(webCamTexture);
            }
            AppendConversation(promptWithContext, "user", base64Image);
        }

        //Crucial: Gemini uses a system_instruction vs a system prompt
        requestBody.system_instruction = systemInstruction;             //Initialize the conversation with a system_instruction which is similar to a system message in LLaMa models
        requestBody.contents = messageHistory.ToArray();                //Add the complete conversation history
        requestBody.tools = GetToolDefintions();                        //Adds the tools available to the LLM at root level of the json file

        //CRUCIAL: WE DON'T USE JSONUTILITY HERE AS GEMINI IS VERY STRICT ON EMPTY JSON VALUES AND DOES NOT ACCEPT THESE !!!
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        string jsonRequestBody = JsonConvert.SerializeObject(requestBody, settings);

        LLMresult = "Waiting";
        if (debug)
            Debug.Log(DEBUG_PREFIX + jsonRequestBody);

        string toSend = apiURI + selectedLLMString + ":generateContent?key=" + apiKey;                      //Google sends the API key as a PUT parameter vs a http header!
        UnityWebRequest request = new UnityWebRequest(toSend, "POST");

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequestBody);
        request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        //Ready to fire off the HTTP request to the API!
        await request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            GeminiResponse geminiCS = JsonConvert.DeserializeObject<GeminiResponse>(responseText);

            if (geminiCS.candidates != null && geminiCS.candidates.Length > 0)
            {
                ResponsePart part = geminiCS.candidates[0].content.parts[0];

                //function call!
                if ((part.functionCall != null) && !string.IsNullOrEmpty(part.functionCall.name))
                    await SendAppendFunctionCall(part, withVision);
                else //Normal Text 
                    await SendAppendUserQuery(part, withVision);
            }
        }
        else
        {
            //First lets try to get some details as per what failed
            string errorMessage=null;
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                errorMessage = "LLM API Request failed: " + request.error;
                string responseText = request.downloadHandler.text;
                if (!string.IsNullOrEmpty(responseText))
                {
                    try
                    {
                        GoogleApiErrorResponse errorResponse = JsonUtility.FromJson<GoogleApiErrorResponse>(responseText);
                        errorMessage += ", " + errorResponse.error.message;
                        await SendMessage<TTSRequestMessage>(MessageCommands.isTTSSay,errorResponse.error.message);     //We tell the user as an FYI
                    }
                    catch
                    {
                        errorMessage += ">> " +responseText; //raw error
                    }
                }
            }
            Debug.LogError(DEBUG_PREFIX + errorMessage);
            AppendConversation("errorMessage","model", null);                                                           //Store the error in the Message History
            await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");                            //Unlock button
        }
    }


    //=================================================================================================================
    // A: Tool call message for the LLM, broken out in a dedicated method to improve readability of the code
    //  This essentially converts the functionCall from the LLM to a MessageBus Request and updates the messageHistory
    //  => the ResponseMessageHandler method will send the response back to the LLM
    //=================================================================================================================
    private async Task SendAppendFunctionCall(ResponsePart part, bool withVision)
    {
        FunctionCall call = part.functionCall;
        LLMresult = "Tool Call Requested: " + call.name;
        if (debug)
        {
            string location = null;
            if (call.args != null && call.args.ContainsKey("location"))
                location = call.args["location"];
            Debug.Log(DEBUG_PREFIX + LLMresult + ", " + location);
        }

        //Now we store the functionCall object in our history
        messageHistory.Add(new Content
        {
            role = "model",
            parts = new Part[] { new Part { functionCall = call } }
        });

        if (Enum.TryParse<MessageCommands>(call.name, out MessageCommands toolCommand))
        {
            switch (toolCommand)
            {
                // d. This is the function call for Google Web Search - Top Links
                case MessageCommands.isRAGWSGetTopLinks:
                    string query = null;
                    if (call.args != null && call.args.ContainsKey("query"))
                        query = call.args["query"];

                    if (!string.IsNullOrEmpty(query))
                    {
                        // Valid LLM Tool Call, query provided 
                        if (debug) Debug.Log(DEBUG_PREFIX + "Requesting Web Search for query: " + query);

                        // Add to messageHistory List<>
                        messageHistory.Add(new Content { role = "model", parts = new Part[] { new Part { functionCall = call } } });

                       //Send to the WebSearch component using the MessageBus
                        await _messageBus.Publish<RAGWSRequestMessage>(new RAGWSRequestMessage
                        {
                            Command = MessageCommands.isRAGWSGetTopLinks,
                            SenderId = ComponentId,
                            Content = query                     // The search/query is the content of the message
                        });
                        return;
                    }
                    else Debug.LogError(DEBUG_PREFIX + "WebSearch Top Links tool failed as no query was provided!");
                    break;


                // e. This is the function call to get the content of a specific URL
                case MessageCommands.isRAGWSGetURLContent:
                    string url = null;
                    if (call.args != null && call.args.ContainsKey("url")) url = call.args["url"];

                    if (!string.IsNullOrEmpty(url))
                    {
                        // Valid LLM Tool Call, provided an URL
                        if (debug) Debug.Log(DEBUG_PREFIX + "Requesting Web Content for URL: " + url);

                        // Add to messageHistory List<>
                        messageHistory.Add(new Content { role = "model", parts = new Part[] { new Part { functionCall = call } } });

                        await _messageBus.Publish<RAGWSRequestMessage>(new RAGWSRequestMessage
                        {
                            Command = MessageCommands.isRAGWSGetURLContent,
                            SenderId = ComponentId,
                            Content = url                       // De URL is the content of the message
                        });
                        return;
                    }
                    else Debug.LogError(DEBUG_PREFIX + "WebSearch URL Content tool failed as no URL was provided!");
                    break;

               
                // Unknown Function Call received
                default:
                    Debug.LogError(DEBUG_PREFIX + "Unknown Tool call received:" + call.name);
                    await SendMessage<TTSRequestMessage>(MessageCommands.isTTSSay, "Oh dear, I ran into an error! I can't execute the function" + call.name);
                    await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");
                    break;
            }
        }
        else Debug.LogError(DEBUG_PREFIX + "Unknown MessageCommand received/failed parsing");
        await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");                                //Unlock button
    }


    //====================================
    // B: Normal end user query to the LLM
    //====================================
    private async Task SendAppendUserQuery(ResponsePart part, bool withVision)
    {
        LLMresult = part.text;
        if (debug)
            Debug.Log(DEBUG_PREFIX + LLMresult);

        //Send to TTS and store the result in the history
        await SendMessage<TTSRequestMessage>(MessageCommands.isTTSSay, LLMresult);

        if (withVision)
        {
            if (messageHistory.Count > 0)
                messageHistory[messageHistory.Count - 1].parts[0].inline_data = null;
            RemoveLastMessageFromConversation();
        }
        AppendConversation(LLMresult, "model", null);
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


    //=======================
    // GEMINI REQUESTS
    //=======================
    [Serializable]                                  //PARTS section
    public class Part
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string text;                         //Do NOT insert empty values in the json string!
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public InlineData inline_data;              //Include image data
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public FunctionCall functionCall;           //Tool Calling 
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public FunctionResponse functionResponse;   //Tool Calling
    }

    [Serializable]                                  //PARTS are encapsulated by multiple CONTENT
    public class Content
    {
        public string role;
        public Part[] parts;                        //Array to match JSON [ 
    }

    [Serializable]
    public class InlineData
    {
        public string mime_type;                    //For Vision, provides the image type, mostly image/jpeg!
        public string data;
    }

    [Serializable]
    public class RequestData                        //!!!! Top level Gemini API json structure !!!!
    {
        public Content system_instruction;          // Single system_instruction field
        public Content[] contents;                  // Array of Parts and roles
        public Tools tools;                         // For Function Calling
    }

    [Serializable]
    public class FunctionCall                       //LLM responds with args which we place in a Dictionary
    {
        public string name;
        [JsonProperty(PropertyName = "args")] public Dictionary<string, string> args;   //Explicitly define the json propertyName!
    }


    //=======================
    //GEMINI RESPONSE LASSES
    //=======================
    [Serializable]
    public class FunctionResponse
    {
        public string name;
        //Google API only accepts "reponse" for function calls!
        // We use a generic "object" type here so we can assign any object to this field like GPSResponse, WeatherResponse etc 
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public object response;
    }

    [Serializable]
    public class ResponsePart
    {
        public string text;
        public FunctionCall functionCall;           //For functionCalls
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


    /// ================================
    /// GEMINI TOOL-CALLING 
    /// ================================
    //FUNCTION CALLING
    [Serializable]
    public class ParameterProperty
    {
        public string type;
        public string description;
    }

    [Serializable]
    public class ParameterSchema
    {
        public string type = "object";
        public Dictionary<string, ParameterProperty> properties;
        public string[] required = null;
    }

    [Serializable]
    public class FunctionDeclaration
    {
        public string name;
        public string description;
        public ParameterSchema parameters;
    }

    [Serializable]
    public class Tools
    {
        public FunctionDeclaration[] functionDeclarations;
    }

    [Serializable]
    public struct GoogleApiErrorDetail
    {
        public int code;
        public string message;
        public string status;
    }

    [Serializable]
    public struct GoogleApiErrorResponse
    {
        public GoogleApiErrorDetail error;
    }


    //==========================================
    // REMOTE AI Component classes
    //==========================================
    [Serializable]
    public class GPSResponseData
    {
        public float latitude;
        public float longitude;
        public string location_name;
    }

    [Serializable]
    public class WebSearchResponseData // NIEUW: Structuur voor de FunctionResponse
    {
        [JsonProperty(PropertyName = "web_search_results")] public string web_search_results;   //Response is a truncated HTML-tag & script-stripped string
    }


    //===========================================================================================
    // TOOLS IMPLEMENTATION - functionCalls!!!!
    //  We now provide our "logic" in the description field in prompt language rather than code !
    //===========================================================================================
    private Tools GetToolDefintions()
    {
        //==========================
        //===>>>  Web Search  <<<===
        //==========================
        FunctionDeclaration getTopLinks = new FunctionDeclaration
        {
            name = MessageCommands.isRAGWSGetTopLinks.ToString(),
            description = "Searches the web for recent information related to a specific query. Use this when the user asks for current news,"
                + " general facts, or information the LLM is not sure about. Requires a 'query' argument."
                + " When you decide to use a " + MessageCommands.isRAGWSGetTopLinks.ToString() + " function call because you cannot answer the question yourself "
                + " then you must always first ask the user whether the user is ok for you to search for the answer online!",
            parameters = new ParameterSchema
            {
                type = "object",
                required = new string[] { "query" }, // De LLM MOET de zoekterm leveren
                properties = new Dictionary<string, ParameterProperty>
                {
                    { "query", new ParameterProperty { type = "string", description = "The search query (e.g., 'IBM AI news' or 'current weather in Amsterdam')." } }
                }
            }
        };


        FunctionDeclaration getUrlContent = new FunctionDeclaration
        {
            name = MessageCommands.isRAGWSGetURLContent.ToString(),
            description = "Retrieves the content of a specific URL for detailed analysis or context extraction."
                + " Use this when the user explicitly provides a URL or asks for content from a specific website. Requires a 'url' argument.",
            parameters = new ParameterSchema
            {
                type = "object",
                required = new string[] { "url" }, // De LLM MOET de URL leveren
                properties = new Dictionary<string, ParameterProperty>
                {
                    { "url", new ParameterProperty { type = "string", description = "The full URL of the page to retrieve (e.g., 'https://www.ibm.com/topics/artificial-intelligence')." } }
                }
            }
        };

        

        //Add all other relevant AI code here!
        return new Tools
        {
            functionDeclarations = new FunctionDeclaration[]
            {
                getTopLinks,
                getUrlContent,
                //Add all other tools here
            }
        };
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.LLM_Vision_Service;               //Make yourself known

        Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<LLMRequestMessage>(HandleLLMRequestAsync);                              //Any incoming requests for the LLM 
        await _messageBus.Subscribe<NPCClickResponseMessage>(HandleNPCClickResponseMessageAsync);           //PTT button - UI
        await _messageBus.Subscribe<STTResponseMessage>(HandleSTTResponseMessageAsync);                     //Speech to Text
        await _messageBus.Subscribe<TTSResponseMessage>(HandleTTSResponseMessageAsync);                     //Text to Speech
        await _messageBus.Subscribe<RAGWSResponseMessage>(HandleRAGWSResponseMessageAsync);                 //Web Search
        await _messageBus.Subscribe<GPSResponseMessage>(HandleGPSResponseMessageAsync);                     //GPS
        await _messageBus.Subscribe<LangResponseMessage>(HandleLangResponseMessageAsync);                   //Language Manager
        await _messageBus.Subscribe<PreferenceResponseMessage>(HandlePreferenceResponseMessageAsync);       //Preference Manager

        Debug.Log(DEBUG_PREFIX + " started and subscribed to LLMRequestMessage.");
        aiD = GetComponent<AI_Director>();
        if (!aiD)
        {
            Debug.LogError(DEBUG_PREFIX + "Cannot find AI Director, aborting");
            return;
        }

        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else apiKey = api_Keys.GetAPIKey("Google_API_Key");

        if (apiKey == null)
            Debug.LogWarning(DEBUG_PREFIX + "Warning: API key not found, check API Key File!");

        selectedLLMString = selectedModel.ToString().Replace('_', '-').Replace('X', '.');
        if (debug)
            Debug.Log(DEBUG_PREFIX + "You have selected LLM: " + selectedLLMString);


        //UPDATED: Initialize the conversation history with a system_message, different than Groq cloud!
        InitializeSystemPrompt();

        //Now we setup the Vision part - first we get all available webcams
        WebCamDevice[] devices = WebCamTexture.devices;
        webCamDevices.AddRange(devices);
        webCamTexture = new WebCamTexture();
        numWebcamDevices = Mathf.Min(webCamDevices.Count, maxCameras);      //limit

        if (debug) Debug.Log(DEBUG_PREFIX + "CAMERAS: " + webCamDevices.Count + " maxCameras:" + maxCameras + " numWebCamDevices:" + numWebcamDevices);
    }


    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<LLMRequestMessage>(HandleLLMRequestAsync);
        await _messageBus.Unsubscribe<STTResponseMessage>(HandleSTTResponseMessageAsync);
        await _messageBus.Unsubscribe<NPCClickResponseMessage>(HandleNPCClickResponseMessageAsync);
        await _messageBus.Unsubscribe<TTSResponseMessage>(HandleTTSResponseMessageAsync);
        await _messageBus.Unsubscribe<RAGWSResponseMessage>(HandleRAGWSResponseMessageAsync);
        await _messageBus.Unsubscribe<GPSResponseMessage>(HandleGPSResponseMessageAsync);
        await _messageBus.Unsubscribe<LangResponseMessage>(HandleLangResponseMessageAsync);
        await _messageBus.Unsubscribe<PreferenceResponseMessage>(HandlePreferenceResponseMessageAsync);       //Preference Manager
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }



    //=====================================================================================
    // MESSAGE BUS HANDLER SECTION
    //=====================================================================================

    //We define a generic SendMessage method that can publish multiple Request message types, each must inherit from the RequestMessage template/abstract
    // this massively simplifies the command to send a request message to the message bus!
    public async Task SendMessage<T>(MessageCommands command, string content) where T : class, IContentMessage, new()
    {
        // Creëer een nieuw object van het opgegeven type
        var message = new T
        {
            Command = command,
            Content = content,
            SenderId = ComponentId
        };

        // Publiseer het generieke bericht
        await _messageBus.Publish<T>(message);
    }


    //Outgoing messages
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        LLMResponseMessage message = new LLMResponseMessage()
        {
            Command = MessageCommands.isSTTResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<LLMResponseMessage>(message);
    }


    // Message Protocol Handler, takes care of all incoming LLM request messages. 
    private async Task HandleLLMRequestAsync(LLMRequestMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isLLMTextToLLMWithVision:
                await TalkToLLM(message.Content, true, false);                             //Send the message content to the LLM
                break;

            case MessageCommands.isLLMTextToLLM:
                await TalkToLLM(message.Content, false, false);                             //Send the message content to the LLM
                break;

            case MessageCommands.isLLMNextCamera:
                NextCamera();
                break;

            //We received nonsense.
            default:
                await RespondMessage("Unknown message type", message.SenderId, true);
                break;
        }
    }


    //Here we handle incoming messages from the NPC Click Handler component
    private async Task HandleNPCClickResponseMessageAsync(NPCClickResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        if (message.IsError)
        {
            if (debug)
                Debug.LogError(DEBUG_PREFIX + message.SenderId + " responded with an error: " + message.Content);

            //Attempt to restart the click handler
            await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");
        }

        switch (message.Command)
        {
            //Step 1 - User clicked
            case MessageCommands.isNPCClick:
                //Tell STT to start recording 
                await SendMessage<STTRequestMessage>(MessageCommands.isSTTStartRecording, "");
                break;

            //Step 2 - User is done talking and we want to get the transcribed text 
            case MessageCommands.isNPCRelease:
                await SendMessage<STTRequestMessage>(MessageCommands.isSTTStopRecording, "");
                break;

            //Generic NPC catch for NPC Error Responses
            case MessageCommands.isNPCResponse:
                //TBD
                break;

            //Nonsense handling
            default:
                Debug.LogError(DEBUG_PREFIX + message.SenderId + " responded with an unexpected message");
                break;
        }
        await Task.Delay(0); //simulate some processing delay
    }


    //Here we handle incoming messages from the active STT component
    private async Task HandleSTTResponseMessageAsync(STTResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);


        //If STT ran into some error lets just try to reset it 
        if (message.IsError)
        {
            Debug.LogError(DEBUG_PREFIX + message.SenderId + " reported with an error: " + message.Content);
            //Lets just ask the STT to re-initialize again
            await SendMessage<STTRequestMessage>(MessageCommands.isSTTInitializeMicrophone, "");
            return;
        }

        //Now we check what response we got
        switch (message.Command)
        {
            case MessageCommands.isSTTResponse:
                //Nothing to do when there is no error
                break;

            case MessageCommands.isSTTTranscription:
                if (debug) Debug.Log(DEBUG_PREFIX + "Received Transcription text from STT:" + message.Content);
                await TalkToLLM(message.Content, currentCamera>0 , false );                                             //when camera is on we send the webcamtexture
                break;

            default:
                Debug.LogError(DEBUG_PREFIX + message.SenderId + " responded with an unexpected message");
                break;
        }
    }


    //Here we handle incoming messages from the active TTS component
    private async Task HandleTTSResponseMessageAsync(TTSResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //If STT ran into some error lets just try to reset it 
        if (message.IsError)
        {
            Debug.LogError(DEBUG_PREFIX + message.SenderId + " reported with an error: " + message.Content);
            //Lets just enable the button again
            await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");
            return;
        }

        //Now we check what response we got
        switch (message.Command)
        {
            case MessageCommands.isTTSResponse:
                //If there is no error then there is nothing else to do than re-enable the microphone button?
                break;

            default:
                Debug.LogError(DEBUG_PREFIX + message.SenderId + " responded with an unexpected message");
                break;
        }
        //Now lets unlock the button again 
        await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");
    }


    //Received a Response from the WebSearch AI component, convert its json structure and pass it on to the LLM 
    private async Task HandleRAGWSResponseMessageAsync(RAGWSResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //If RAG ran into some error lets just try to reset it 
        if (message.IsError)
            Debug.LogError(DEBUG_PREFIX + message.SenderId + " reported with an error: " + message.Content);


        //Build json functionCall Response structure for the LLM
        WebSearchResponseData responseData = new WebSearchResponseData
        {
            web_search_results = message.Content                    //Get the WebSearch component results (string with context)
        };

        FunctionResponse functionResponse = new FunctionResponse    //Construct a suitable functionCall structure with the results
        {
            name = message.originatingMessageCommand.ToString(),    //Crucial, this way the LLM know what request this reponse belongs to       
            response = responseData
        };

        //Add it to the history
        messageHistory.Add(new Content
        {
            role = "function",
            parts = new Part[] { new Part { functionResponse = functionResponse } }
        });

        // Send the messageHistory with the new Response content to the LLM using the REST API
        await TalkToLLM("", false, true);

    }


    //GPS Component Responses
    private async Task HandleGPSResponseMessageAsync(GPSResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), "=>" + message.location + ",lat/lon=" + message.lattitude + "/" + message.longitude);

        //If GPS ran into some error lets just try to reset it 
        if (message.IsError)
        {
            Debug.LogError(DEBUG_PREFIX + message.SenderId + " reported with an error: " + message.Content);
            return;
        }

        switch (message.Command)
        {
            case MessageCommands.isGPSResponse:
            case MessageCommands.isGPSBroadcast:
                aiD.lat = message.lattitude;                                            //lets copy the response from the GPS module so we can use it in other components 
                aiD.lon = message.longitude;

                //We now transfer the GPS results into a structured json object for the LLM
                GPSResponseData responseData = new GPSResponseData
                {
                    latitude = message.lattitude,
                    longitude = message.longitude,
                    location_name = message.location
                };

                //Now create a correct FunctionResponse object
                FunctionResponse functionResponse = new FunctionResponse
                {
                    name = MessageCommands.isGPSCurrentLocationData.ToString(),         //note that we 1:1 map the MC command name to the function call name!
                    response = responseData
                };

                //Now we add the new content to our history
                messageHistory.Add(new Content
                {
                    role = "function",                                                  //FUNCTION CALLING!
                    parts = new Part[] { new Part { functionResponse = functionResponse } }
                });

                await TalkToLLM("", false, true);                                       //This is not a user message but a Tool Call so silent for the user!
                break;

            default:
                Debug.LogError(DEBUG_PREFIX + message.SenderId + " responded with an unexpected message");
                break;
        }

        return;
    }


    //===========================
    //Language Manager Responses
    //===========================
    //LanguageManager Responses, store in LOCAL CACHE to support synchronous calls over our async messagebus!
    private Task HandleLangResponseMessageAsync(LangResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content, message.TargetId);

        //Set global variable
        langMgrGetLocalizedDict.TryAdd(message.key, (object)message.Content);

        return Task.CompletedTask;
    }


    //Helper to pull the string from the Language Manager via the messageBus, acts as a SYNCHRONOUS request!
    // Populates a LOCAL CACHE
    // Sends message and awaits the response using HandleLangResponseMessageAsync
    private async Task<string> LanguageManagerGetLocalized(string key)
    {
        //Check whether we already have the information in our local cache
        if (langMgrGetLocalizedDict.TryGetValue(key, out object cachedResult))
        {
            if (debug) Debug.Log(DEBUG_PREFIX + "Cache hit for key " + key);
            return cachedResult.ToString();
        }

        if (debug) Debug.Log(DEBUG_PREFIX + "Cache miss for key " + key + ", requesting from LanguageManager");

        //First we send the Request to Language Manager
        await SendMessage<LangRequestMessage>(MessageCommands.isLangGetLocalized, key);
        
        //Now we wait until the global string is set by the Response Handler OR a timeout occurs
        var result = await WaitForDictValueAsync(key, TimeSpan.FromSeconds(3));
        if (result == null)
        {
            Debug.LogError(DEBUG_PREFIX + "Did not receive a response from LanguageManager for key " + key);
            return null;
        }
        
        return result.ToString();
    }


    //Wait until we have a value in the dictionary
    private async Task<object> WaitForDictValueAsync(string key, TimeSpan timeout)
    {
        var startTime = Time.time;
        object result = null;

        while (!langMgrGetLocalizedDict.TryGetValue(key, out result))
        {
            if (Time.time - startTime > (float)timeout.TotalSeconds)
                return null;
            await Task.Yield();
        }
        return result;
    }


    //============================
    //Preference Manager Responses
    //============================
    private Task HandlePreferenceResponseMessageAsync(PreferenceResponseMessage message) 
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content, message.TargetId);

        switch (message.Command) 
        {
            case MessageCommands.isPreferencesResponse:
                if (!string.IsNullOrEmpty(message.selectedLanguage)) 
                {
                    prefMgrCurrentLang = message.selectedLanguage;                                          //Cache it until it changes
                    langMgrGetLocalizedDict.Clear();
                    if (debug) Debug.Log(DEBUG_PREFIX+"Dictionary CLEARED!");
                }
                if (!string.IsNullOrEmpty(message.userName)) 
                    AppendConversation("The user's name is "+ message.userName + "\n", "user", null);       //Just add it to the history so the LLM is aware of the user's name
                break;    
            
            default:
                Debug.LogError(DEBUG_PREFIX + "Error: unknown MessageCommand " + message.Command.ToString());
                break;
        }

        return Task.CompletedTask;
    }
}
