using System;
using System.Threading.Tasks;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Defines the contract for all AI services as well as all available MessageCommands for the MessageBus.
/// </summary>
public interface IAiService                                     // NEW ARCHITECTURE: MUST CONVERT TO THE NEWER IAIComponent TO MAKE SURE EVERYTHING IS ASYNC COMPATIBLE!!!!
{
    Task Init(MessageBus _messageBus = null);                                                //Synchroneous
}


/// <summary>
/// Defines the contract for any Speech-to-Text service.
/// </summary>
public interface ISttService : IAiService
{
    void SelectLanguage(string langCode);                       //command: isSTTSelectLanguage, content: langCode 
    bool InitializeMicrophone(int duration);                    //command: isSTTInitializeMicrophone, content: duration
}


public class LLMResponse
{
    public string Text { get; set; }
    public string ToolCallJson { get; set; }
    public bool RequiresToolCall { get; set; }
}


/// <summary>
/// Defines the contract for any Large Language Model service.
/// </summary>
public interface ILlmService : IAiService
{
    void TextToLLM(string input, string context);               //command: isLLMTextToLLM, content: input, context
    void AppendSystemInstruction(string instruction);           //command: isLLMAppendSystemInstruction, content: instruction
    void CreateSystemPrompt();                                  //command: isLLMCreateSystemPrompt, no content
}


public interface ILlmVisionService : IAiService
{
    bool isCameraOn();                                          //command: isLLMCameraOn, no content
    void NextCamera();                                          //command: isLLMNextCamera, no content
    void CameraOff();                                           //command: isLLMCameraOff, no content   
    void AppendSystemInstruction(string instruction);           //command: isLLMAppendSystemInstruction, content: instruction
    void InitializeSystemPrompt();                              //command: isLLMCreateSystemPrompt, no content
}


/// <summary>
/// Defines the contract for any Text-to-Speech service.
/// </summary>
public interface ITtsService : IAiService
{
    Task Say(string input);
    void SelectLanguage(string langID);
    string NextVoice();
    void SetVoice(string selectedVoiceName);
    string GetCurrentVoice();
}


/// <summary>
/// Defines the contract for getting context from a Web search
/// </summary>
public interface IRagWebService : IAiService
{
    Task<string> GetTopLinks(string question, int topn);
    Task<string> GetURLContent(string url, int maxLen);
}


/// <summary>
/// Defines the contract for all Location Services
/// </summary>
public interface ILocationService : IAiService
{
    string GetLocationName();                                         //returns the string name from the Location services API
}

public interface ILangService : IAiService {}


/// <summary>
/// MessageBus Interface, ensures all components have a consistent way to start and stop their message bus subscription
/// </summary>
public interface IAIComponent
{
    ComponentID ComponentId {get; set;}
    
    // Initializes the component and sets up its message bus subscription.
    // It is a long-running task, hence the Task return type.
    Task Init(MessageBus messageBus);

    // Disposes of the component and unsubscribes from the message bus.
    Task Stop();

    Task RespondMessage(string content, ComponentID senderId, bool isError);    //Return to Request sender
}


//=====================================================================================================
//This specifies all of the available COMMANDS that the Director can send instructions to AI components 
// Each command should align with the corresponding AI Component public method. 
// The AI Components respond with isXYZResponse, the Director sends isXYZCommand
//=====================================================================================================
[Serializable]
public enum MessageCommands     //TO BE SIMPLIFIED IN THE FUTURE INTO A FEW STANDARD COMMAND TYPES!
{
    isTest,                                                                                                     //A test command, can be used to test the message bus          
    isNone,                                                                                                     //Used to determine Multistep Tool Chain handling!
    isNPCClickEnable, isNPCClick, isNPCRelease, isNPCResponse,                                                  //NPC Click Handler commands                                              
    isSTTSelectLanguage, isSTTInitializeMicrophone, isSTTStartRecording, isSTTStopRecording, isSTTResponse,     //STT commands
    isSTTTranscription,                                                                                         //Actual SST results 
    isLLMTextToLLM, isLLMAppendSystemInstruction, isLLMCreateSystemPrompt, isLLMResponse,                       //LLM commands
    isLLMTextToLLMWithVision, isLLMCameraOn, isLLMNextCamera, isLLMCameraOff, isLLMWithVisionResponse,          //LLM vision commands
    isTTSSay, isTTSSelectLanguage, isTTSNextVoice, isTTSSetVoice, isTTSCurrentVoice, isTTSResponse,             //TTS commands
    isRAGWSGetTopLinks, isRAGWSGetURLContent, isRAGWSResponse,                                                  //RAG Web Service commands
    isGPSCurrentLocationString, isGPSCurrentLocationData, isGPSCurrentCountry, isGPSResponse, isGPSBroadcast,   //Location services commands 
    isLangCurrentLang, isLangGetLocalized, isLangNextLang, isLangResponse,                                      //Language Manager commanda 
    isInitPreferences, isPreferencesUpdate, isPreferencesResponse,                                              //Preferences Manager commands
    isUIOnOff, isUINPCOnOff, isCameraOnOff, isUIResponse,
    isNPCCharacterName, isNPCNextSkin, isNPCNextEyes,                                                           //NPC Looks
    isNPCExpressionSet, isNPCExpressionReset, isNPCResetAllExpressions, isNPCExpressionResponse                 //Expressions/Emotion commands - SyncAllBlendShapes
}


[Serializable]
public enum ComponentID     //For now a simple enum, consider migrating to a certificate based PKI system to confirm authenticity
{
    Broadcast,                          //send to anyone 
    AI_Registrar,
    AI_Director,
    NPC_Click_Handler,
    STT_Service,
    LLM_Vision_Service,
    TTS_Service,
    TTI_Service,
    RAG_Web_Service,
    GPS_Location_Service,
    Language_Manager, 
    Preferences_Manager,
    UI_Manager,
    NPC_Looks_Handler,
    NPC_Expressions_Handler,
}
