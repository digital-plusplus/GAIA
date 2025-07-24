using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using imessages;
using UnityEngine;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Defines the NON-BLOCKING message bus. The IMessageBus interface defines the core publish/subscribe functionality.
///  MessageBus:    YES
///  Tool-Calling:  N/A
/// </summary>


public class MessageBus : MonoBehaviour
{
    // A dictionary to store handlers for different message types.
    // The key is the message type, and the value is a list of handlers.
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new ConcurrentDictionary<Type, List<object>>();
    private readonly ConcurrentQueue<object> _messageQueue = new ConcurrentQueue<object>();
    private bool _isProcessing = false;

    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "MESSAGEBUS: ";


    private void Start()
    {
        _isProcessing = true;
        ProcessMessagesAsync();      //runs in the background
    }


    private void OnDestroy()
    {
        _isProcessing = false;
    }


    // Publishes a message to all subscribed handlers.
    public Task Publish<T>(T message)
    {
        _messageQueue.Enqueue(message);
        return Task.CompletedTask;
    }


    // Subscribes a handler to a specific message type.
    public Task Subscribe<T>(Func<T, Task> handler)
    {
        var messageType = typeof(T);
        var handlers = _handlers.GetOrAdd(messageType, new List<object>());

        // Add the handler if it's not already in the list
        if (!handlers.Contains(handler))
        {
            handlers.Add(handler);
        }
        return Task.CompletedTask;
    }



    // Unsubscribes a handler from a message type.
    public Task Unsubscribe<T>(Func<T, Task> handler)
    {
        var messageType = typeof(T);
        if (_handlers.TryGetValue(messageType, out var handlers))
        {
            handlers.Remove(handler);
        }
        return Task.CompletedTask;
    }
      

    private async void ProcessMessagesAsync()
    {
        while (_isProcessing)
        {
            // Get the messages from the queue
            if (_messageQueue.TryDequeue(out var message))
            {
                ComponentID targetComponentId = ComponentID.Broadcast;
                if (message is IResponseMessage responseMessage)                                //Now uses INTERFACES instead of ResponseMessage class!
                    targetComponentId = responseMessage.TargetId;

                Type messageType = message.GetType();

                if (_handlers.TryGetValue(messageType, out var handlers))
                {
                    foreach (var handler in handlers.ToArray())
                    {
                        if (handler is Delegate typedDelegate)
                        {
                            bool shouldProcess = true;

                            // Filtering logic, filter only when a specific target was sent
                            if (targetComponentId != ComponentID.Broadcast)
                            {    
                                if (typedDelegate.Target is IAIComponent component)
                                    if (component.ComponentId != targetComponentId)
                                        shouldProcess = false;                                  // Filter out the message
                            }

                            if (shouldProcess)
                            {
                                if (typedDelegate.Method.ReturnType == typeof(Task))
                                {
                                    try
                                    {
                                        //await (Task)typedDelegate.DynamicInvoke(message);     //blocking messagebus
                                        _ = (Task)typedDelegate.DynamicInvoke(message);         //non-blocking messagebus!
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogError(DEBUG_PREFIX + $"Error processing async handler: {ex}");
                                    }
                                }
                            }
                        }
                    }
                }
        }
            
        // Wacht op de volgende verwerkingscyclus
        await Task.Yield(); 
        }
    }

}
