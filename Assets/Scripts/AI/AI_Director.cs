using UnityEngine;
using System.Threading.Tasks;
using imessages;


/// <summary>
/// Central AI Director Component => Currently its role is marginal & replaced by the LLM!
/// </summary>
public class AI_Director : MonoBehaviour
{
    //MessageBus    
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }

    //Context Storage for most recent RAG/GPS data
    [HideInInspector] public float lat = -1, lon = -1;
    [HideInInspector] public string gpsLocation = null;
    [HideInInspector] public string ragWebContext = "";
    [HideInInspector] public string ragWeatherContext = "";

    //LLM prompt settings
    public string whoAmI = "nobody";               //Define the name of the avatar
    public string context;                         //Any information to append to the prompt (weather, RAG, internet content etc)
    public int maxNumberOfWords = 75;              //Avoid verbose responses & running out of credits


    //Debug 
    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "AI DIRECTOR: ";
    iMessage iM = new iMessage();

    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================

    //Called from the AI Registrar
    public async Task Init(MessageBus messageBus)
    {
        Debug.Log(DEBUG_PREFIX + "DIRECTOR INITIALIZING");

        //First we define the MessageBus
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.AI_Director;                      //Make yourself known

        await Task.Delay(0);

        Debug.Log(DEBUG_PREFIX + ComponentId + " started and is ready to orchestrate.");
    }


    public async Task Stop()
    {
        await Task.Delay(0);
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }

}
