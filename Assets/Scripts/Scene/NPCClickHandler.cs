using UnityEngine;
using imessages;
using System.Threading.Tasks;
using UnityEngine.UI;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Manages the Press To Talk button
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>

public class NPCClickHandler : MonoBehaviour, IAIComponent
{
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }
    [SerializeField] private Button pttButton;          //Reference to the Launch UI so we can control the button
    [HideInInspector] public bool isRecording=false;
    private bool isReady = false;                       //We are only ready after the Director has sent us an enable message
    [SerializeField] private bool debug;
    const string DEBUG_PREFIX = "NPCClickHandler: ";
    iMessage iM = new iMessage();


    //=====================================================================================
    // UI EVENT HANDLERS
    //=====================================================================================

    //User presses the button, we start recording if the AI Director has enabled us
    public async void OnMouseDown()
    {
        if (!isReady) return;                                                                   // ignore
        if (debug)
            Debug.Log(DEBUG_PREFIX + "<<Click>>");
        isRecording = true;
        isReady = false;                                                                        //We are no longer ready until the Director re-enables us
        await RespondMessage(MessageCommands.isNPCClick, "", ComponentID.Broadcast, false);     //Tell EVERYONE that the user has clicked
    }


    //User releases the button, we stop recording
    public async void OnMouseUp()
    {
        if (pttButton.interactable == false) return;                                            //Avoid we can release multiple times whilst the Director is processing
        if (debug)
            Debug.Log(DEBUG_PREFIX + "<<Release>>");
        isRecording = false;
        pttButton.interactable = false;                                                         //Disable the button until we are re-enabled by the Director
        await RespondMessage(MessageCommands.isNPCRelease, "", ComponentID.Broadcast, false);   //Tell EVERYONE that the user has released
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;
        ComponentId = ComponentID.NPC_Click_Handler;

        if (debug)
            Debug.Log(DEBUG_PREFIX + "INITIALIZING");

        if (pttButton == null)
        {
            Debug.LogError(DEBUG_PREFIX + "PTT Button reference not set in the Inspector!");
            return;
        }
        else pttButton.interactable = false;

        await _messageBus.Subscribe<NPCClickRequestMessage>(HandleNPCClickRequestAsync);        //Here we register the message bus handler for incoming messages

        if (debug)
            Debug.Log(DEBUG_PREFIX + ComponentId + " INITIALIZED and subscribed to NPCClickRequestMessage.");
    }


    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<NPCClickRequestMessage>(HandleNPCClickRequestAsync);
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    //Posts a response message back on the message bus
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        NPCClickResponseMessage message = new NPCClickResponseMessage()
        {
            Command = MessageCommands.isNPCResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = isError
        };
        await _messageBus.Publish<NPCClickResponseMessage>(message);
    }

    //Explicit Response MessageCommand overload
    public async Task RespondMessage(MessageCommands command, string content, ComponentID requestorId, bool isError)
    {
        NPCClickResponseMessage message = new NPCClickResponseMessage()
        {
            Command = command,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = isError
        };
        await _messageBus.Publish<NPCClickResponseMessage>(message);
    }


    //=====================================================================================
    // MESSAGE BUS HANDLERS
    //=====================================================================================

    // Awaits and handles incoming messages from the Director to the NPC Click Handler
    private async Task HandleNPCClickRequestAsync(NPCClickRequestMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        switch (message.Command)
        {
            case MessageCommands.isNPCClickEnable:
                isReady = true;
                isRecording = false;
                pttButton.interactable = true;                                                   //Enable the button
               //await RespondMessage("ok", message.SenderId, false);           //confirm the AI Director that we're ready for user input
                break;

            default:
                Debug.LogError(DEBUG_PREFIX + "Unknown command received: " + message.Command.ToString());
                await RespondMessage("", message.SenderId, true);             //Tell the AI Director we have an error
                break;
        }
    }
}