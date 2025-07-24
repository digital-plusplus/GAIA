using UnityEngine;
using System;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Central script where we store the messagebus message structures
/// ***CRUCIAL***
/// *** WE CAN NOT USE INHERITANCE FOR ANY REQUEST AND RESPONSE MESSAGES IN OUR SPECIFIC MESSAGE CLASSES
/// *** AS THIS WILL GENERATE NULL REFERENCE EXCEPTIONS WHEN RUNNING ON IOS DEVICES!
/// ***CRUCIAL***
///  MessageBus:    NO
///  Tool-Calling:  NO
/// </summary>
 
namespace imessages
{
    //We first define interfaces for our message classes so we can later use simplified methods without need to overload for all Request/Response classes
    public interface IMessageBase
    {
        MessageCommands Command {get; set;}
        ComponentID SenderId {get; set;}
    }

    public interface IContentMessage: IMessageBase
    {
        string Content {get; set;}
    }

    public interface IUIMessage: IMessageBase
    {
        bool isOn {get; set;}
    }

    public interface IResponseMessage: IContentMessage
    {
        bool IsError {get; set;}
        ComponentID TargetId {get; set;}
    }

    public enum Role
    {
        user, assistant, system, function           //LLM roles
    }

    public class Content
    {
        public Role role { get; set; }              //LLM role for the content
        public string text { get; set; }            //Actual content itself
        public string toolCallName { get; set; }    //What tool do we need
        public string toolResultJson { get; set; }  //Tool result
    }


    //NPC Click Handler messages
    public class NPCClickRequestMessage: IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
    }

    public class NPCClickResponseMessage: IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request
    }


    // A message representing a request for a speech-to-text transcription.
    public class STTRequestMessage: IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
    }

    // A message containing the result of a speech-to-text transcription.
    public class STTResponseMessage: IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request
    }


    //Message from the director to the LLM, can be either a system prompt or a user prompt
    // in the new architecture, the Director constructs the system prompt for the LLM!
    public class LLMRequestMessage:IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
        public string Prompt_question { get; set; }     //Question part of the prompt
        public string Prompt_context { get; set; }      //Context part of the prompt
    }

    //Response from the LLM to the Director
    //In the future, the LLM can also respond with an image, however in this case we treat this as a separate image component!
    // We could still use the same LLM component but expect it to respond to a different ImageRequestMessage for example
    // The same for any additional modalities (audio, video etc)
    public class LLMResponseMessage:IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request
    }


    //Message from the Director to the TTS component
    public class TTSRequestMessage:IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
    }

    //Message from the TTS component to the LLM
    public class TTSResponseMessage:IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request
        public MessageCommands originatingMessageCommand { get; set; }              //to detect which command the response belongs to (ie. to detect whether we're not speaking)
    }


    //Language Manager component Request
    public class LangRequestMessage:IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
    }

    //Language Manager component Response
    public class LangResponseMessage:IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request        public string key;                                        //key we asked for
        public string key {get; set;}
    }

    //RAG Google WebSearch
    public class RAGWSRequestMessage:IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
        public int topn { get; set; }
    }

    //RAG WS response is in the Content field
    public class RAGWSResponseMessage:IResponseMessage
    {
        public MessageCommands Command { get; set; }                //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }                   //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }                   //The Response is sent to its requestor so we need the id of the message that sent the request
        public MessageCommands originatingMessageCommand { get; set; }           //as we have multiple command options this allows us to find out what called this response
    }


    //GPS Location Services
    public class GPSRequestMessage:IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
    }

    public class GPSResponseMessage:IResponseMessage
    {
        public MessageCommands Command { get; set; }                //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }                   //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }                   //The Response is sent to its requestor so we need the id of the message that sent the request
        public float lattitude { get; set; }                                    //explicit response in this variable so we can return 1 response with all GPS information!
        public float longitude { get; set; }                                     //explicit response in this variable so we can return 1 response with all GPS information!
        public string location { get; set; }                                     //explicit response in this variable so we can return 1 response with all GPS information!
        public MessageCommands originatingMessageCommand { get; set; }
    }


    //Preference Services, for simplicity we use the Push model vs synchronous request/response where possible
    public class PreferenceRequestMessage: IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
        public string character {get; set;}                     
        public string skinName {get; set;}                  
        public string eyesName {get; set;}
        public string debugOnOff {get; set;}                      
        public string userName {get; set;}  
        public string selectedLanguage {get; set;}              
        public string selectedVoice {get; set;}                 
        public string selectedStage {get; set;}                 

    }

    public class PreferenceResponseMessage: IResponseMessage                     //We have a MessageCommand for each stored preference
    {
        public MessageCommands Command { get; set; }            //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }               //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }               //The Response is sent to its requestor so we need the id of the message that sent the request
        public string character {get; set;}                     //Sends new character to NPC_Looks
        public string skinName {get; set;}                  
        public string eyesName {get; set;}
        public string debugOnOff {get; set;}                    //Sends GPS config to active LLM component
        public string userName {get; set;}  
        public string selectedLanguage {get; set;}              //Sends language to STT, LLM, TTS and LanguageManager
        public string selectedVoice {get; set;}                 //Sends voice to TTS
        public string selectedStage {get; set;}                 //Sends stage to LaunchUI
        public MessageCommands originatingMessageCommand { get; set; }       //as we have multiple request options
    }


    //UI
    public class UIRequestMessage: IUIMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
        public bool isOn {get; set;}
    } 

    public class UIResponseMessage: IResponseMessage 
    {
        public MessageCommands Command { get; set; }            //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }               //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }               //The Response is sent to its requestor so we need the id of the message that sent the request
    }
    

    //NPC_Looks
    public class NPCLooksRequestMessage: IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
    }

    public class NPCLooksResponseMessage: IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request
    }
    

    //Manage the NPC Expressions/Emotions
    public class NPCExpressionRequestMessage: IContentMessage
    {
        public MessageCommands Command { get; set; }
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }
        public string expressionName { get; set; }
    }
    
    public class NPCExpressionResponseMessage: IResponseMessage
    {
        public MessageCommands Command { get; set; }        //Command to the NPC Click Handler component, see AI_Interfaces.cs for details
        public string Content { get; set; }
        public ComponentID SenderId { get; set; }           //Who sent this
        public bool IsError { get; set; }
        public ComponentID TargetId { get; set; }           //The Response is sent to its requestor so we need the id of the message that sent the request
    }


    //Simple logging class to keep the code cleaner & message logging consistent
    public class iMessage
    {
        public void Log(string prefix, string sender, string command, string content)
        {
            Debug.Log(DateTime.Now.ToString("HH:mm:ss.fff") + ": [ " + sender + " => " + prefix + ": {" + command.ToString() + "}"
            + (content != null ? " '" + content + "'" : " <no content>") + " ]");
        }
        
        public void Log(string prefix, string sender, string command, string content, ComponentID targetId)
        {
            Debug.Log(DateTime.Now.ToString("HH:mm:ss.fff") + ": [ " + sender + " => " + prefix + ", TargetId=" + targetId + ", {" + command.ToString()
                + "} Content="+(content!=null ? " '" + content + "'" : " <no content>") + " ]");
        }
    }
}
