
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using imessages;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// The new AI Orchestrator based on the MessageBus Architecture
/// This AIR component registers and initializes all active AI components, it also creates the central MessageBus
/// Uses ASYNC and EVENTS vs COROUTINES
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>

public class AI_Registrar : MonoBehaviour
{
    //Here we define all the generalized types of AI services so the AIR can auto-detect which one is used in the Inspector
    private ISttService activeSttService;
    private ILlmService activeLlmService;
    private ILlmVisionService activeLlmVisionService;
    private ITtsService activeTtsService;
    private IRagWebService activeRagWebService;
    private ILocationService activeGPSLocationService;
    
    //Central MessageBus 
    private MessageBus _messageBus;                                     //HERE is where we define the active messagebus and transfer it to all components via Init(messagebus)!
    public ComponentID ComponentId { get; set; }

    //public static event Action OnAllAIComponentsInitialized;            //Event to signal other components that all AI components are initialized   
    [SerializeField] private bool startWithGreeting=false;               //Setting to let the NPC immediately respond when you open the app

    //Debug related properties
    [SerializeField] private bool debug;
    private string DEBUG_PREFIX = "AIR: ";
 

    //=====================================================================
    // Starting point of the Project!
    //=====================================================================
    async void Start()
    {
        if (debug) Debug.Log(DEBUG_PREFIX + "Starting...");

        //Initialize the MessageBus
        _messageBus = gameObject.AddComponent<MessageBus>();
        ComponentId = ComponentID.AI_Registrar;
        if (debug)
            if (_messageBus)
                Debug.Log(DEBUG_PREFIX + "MessageBus instance created.");
            else
                Debug.LogError(DEBUG_PREFIX + "Cannot create an AI message bus!");

        //!!!!!!!!!!!!!!!!!!!!!!
        //Startup Sequence
        //!!!!!!!!!!!!!!!!!!!!!!
        await GameObject.Find("LaunchUI").GetComponent<PreferencesManager>().Init(_messageBus);     //Step 1: We load all preferences 

        await GetComponent<API_Keys>().Init();                                                      //Step 2: We load the API keys
        await GetComponent<AI_Director>().Init(_messageBus);                                        //Step 3: We initialize the AI Director
        await GetComponent<NPCClickHandler>().Init(_messageBus);
        await RegisterAndFindAllServices(_messageBus);                                              //Step 5: we AUTO-register all services 

        await GetComponent<NPC_Looks>().Init(_messageBus);                                          //Step 5a: Initialize the NPC_Looks component (non-AI)
        await GetComponent<SyncAllBlendShapes>().Init(_messageBus);                                 //Step 5b: Initialize the NPC SyncAllBlendShapes components (expressions manager)

        await GameObject.Find("LaunchUI").GetComponent<LaunchUI>().Init(_messageBus);               //Step 6: First of all we initiate the UI

        await SendMessage<PreferenceRequestMessage>(MessageCommands.isInitPreferences,"");          //Step 7: We push the preferences to all components

        await Task.Delay(1000);                                                                     //Wait a little so that all prefs are loaded (ASYNC bus so no ready event!)                                                                                                        
        if (debug) 
            Debug.Log(DEBUG_PREFIX + "ALL COMPONENTS INITIALIZED AND READY");

        await SendMessage<UIRequestMessage>(MessageCommands.isUINPCOnOff, true);                    //Turn ON the NPC

        if (startWithGreeting)
            await SendMessage<LLMRequestMessage>(MessageCommands.isLLMTextToLLM,"Hello!");
            
        await SendMessage<NPCClickRequestMessage>(MessageCommands.isNPCClickEnable, "");            //Step 8: Enable the PTT button
    }


    //=====================================================================
    // Registration code for AI Components - Interfaces based 
    //=====================================================================

    private async Task RegisterAndFindAllServices(MessageBus messageBus)
    {
        // Collecteer alle initiatie-taken
        var initTasks = new List<Task>();
        
        // Roep de generieke methode aan om de taken te verzamelen
        initTasks.Add(RegisterAndFindServices<ILangService>(messageBus));
        initTasks.Add(RegisterAndFindServices<ISttService>(messageBus));
        initTasks.Add(RegisterAndFindServices<ILlmService>(messageBus));
        initTasks.Add(RegisterAndFindServices<ILlmVisionService>(messageBus));
        initTasks.Add(RegisterAndFindServices<ITtsService>(messageBus));
        initTasks.Add(RegisterAndFindServices<IRagWebService>(messageBus));
        initTasks.Add(RegisterAndFindServices<ILocationService>(messageBus));
        
        // Await until all done
        await Task<MessageBus>.WhenAll(initTasks);
    }


    //Generic method to find and initialize all components that implement a specific interface
    // - this way we can easily add new AI components that implement the same interface without having  to change this script
    private async Task RegisterAndFindServices<T>(MessageBus messageBus) where T : class, IAiService
    {
        T[] services = GetComponents<T>();
        if (services.Length == 0) return;

        // Make a list of Init tasks
        var serviceInitTasks = services.Select(service => service.Init(messageBus)).ToList();

        // Wait until they are all done
        await Task.WhenAll(serviceInitTasks);

        // Provide some logging
        foreach (var service in services)
        {
            if (debug) Debug.Log(DEBUG_PREFIX + "Initialized " + service.GetType().Name);
        }
    }

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

    public async Task SendMessage<T>(MessageCommands command, bool isOn) where T : class, IUIMessage, new()
    {
        // Creëer een nieuw object van het opgegeven type
        var message = new T
        {
            Command = command,
            isOn = isOn,
            SenderId = ComponentId
        };

        if (message is IContentMessage contentMsg)
        {
            contentMsg.Content = "";
        }

        // Publiseer het generieke bericht
        await _messageBus.Publish<T>(message);
    }
}
