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

/// <summary>
/// Component for GroqCloud models with Vision API and Tool-Calling
///  MessageBus:    YES
///  Tool-Calling:  YES
/// </summary>


public class LLM_Groq_Vision : MonoBehaviour, ILlmVisionService, IAIComponent
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


    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ModelCapabilitiesAttribute : System.Attribute
    {
        public bool SupportsVision { get; }
        public ModelCapabilitiesAttribute(bool supportsVision)
        {
            SupportsVision = supportsVision;
        }
    }

    //LLM configuration
    const string apiURI = "https://api.groq.com/openai/v1/chat/completions";
    private enum LLMModel { 
        [ModelCapabilities(false)] llama_3X1_8b_instant,                                //TEXT ONLY
        [ModelCapabilities(false)] llama_3X3_70b_versatile,                             //TEXT ONLY
        [ModelCapabilities(false)] openaiYgpt_oss_20b,                                  //TEXT ONLY
        [ModelCapabilities(false)] openaiYgpt_oss_120b,                                 //TEXT ONLY
        [ModelCapabilities(true)] meta_llamaYllama_4_scout_17b_16e_instruct,            //VISION
        [ModelCapabilities(true)] meta_llamaYllama_4_maverick_17b_128e_instruct         //VISION
    }

    [SerializeField] private LLMModel selectedModel;
    string selectedLLMString;
    private string LLMresult = "Waiting";

    private List<GroqMessage> messageHistory = new List<GroqMessage>();                 //This stores the message history for the LLM in object format, must Serialize to JSON before sending
    private string systemInstructionString = "";
    private Dictionary<MessageCommands, string> pendingToolCalls = new Dictionary<MessageCommands, string>();   //Toolcall-ID's - required in Groq!
    private bool isProcessingTools = false;
     
    //Language Manager
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
    const string DEBUG_PREFIX = "LLM_GROQ_VISION: ";      //prefix we use for debugging


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
        systemInstructionString += message;
        if (debug) Debug.Log(DEBUG_PREFIX+"System Instruction updated: " + message);
    }


    //Creates a new system prompt and adds the suffix, eg. to tell the LLM to respond in another language
    public void InitializeSystemPrompt()
    {
        string prompt;
        DateTime currentDate = DateTime.Now;

        //CONSTRUCT PROMPT - STEP 1: WHO IS THIS
        prompt = "Your name is " + aiD.whoAmI + "You are a helpful assistant. " 
                + "Use tools only when necessary based on explicit user requests. Do not call tools to set user information unless the user provides it.";

        //STEP 2: HOW LONG CAN THE RESPONSE BE
        if (aiD.maxNumberOfWords > 0)
            prompt += "\nAnswer all questions in maximum " + aiD.maxNumberOfWords + " words\n";

        // STEP 3: GIVE IT A NOTION OF TIME & AVOID IT REINTRODUCING ITSELF
        prompt += "\nToday is " + currentDate.ToShortDateString();
        prompt += "\nAssume the user already knows your name and only tell the user your location or the time when explicitly asked.";    

        //STEP 4: Add system instruction and general context
        prompt += "\n" + systemInstructionString;
        prompt += CreatePromptContext(aiD.context);

        if (debug)
            Debug.Log(DEBUG_PREFIX + prompt);

        //Initialize the message History and post the system message
        messageHistory = new List<GroqMessage>();                       //empty List
        messageHistory.Add(new GroqMessage 
        {                            
            role = "system", 
            content = prompt                                            //Note: Groq uses 'content' vs 'parts'
        });
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


    private void AppendConversation(string text, string role, string base64Image=null, string toolCallId=null)
    {
        // User or system instruction
        string groqRole = (role == "model") ? "assistant" : role;
        GroqMessage newMessage = new GroqMessage { role = groqRole };

        //Set toolcallId if defined
        if (!string.IsNullOrEmpty(toolCallId))                  //TOOLS
        {
            newMessage.tool_call_id = toolCallId;
            newMessage.content = text;
        }
        else if (!string.IsNullOrEmpty(base64Image))            //IMAGE
        {
            var contentParts = new List<object>();
            if (!string.IsNullOrEmpty(text))
            {
                contentParts.Add(new {
                    type = "text",
                    text = text
                });
            }

            contentParts.Add( new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:image/jpeg;base64,{base64Image}"
                }
            });
            newMessage.content = contentParts;
        }
        else                                                    //BASIC TEXT
            newMessage.content = text;

        messageHistory.Add(newMessage);
    }


    //All possible context info is captured in global variables and sent to the LLM when relevant
    // Mechanism to deal with stateless request/response message architecture
    // Hence clear irrelevant context as soon as its used!
    private async Task<string> BuildCompleteContextString()
    {
        string currentContext = "";
        DateTime currentDate = DateTime.Now;

        //We need to determine the language suffix for the LLM, we use a synchronous MessageBus call option, this will first try to pull from the cache to avoid message overhead!
        await Task.Delay(0);
        langMgrLangSuffix =  await LanguageManagerGetLocalized("responseLanguage");                                   
        
        if (langMgrLangSuffix == null) 
        {
            langMgrLangSuffix = "\nRespond in US English";
            Debug.LogWarning(DEBUG_PREFIX+"Did not receive response from the language manager, assuming US English!");
        } 
        
        currentContext += langMgrLangSuffix;                                                                //add to the context explicitly for each query

        return currentContext;
    }


    //======================
    // REST API HANDLING
    //======================

    //Now has a functionCall/Tool Call parameter 
    private async Task TalkToLLM(string mesg, bool withVision = false, bool isToolCallLoop = false)         //no more context parameter, ensure all context is actual in global parameters!
    {
        if (debug) Debug.Log(DEBUG_PREFIX + (isToolCallLoop ? "Retrying with tool results..." : mesg));

        //Add the context to the prompt for RAG, but only if this is a user message and not an internal tool call message to the LLM
        if (!isToolCallLoop)
        {
            string base64Image = null;

            if (withVision)
            {
                if (!webCamTexture)
                {
                    string seeNothing = await LanguageManagerGetLocalized("see_nothing");                   //Synchronous MessageBus call option
                    seeNothing = seeNothing == null ? "Please turn on your camera!" : seeNothing;
                    await SendMessage<TTSRequestMessage>(MessageCommands.isTTSSay, seeNothing);
                    return;
                }
                base64Image = WebCamTextureToBase64String(webCamTexture);
            }
            string context = await BuildCompleteContextString();
            AppendConversation(mesg+context,"user", base64Image);
        }

        //Build Groq Request body
        GroqRequest requestBody = new GroqRequest
        {
            model = selectedModel.ToString().Replace('_','-').Replace('X','.').Replace('Y','/'),            //Convert to Groq codes
            messages = messageHistory,                                                                      //We pass on our complete history
            tools = GetToolDefinitions(),                                                                   //Here are our tools
            tool_choice = "auto"
        };
        
        //Serialize into a JSON
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        string jsonRequestBody = JsonConvert.SerializeObject(requestBody, settings);

        if (debug)
            Debug.Log(DEBUG_PREFIX + "Sending JSON: "+jsonRequestBody);

        //Prepare REST API call
        UnityWebRequest request = new UnityWebRequest(apiURI, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequestBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);
        request.SetRequestHeader("Content-Type", "application/json");

        //Ready to fire off the HTTP request to the API!
        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            if (debug) Debug.Log(DEBUG_PREFIX+"ResponseText: " + responseText);

            var groqResponse = JsonConvert.DeserializeObject<GroqCloudResponse>(responseText);
            if (groqResponse.choices != null && groqResponse.choices.Length>0)
            {
                var choice = groqResponse.choices[0];
                
                //First clean out any null parameters
                if (choice.message.tool_calls != null)
                {
                    foreach (var toolCall in choice.message.tool_calls)
                    {
                        // Als arguments null is of de string "null", vervang door "{}"
                        if (string.IsNullOrEmpty(toolCall.function.arguments) || toolCall.function.arguments == "null")
                        {
                            toolCall.function.arguments = "{}";
                        }
                    }
                    
                    //Now we clean any content fields within Function Calls as most LLMs don't accept that
                    if (choice.message.content != null)
                    {
                        Debug.LogWarning(DEBUG_PREFIX + "Detected function call with content field, removing: " + choice.message.content);
                        choice.message.content = null;
                    }
                }

                messageHistory.Add(choice.message);
                if (choice.finish_reason == "tool_calls")                                                                   //Tool call response
                {
                    isProcessingTools = true;                                                                               //Gatekeeper
                    //Lets first register all incoming tool call requests before handling them so we don't miss any
                    foreach(var toolCall in choice.message.tool_calls)
                    {
                        //Store the toolCall 
                        if (Enum.TryParse<MessageCommands>(toolCall.function.name, out MessageCommands cmd)) 
                        {
                            if (debug)
                                Debug.Log(DEBUG_PREFIX+"Stored toolcall " + cmd + " with id " + toolCall.id);
                            pendingToolCalls[cmd] = toolCall.id;
                        }
                    }

                    //Second pass, now we actually process the requests 
                    foreach (var toolCall in choice.message.tool_calls)
                    {   
                        if (Enum.TryParse<MessageCommands>(toolCall.function.name, out MessageCommands cmd))
                        {
                            string result = await ProcessFunctionCallAsync(toolCall.function.name, toolCall.function.arguments);
                            AppendConversation(result, "tool", null, toolCall.id);
                        }
                        else
                        {
                            Debug.LogError(DEBUG_PREFIX+"Received an unknown tool-call command from the LLM");              //Inform the user
                            AppendConversation("Error: Tool not found or not implemented.", "tool", null, toolCall.id);     //Inform the LLM
                        }
                    }    

                    isProcessingTools = false;                                                                              //Unblock                          
                    if (pendingToolCalls.Count == 0)
                        await TalkToLLM(null, false, true);                                                                 //send toolcall to the LLM
                }
                else                                                                                                        //Regular text response
                {
                    string rawContent = choice.message.content?.ToString();                                                 //Explicitly cast object to string
                    string cleanText = rawContent;
                    
                    if (!string.IsNullOrEmpty(rawContent) && rawContent.StartsWith("["))
                    {
                        int closingBracket = rawContent.IndexOf("]");
                        if (closingBracket >0)
                        {
                            cleanText = rawContent.Substring(closingBracket+1).Trim();                                      //Actual response text 
                        }
                    }
                    LLMresult = cleanText;
                    await SendMessage<TTSRequestMessage>(MessageCommands.isTTSSay, LLMresult);                              //Speak the LLM repsonse out loud
                    await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");
                }
            }
        }
        else
        {
            Debug.LogError(DEBUG_PREFIX + "Groq Error: "+ request.downloadHandler.text);
            await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");                                //Unlock button
        }
    }


    //=================================================================================================================
    // A: Tool call message for the LLM, broken out in a dedicated method to improve readability of the code
    //  This essentially converts the functionCall from the LLM to a MessageBus Request and updates the messageHistory
    //  => the ResponseMessageHandler method will send the response back to the LLM
    //=================================================================================================================


    //Main method that is called when a function call is received from the LLM, now need to disect and send to appropriate component over the MessageBus
    // returns a message for the LLM that we append to the conversation
    private async Task<string> ProcessFunctionCallAsync(string functionName, string jsonArguments)
    {

        LLMresult = "Tool Call Requested: " + functionName;
        var args = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonArguments) ?? new Dictionary<string, string>();

        if (debug)
        {
            args.TryGetValue("location", out string loc);
            Debug.Log(DEBUG_PREFIX + LLMresult + ", " + (loc ?? "no location"));
        }

        if (Enum.TryParse<MessageCommands>(functionName, out MessageCommands toolCommand))
        {
            switch (toolCommand)
            {
                // d. This is the function call for Google Web Search - Top Links
                case MessageCommands.isRAGWSGetTopLinks:
                   if (args.TryGetValue("query", out string q) && !string.IsNullOrEmpty(q))
                    {   
                        // Valid LLM Tool Call, query provided 
                        if (debug) Debug.Log(DEBUG_PREFIX + "Requesting Web Search for query: " + q);

                       //Send to the WebSearch component using the MessageBus
                        await _messageBus.Publish<RAGWSRequestMessage>(new RAGWSRequestMessage
                        {
                            Command = MessageCommands.isRAGWSGetTopLinks,
                            SenderId = ComponentId,
                            Content = q                    // The search/query is the content of the message
                        });
                        return $"Searching web for '{q}'...";
                    }
                    return "Error: No query provided for search.";


                // e. This is the function call to get the content of a specific URL
                case MessageCommands.isRAGWSGetURLContent:
                    if (args.TryGetValue("url", out string url) && !string.IsNullOrEmpty(url))
                    {   
                        // Valid LLM Tool Call, provided an URL
                        if (debug) Debug.Log(DEBUG_PREFIX + "Requesting Web Content for URL: " + url);
                        await _messageBus.Publish<RAGWSRequestMessage>(new RAGWSRequestMessage
                        {
                            Command = MessageCommands.isRAGWSGetURLContent,
                            SenderId = ComponentId,
                            Content = url                       // De URL is the content of the message
                        });
                        return $"Extracting content from {url}...";
                    }
                    return "Error: No URL provided.";

                // Unknown Function Call received
                default:
                    Debug.LogError(DEBUG_PREFIX + "Unknown Tool call received:" + functionName);
                    return "Error: Function not implemented.";
            }
        }
         return "Unknown MessageCommand received/failed parsing";
    }


    //====================================
    // B: Normal end user query to the LLM
    //====================================


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
    // GROQ REQUESTS
    //=======================

    [Serializable]
    public class MessagePart // Voor Vision
    {
        public string type; // "text" of "image_url"
        public string text;
        public ImageUrl image_url;
    }

    [Serializable]
    public class ImageUrl
    {
        public string url; // Bevat "data:image/jpeg;base64,{base64_string}"
    }   


    //=======================
    //GROQ RESPONSE LASSES
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
    public class GroqRequest
    {
        public string model;
        public List<GroqMessage> messages;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<GroqTool> tools;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string tool_choice = "auto";
    }

    [Serializable]
    public class GroqMessage
    {
        public string role; // "system", "user", "assistant", of "tool"
    
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public object content;             // Standard text or image data
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ToolCall> tool_calls;  // Als de assistent een tool aanroept
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string tool_call_id;        // Verplicht bij role "tool" om resultaat te koppelen
    }

    [Serializable]
    public class GroqContentPart
    {
        public string type; // "text" of "image_url"
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string text;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public GroqImageUrl image_url;
    }

    [Serializable]
    public class GroqImageUrl
    {
        public string url; // Bevat: "data:image/jpeg;base64,{base64_string}"
    }

    [Serializable]
    public class GroqCloudResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    public class Choice
    {
        public GroqMessage message;
        public string finish_reason;
    }

    /// ================================
    /// GROQ TOOL-CALLING 
    /// ================================
    [Serializable]
    public class GroqTool
    {
        public string type = "function";
        public GroqFunction function;
    }

    [Serializable]
    public class GroqFunction
    {
        public string name;
        public string description;
        public GroqParameters parameters;
    }

    [Serializable]
    public class GroqParameters
    {
        public string type = "object";
        public Dictionary<string, GroqProperty> properties;
        public List<string> required;
    }

    [Serializable]
    public class GroqProperty
    {
        public string type;
        public string description;
    }

    [Serializable]
    public class ToolCall
    {
        public string id; // De unieke ID die je moet terugsturen bij het resultaat
        public string type = "function";
        public FunctionCallDetail function;
    }

    [Serializable]
    public class FunctionCallDetail
    {
        public string name;
        public string arguments; // Let op: dit is een JSON-string, moet je nog parsen!
    }

    /// ================================
    /// GROQ ERROR CLASSES
    /// ================================
    [Serializable]
    public class GroqErrorResponse
    {
        public GroqError error;
    }

    [Serializable]
    public class GroqError
    {
        public string message;
        public string type;
        public string code; // Bijv. "rate_limit_exceeded"
        public string failed_generation;
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
    private List<GroqTool> GetToolDefinitions()
    {
        List<GroqTool> tools = new List<GroqTool>();

        //==========================
        //===>>>  Web Search  <<<===
        //==========================
        tools.Add(new GroqTool 
        {
            function = new GroqFunction 
            {
                name = MessageCommands.isRAGWSGetTopLinks.ToString(),
                description = "Searches the web for recent information related to a specific query. Use this when the user asks for current news,"
                    + " general facts, or information the LLM is not sure about. Requires a 'query' argument."
                    + " If the query is non-location-specific (e.g., 'latest news' or 'nearby events') then you must use the known GPS location to provide local context.",
                parameters = new GroqParameters         //SEARCH QUERY parameter
                {
                    type = "object",
                    properties = new Dictionary<string, GroqProperty>
                    {
                        { 
                            "query", 
                            new GroqProperty { type = "string", description = "The search query (e.g., 'latest AI news' or 'current events in Amsterdam')."  } 
                        }
                    },
                    required = new List<string> { "query" } // De LLM MOET dit veld invullen
                }
            }
         });


        tools.Add(new GroqTool 
        {
            function = new GroqFunction 
            {
                name = MessageCommands.isRAGWSGetURLContent.ToString(),
                description = "Retrieves the content of a specific URL for detailed analysis or context extraction."
                    + " Use this when the user explicitly provides a URL or asks for content from a specific website. Requires a 'url' argument.",
                parameters = new GroqParameters             //URL parameter
                {
                    type = "object",
                    properties = new Dictionary<string, GroqProperty>
                    {
                        { 
                            "url", 
                            new GroqProperty { type = "string", description = "The full URL of the page to retrieve (e.g., 'https://www.ibm.com/topics/artificial-intelligence')."  } 
                        }
                    },
                    required = new List<string> { "url" }, // De LLM MOET de URL leveren
                }
            }
        }); 


        //Add all other relevant AI code here!
        return tools;
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                                                           //Ensure we link to the active bus
        ComponentId = ComponentID.LLM_Vision_Service;                                                       //Make yourself known

        Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<LLMRequestMessage>(HandleLLMRequestAsync);                              //Any incoming requests for the LLM 
        await _messageBus.Subscribe<NPCClickResponseMessage>(HandleNPCClickResponseMessageAsync);           //PTT button - UI
        await _messageBus.Subscribe<STTResponseMessage>(HandleSTTResponseMessageAsync);                     //Speech to Text
        await _messageBus.Subscribe<TTSResponseMessage>(HandleTTSResponseMessageAsync);                     //Text to Speech
        await _messageBus.Subscribe<RAGWSResponseMessage>(HandleRAGWSResponseMessageAsync);                 //Web Search
        await _messageBus.Subscribe<GPSResponseMessage>(HandleGPSResponseMessageAsync);                     //GPS Toolcalling DISABLED, using BROADCAST instead!
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
        else apiKey = api_Keys.GetAPIKey("Groq_API_Key");

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

        //Now we enable/disable the camera button depending on the selected LLM model & send the info to the UI component 
        var type = selectedModel.GetType();
        var memInfo = type.GetMember(selectedModel.ToString());
        var attributes = memInfo[0].GetCustomAttributes(typeof(ModelCapabilitiesAttribute), false);
        bool supportsVision = (attributes.Length>0) && ((ModelCapabilitiesAttribute)attributes[0]).SupportsVision;
        var message = new UIRequestMessage
        {
            Command = MessageCommands.isCameraOnOff,
            isOn = supportsVision,
            SenderId = ComponentId
        };
        await _messageBus.Publish(message);

        if (debug) 
            Debug.Log(DEBUG_PREFIX + "CAMERAS: " + (supportsVision ? webCamDevices.Count + " maxCameras:" + maxCameras + " numWebCamDevices:" + numWebcamDevices : "OFF"));
        
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
            Debug.LogError(DEBUG_PREFIX + message.SenderId + " reported with an error: " + message.Content);
        else 
        //Now we check what response we got
        switch (message.Command)
        {
            case MessageCommands.isTTSResponse: 
                //Nothing to do
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

        if (pendingToolCalls.TryGetValue(message.originatingMessageCommand, out string toolId))
        {
            AppendConversation(message.Content, "tool", null, toolId);          //add tool-result to the conversation with corresponding toolCall-Id
            pendingToolCalls.Remove(message.originatingMessageCommand);         //remove from the pending toolID list
            if (!isProcessingTools && pendingToolCalls.Count == 0)
                await TalkToLLM(null, false, true);                             //When done, send to LLM 
        }
        else 
            Debug.LogError(DEBUG_PREFIX+"Could not find toolCallId for " + message.originatingMessageCommand);
    }


    //GPS Component Responses - broadcast, not used directly by the LLM so we don't send this to the LLM directly
    private Task HandleGPSResponseMessageAsync(GPSResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), "=>" + message.location + ",lat/lon=" + message.lattitude + "/" + message.longitude);

        switch (message.Command)
        {
            case MessageCommands.isGPSResponse: 
            case MessageCommands.isGPSBroadcast:
                if ((aiD.lat != message.lattitude)||(aiD.lon!=message.longitude))       //We changed our GPS location!
                {
                    aiD.lat = message.lattitude;                                        //lets copy the response from the GPS module so we can use it in other components 
                    aiD.lon = message.longitude;
                    aiD.gpsLocation = message.location; 
                    string gpsResult = $"Current location: {message.location} (Latitude: {message.lattitude}, Longitude: {message.longitude})";
                    AppendConversation(gpsResult, "user", null);                        //Add to the conversation history so the LLM knows next time when we send it a message

                    if (debug) 
                        Debug.Log(DEBUG_PREFIX+"Appended new location to conversation history: " + gpsResult);
                }
                break;

            default:
                Debug.LogError(DEBUG_PREFIX + message.SenderId + " responded with an unexpected message");
                break;
        }

        return Task.CompletedTask;
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
            if (debug) 
                Debug.Log(DEBUG_PREFIX + "Cache hit for key " + key);
            return cachedResult.ToString();
        }

        if (debug) 
            Debug.Log(DEBUG_PREFIX + "Cache miss for key " + key + ", requesting from LanguageManager");

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

                //Selected language
                if (!string.IsNullOrEmpty(message.selectedLanguage)) 
                {
                    prefMgrCurrentLang = message.selectedLanguage;                                          //Cache it until it changes
                    langMgrGetLocalizedDict.Clear();
                    if (debug) Debug.Log(DEBUG_PREFIX+"Dictionary CLEARED!");
                }
                
                //User's name
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
