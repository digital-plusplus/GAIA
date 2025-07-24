using UnityEngine;
using UnityEngine.Networking;
using imessages;
using System.Threading.Tasks;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Simplified - version that statically defines the GPS coordinates and uses OpenCageData service to retrieve location name information
/// The non-GPL pro version accesses the local GPS device on iOS and Android devices to provide realtime location information
///  MessageBus:    YES
///  Tool-Calling:  NO (can be done but high impact on your rate limits!)
/// </summary>

public class GPS_LocationManager : MonoBehaviour, ILocationService, IAIComponent
{
    [SerializeField] public float locationUpdateInterval = 60.0f;   // Standard interval for location updates
    [SerializeField] private float _long = 51.35f;                  
    [SerializeField] private float _lat = 3.12f;                       
    private bool overrideGPS=false;                                 //This will be enforced in Unity Editor mode          

    private float currentLat, currentLon, currentAlt;               //internal variables, one async loop updates them, other methods can pull the location from these variables
    private string currentLocationCity, currentLocationCountry;
    private bool isErrorState = false;
    private bool firstRun = true;
    [SerializeField] private bool broadcastGPS=true;                //Automatically send GPS updates onto the MessageBus vs awaiting Requests

    const string DEBUG_PREFIX = "GPS_LOCATIONMANAGER ";
    [SerializeField] private bool debug;
    iMessage iM = new iMessage();

    [HideInInspector] public bool isGPSRunning = false;

    private string openCageAPIKey;
    private const string openCageApiBaseUrl = "https://api.opencagedata.com/geocode/v1/json";

    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }
    

    //This initializes the GPS and if successful it sets the isGPSRunning global bool variable
    private async Task InitializeGPS(float maxWaitTime)
    {
        //We first check whether we're running on Unity Editor as we don't have a GPS there => we enforce a bypass
        #if UNITY_EDITOR
        overrideGPS = true;
        Debug.Log(DEBUG_PREFIX + "UNITY EDITOR DETECTED, OVERRIDING GPS");
        #endif

        await Task.Delay(0);        //Keep the compiler happy

        //First check whether we're in override mode
        if (overrideGPS)
        {
            Debug.Log(DEBUG_PREFIX + "GPS OVERRIDE IS ACTIVE. Skipping location service initialization.");
            isGPSRunning = true; 
            return; 
        }
    }


    //Constantly runs in the background and updates the location name with a fixed locationUpdateInterval
    // this is to avoid we constantly call the API and exceed the account limits!
    private async Task PeriodicallyUpdateLocation()
    {
        float lastLocationUpdateTime = 0f;

        while (isGPSRunning) // loop whilst GPS
        {
            float timeSinceLastUpdate = Time.time - lastLocationUpdateTime;
            float delayNeeded = locationUpdateInterval - timeSinceLastUpdate;

            if (!firstRun)                  //Skip for the first cycle - we immediately want to know where we are 
            {
                if (delayNeeded > 0)        //enforce getting the location name immediately after we started
                {
                    int delayMs = Mathf.RoundToInt(delayNeeded * 1000f);
                    firstRun = false;                   //Only once
                    await Task.Delay(delayMs).ConfigureAwait(true);
                }
            }
            else firstRun = false;          //Only once

            lastLocationUpdateTime = Time.time;
            LocationServiceStatus status = Input.location.status;

            if (overrideGPS)
            {
                // Stel de interne variabelen in
                currentLat = _lat;
                currentLon = _long;
                isErrorState = false;
                if (debug)
                    Debug.Log(DEBUG_PREFIX + "OVERRIDE Location Updated: " + currentLat + "/" + currentLon);
            }

            //Get the location name from the API
            string locationName = await GetLocationNameFromOpenCageAsync();
            currentLocationCity = locationName;
            if (debug) Debug.Log(DEBUG_PREFIX + locationName);

            //Now we BROADCAST the GPS information to anyone interested
            if (broadcastGPS) 
            {
                GPSResponseMessage rM = new GPSResponseMessage();
                rM.Command = MessageCommands.isGPSBroadcast;
                rM.lattitude = currentLat;
                rM.longitude = currentLon;
                rM.location = GetLocationName();
                rM.IsError = isErrorState;
                rM.SenderId = ComponentId;
                rM.Content = rM.location;               //just for debugging purposes
                rM.originatingMessageCommand = MessageCommands.isGPSBroadcast;

                if (debug) Debug.Log(DEBUG_PREFIX + "Broadcasting GPS data: " + rM.location + " " + currentLat+"/"+currentLon);
                await _messageBus.Publish(rM);
            }
        }
    }


    //Connects to the OpenCage REST API
    private async Task<string> GetLocationNameFromOpenCageAsync()
    {
        if (string.IsNullOrEmpty(openCageAPIKey))
        {
            Debug.LogError(DEBUG_PREFIX + "OpenCage API Key is not set/valid");
            isErrorState = true;
            currentLocationCity = "Error";
            currentLocationCountry = "Error";
            return "Error";
        }

        string requestUrl = $"{openCageApiBaseUrl}?q={currentLat}+{currentLon}&key={openCageAPIKey}"; // language=nl voor Nederlandse resultaten

        using (UnityWebRequest webRequest = UnityWebRequest.Get(requestUrl))
        {
            await webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError(DEBUG_PREFIX + "OpenCage API Error: " + webRequest.error);
                isErrorState = true;
                currentLocationCity = "Error";
                currentLocationCountry = "Error";
                return "Error";
            }
            else
            {
                string jsonResponse = webRequest.downloadHandler.text;
                isErrorState = false;
                return ParseOpenCageResponse(jsonResponse);
            }
        }
    }


    //Pulls the location string from the json, here we just pull the combined city, street etc, you can include more variables if you like!
    private string ParseOpenCageResponse(string jsonString)
    {
        try
        {
            OpenCageResponse response = JsonUtility.FromJson<OpenCageResponse>(jsonString);

            if (response.status.code == 200 && response.results != null && response.results.Length > 0)
            {
                currentLocationCity = response.results[0].formatted;
                currentLocationCountry = response.results[0].components.country;

                if (debug)
                    Debug.Log(DEBUG_PREFIX + "Parsed OpenCage Location: " + currentLocationCountry + " " + currentLocationCity);
                isErrorState = false;
                return currentLocationCity;
            }
            else
            {
                isErrorState = true;
                currentLocationCity = "Error";
                currentLocationCountry = "Error";
                Debug.LogWarning(DEBUG_PREFIX + "OpenCage: No results found " + response.status.code + " - " + response.status.message);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError(DEBUG_PREFIX + "Error parsing OpenCage JSON: " + e.Message + "\nJSON: " + jsonString);
            isErrorState = true;
            currentLocationCity = "Error";
            currentLocationCountry = "Error";
        }
        return "Error";
    }


    //Main method that retrieves the info, called by RequestHandler method
    public string GetLocationName()
    {
        return currentLocationCity;
    }


    public string GetCountry()
    {
        return currentLocationCountry;
    }


    //Cleanly stop the GPS service when the application quits
    void OnApplicationQuit()
    {
        Input.location.Stop();
    }


    /// <summary>
    /// JSON classes for OpenCage API response parsing
    /// </summary>
    [System.Serializable]
    public class OpenCageResult
    {
        public string formatted;                    
        public OpenCageComponents components;   
    }

    [System.Serializable]
    public class OpenCageComponents
    {
        //public string city;
        public string country;
        //public string road;
    }

    [System.Serializable]
    public class OpenCageResponse
    {
        public OpenCageResult[] results; // Een array van resultaten
        public OpenCageStatus status; // De status van de API call
    }

    [System.Serializable]
    public class OpenCageStatus
    {
        public int code; // HTTP status code
        public string message; // Status bericht
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                           //Ensure we link to the active bus
        ComponentId = ComponentID.GPS_Location_Service;                     //Make yourself known

        if (debug) Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<GPSRequestMessage>(HandleGPSRequestAsync);
        Debug.Log(DEBUG_PREFIX + " started and subscribed to RAGWeatherRequestMessage.");

        // We first retrieve the OpenCage API key from the API Key component
        API_Keys api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else openCageAPIKey = api_Keys.GetAPIKey("OpenCageData_API_Key");

        //We check whether GPS is enabled in the Launch UI
        await InitializeGPS(15f);                                           // Initialize GPS service, wait for max 15 seconds to avoid blocking of the main thread~!
        
        _ = PeriodicallyUpdateLocation();                                   //CRUCIAL, use _ to ensure we're not blocking the main thread!
    }


    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<GPSRequestMessage>(HandleGPSRequestAsync);
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }


    //=====================================================================================
    // MESSAGE BUS HANDLERS
    //=====================================================================================

    //Outgoing messages
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        GPSResponseMessage message = new GPSResponseMessage()
        {
            Command = MessageCommands.isGPSResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish(message);
    }

    public async Task RespondMessage(MessageCommands originatingMessageCommand, string content, ComponentID requestorId, bool isError)
    {
        GPSResponseMessage message = new GPSResponseMessage()
        {
            Command = MessageCommands.isGPSResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false,
            originatingMessageCommand = originatingMessageCommand
        };
        await _messageBus.Publish(message);
    }


    private async Task HandleGPSRequestAsync(GPSRequestMessage message)
    {
        
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        switch (message.Command)
        {
            case MessageCommands.isGPSCurrentLocationString:                                                                //just the location name, no lat/long needed
                await RespondMessage(message.Command, "Location = "+ GetLocationName(), message.SenderId, isErrorState);    //respond with whatever string we have in the global variable
                break;

            case MessageCommands.isGPSCurrentCountry:                                                                       //just the location name, no lat/long needed
                await RespondMessage(message.Command, "Country = " + GetCountry(), message.SenderId, isErrorState);         //respond with whatever string we have in the global variable
                break;


            case MessageCommands.isGPSCurrentLocationData:                                                                  //Now we're asked to provide all information incl lat/lon
                GPSResponseMessage rM = new GPSResponseMessage();
                rM.Command = MessageCommands.isGPSResponse;
                rM.lattitude = currentLat;
                rM.longitude = currentLon;
                rM.location = GetLocationName();
                rM.IsError = isErrorState;
                rM.SenderId = ComponentId;
                rM.Content = rM.location;               //just for debugging purposes
                rM.originatingMessageCommand = message.Command;

                if (debug) Debug.Log(DEBUG_PREFIX + "Responding with " + rM.location + " " + currentLat+"/"+currentLon);
                await _messageBus.Publish(rM);
                break;

            //We received nonsense.
            default:
                Debug.LogError(DEBUG_PREFIX+ "Unknown message command received!");
                break;
        }
    }
}
