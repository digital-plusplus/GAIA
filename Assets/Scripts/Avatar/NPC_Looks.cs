using System;
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
/// Component that manages the way the NPC looks - Blendshapes, hairtype, skintype etc 
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>


public class NPC_Looks : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    // provides public methods which are called by the UI and/or speech commands 

    [Header("Connectors to GameObjects")]
    [SerializeField] GameObject gen9Shape;
    [SerializeField] GameObject gen9Eyes;
    [SerializeField] LaunchUI launchUI;


    //here we set the BlendShape names as per the main shape
    [SerializeField] private List<CharacterItemData> characterData = new List<CharacterItemData>();
    private Dictionary<string, CharacterItemData> _itemLookup = new Dictionary<string, CharacterItemData>();

    //here we set the Skin material (not texture!)
    [SerializeField] private List<SkinItemData> skinData = new List<SkinItemData>();
    private Dictionary<string, SkinItemData> _skinLookup = new Dictionary<string, SkinItemData>();

    //here we set the Eye Texture (not material!) - only provide the texture name as a string!
    [SerializeField] private List<EyeItemData> eyeData = new List<EyeItemData>();
    private Dictionary<string, EyeItemData> _eyeLookup = new Dictionary<string, EyeItemData>();

    Mesh gen9Shape_Mesh;
    SkinnedMeshRenderer gen9Shape_SMR;

    [SerializeField] bool debug;
    string DEBUG_PREFIX = "NPC_LOOKS:";
    iMessage iM = new iMessage();

    private int currentSelection;           //Which character is currently selected - use an integer to calculate whether we roll over to the start
    private int currentSkinSelection;       //Which Skin is currently selected
    private int currentEyeSelection;        //Which EYes have been selected
    
    //MessageBus
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }
    private PreferenceResponseMessage pRM = null;


    //Can be called by LaunchUI to say the selected character name
    public string GetCurrentCharacterName()
    {
        return GetCharacterItemInt(currentSelection).Key;
    }


    private CharacterItemData GetCharacterItemInt(int id)
    {
        int cnt = 0;
        foreach (CharacterItemData item in characterData)
        {
            if (cnt == id)
            {
                return item;
            }
            cnt++;
        }
        Debug.LogWarning(DEBUG_PREFIX + $"Item with ID '{id}' not found in database.");
        return default(CharacterItemData); // Return default struct if not found
    }


    private SkinItemData GetSkinItemInt(int id)
    {
        int cnt = 0;
        foreach (SkinItemData item in skinData)
        {
            if (cnt == id)
            {
                return item;
            }
            cnt++;
        }
        Debug.LogWarning(DEBUG_PREFIX + $"Item with ID '{id}' not found in Skin database.");
        return default(SkinItemData);
    }


    private EyeItemData GetEyeItemInt(int id)
    {
        int cnt = 0;
        foreach (EyeItemData item in eyeData)
        {
            if (cnt == id)
            {
                return item;
            }
            cnt++;
        }
        Debug.LogWarning(DEBUG_PREFIX + $"Item with ID '{id}' not found in Eye database.");
        return default(EyeItemData);
    }


    //When called just picks the next available skin, rotates with #available skins
    // returns the name of the active skin
    public string NextSkin()
    {
        string skinName;

        currentSkinSelection = (currentSkinSelection + 1) % _skinLookup.Count;
        skinName = GetSkinItemInt(currentSkinSelection).Key;

        if (debug)
            Debug.Log(DEBUG_PREFIX + "current Skin=" + currentSkinSelection + " " + skinName);

        //Call to Skin changer
        SetNPCSkinPreset(skinName);
        return skinName;
    }


    public string NextEyes()
    {
        string eyesName;

        currentEyeSelection = (currentEyeSelection + 1) % _eyeLookup.Count;
        eyesName = GetEyeItemInt(currentEyeSelection).Key;

        if (debug)
            Debug.Log(DEBUG_PREFIX + "current Eyes=" + currentEyeSelection + " " + eyesName);

        //Call to Eyes changer
        SetNPCEyesPreset(eyesName);

        return eyesName;
    }


    //Sets the NPCs face blendshape acc to the input string (ie. name)
    public void SetNPCBlendShapePreset(string preset)
    {
        int cnt = 0;

        foreach (KeyValuePair<string, CharacterItemData> entry in _itemLookup)
        {
            if (entry.Key.ToString() == preset)
            {
                currentSelection = cnt;
                if (debug)
                    Debug.Log(DEBUG_PREFIX + preset + " has blendshape " + entry.Value.BlendShapeName + " index=" + gen9Shape_Mesh.GetBlendShapeIndex(entry.Value.BlendShapeName));
            }
            //Set for all blendhapes 0 except for the one specified in preset
            BlendFace(GetBlendShapeIndex(entry.Value.BlendShapeName), (entry.Key == preset ? entry.Value.Percent : 0));
            cnt++;
        }
    }


    //Sets the NPCs skin acc to the input string (ie. name)
    public void SetNPCSkinPreset(string preset)
    {
        int cnt = 0;

        Material[] mats = gen9Shape_SMR.materials;

        foreach (KeyValuePair<string, SkinItemData> entry in _skinLookup)
        {
            if (entry.Key.ToString() == preset)
            {
                currentSkinSelection = cnt;
                mats[0] = entry.Value.FingerNails;
                mats[1] = entry.Value.ToeNails;
                mats[2] = entry.Value.Legs;
                //gen9Shape_SMR.materials[3] = entry.Value.MouthCavity; //same for all 
                mats[4] = entry.Value.Arms;
                mats[5] = entry.Value.Head;
                mats[6] = entry.Value.Body;

                gen9Shape_SMR.materials = mats; // <-- THIS is crucial! You can't directly assign to materials[]
            }
            cnt++;
        }
    }


    public void SetNPCEyesPreset(string preset)
    {
        int cnt = 0;

        var smr = gen9Eyes.GetComponent<SkinnedMeshRenderer>();
        Material[] currentMaterials = smr.materials;

        foreach (KeyValuePair<string, EyeItemData> entry in _eyeLookup)
        {
            if (entry.Key.ToString() == preset)
            {
                currentEyeSelection = cnt;
                if (debug)
                    Debug.Log(DEBUG_PREFIX + preset + " has eye Texture " + entry.Value.texture.name);
                currentMaterials[2].SetTexture("_BaseMap", entry.Value.texture);
                currentMaterials[3].SetTexture("_BaseMap", entry.Value.texture);
                smr.materials = currentMaterials;

                break;
            }
            cnt++;
        }
    }

    //Gets the integer that represents the actual blendshape
    private int GetBlendShapeIndex(string aBS)
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + aBS);
        int rtnValue = gen9Shape_Mesh.GetBlendShapeIndex(aBS);
        return rtnValue;
    }


    //Sets the blendshape identified by integer what to value value
    private void BlendFace(int what, int value)
    {
        if (what == -1)
        {
            Debug.LogError(DEBUG_PREFIX + " Illegal BlendShape index (-1), check whether the Blendshapes are configured in Inspector!");
            return;
        }
        gen9Shape_SMR.SetBlendShapeWeight(what, value);
    }


    //================================
    //JSON Class representation
    //================================
    [System.Serializable]
    public struct CharacterItemData
    {
        public string Key;                  //Name of the Character
        public string BlendShapeName;       //Which Blendshape corresponds to the character (Genesis9->SMR->Blendshapes)
        public int Percent;                 //how much should the BlendShape be enforced
    }

    [System.Serializable]
    public struct SkinItemData
    {
        public string Key;                  //Name of the Skin
        public Material Head, Arms, Body, Legs, FingerNails, ToeNails;             //Which Blendshape corresponds to the character (Genesis9->SMR->Blendshapes)
    }


    [System.Serializable]
    public struct EyeItemData
    {
        public string Key;                  //Name of the eye color
        public Texture2D texture;           //Actual texture that corresponds to this name
    }


    [System.Serializable]
    public struct HairStyleData
    {
        public string Key;                  //Name of the hairstyle
        public GameObject hairStyle;        //GameObject to activate (others will be deactivated)
    }


    [System.Serializable]
    public struct ClothesItemData
    {
        public string Key;                  //Name of the Clothes
        public GameObject Top, Bottom;      //Which GameObjects to use fpr top and bottom
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.NPC_Looks_Handler;                //Make yourself known

        Debug.Log(DEBUG_PREFIX+"Initializing");
        await _messageBus.Subscribe<NPCLooksRequestMessage>(HandleNPCLooksRequestAsync);
        await _messageBus.Subscribe<PreferenceResponseMessage>(HandlePrefResponseAsync);                //To activateincoming preference changes!
        Debug.Log(DEBUG_PREFIX + " started and subscribed to NPCLooksRequestMessage");

        //Get NPC blendshapes via the Skinned Mesh Renderer
        gen9Shape_SMR = gen9Shape.GetComponent<SkinnedMeshRenderer>();
        gen9Shape_Mesh = gen9Shape_SMR.sharedMesh;

        // Clear any existing data if Awake runs multiple times (e.g., domain reload)
        _itemLookup.Clear();
        _skinLookup.Clear();
        _eyeLookup.Clear();

        //Populate characters in Inspector
        foreach (CharacterItemData item in characterData)
        {
            if (!_itemLookup.ContainsKey(item.Key)) // Ensure ID is unique
                _itemLookup.Add(item.Key, item);
            else
                Debug.LogWarning(DEBUG_PREFIX + $"Duplicate Item ID '{item.Key}' found in initial items list. Only the first will be used.");
        }

        //Populate skins
        foreach (SkinItemData skItem in skinData)
        {
            if (!_skinLookup.ContainsKey(skItem.Key))
                _skinLookup.Add(skItem.Key, skItem);
            else
                Debug.LogWarning(DEBUG_PREFIX + $"Duplicate Item ID '{skItem.Key}' found in initial items list. Only the first will be used.");
        }

        //Populate eyes
        foreach (EyeItemData eyeItem in eyeData)
        {
            if (!_eyeLookup.ContainsKey(eyeItem.Key))
                _eyeLookup.Add(eyeItem.Key, eyeItem);
            else
                Debug.LogWarning(DEBUG_PREFIX + $"Duplicate Item ID '{eyeItem.Key}' found in initial items list. Only the first will be used.");
        }
    }
    

    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<NPCLooksRequestMessage>(HandleNPCLooksRequestAsync);
        await _messageBus.Unsubscribe<PreferenceResponseMessage>(HandlePrefResponseAsync);
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
        NPCLooksResponseMessage message = new NPCLooksResponseMessage()
        {
            Command = MessageCommands.isSTTResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<NPCLooksResponseMessage>(message);
    }


    //Helper to send a Preference request
    private async Task UpdatePreference(Func<string> lookUpdateAction, Action<PreferenceRequestMessage, string> setProperty)
    {
        string newValue = lookUpdateAction();
        PreferenceRequestMessage prefMessage = new PreferenceRequestMessage()
        {
            Command = MessageCommands.isPreferencesUpdate,
            SenderId = ComponentId
        };
        setProperty(prefMessage, newValue);

        await _messageBus.Publish<PreferenceRequestMessage>(prefMessage);
    }


    //Incoming messages
    //When we receive a request we invoke the corresponding method using the globally set Preferences variable pRM!!
    // this pRM is set by the reception of a PreferencesRequestMessage! == PUSH mechanism
    private async Task HandleNPCLooksRequestAsync(NPCLooksRequestMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isNPCCharacterName:        
                string response = GetCurrentCharacterName();                    //get the name of the current character
                await RespondMessage(response, ComponentId, false);             //Send it to the bus, requires sender to implement SYNCHRONOUS mechanism!
                break;

            case MessageCommands.isNPCNextEyes:
                await UpdatePreference( () => NextEyes(), (msg, val) => msg.eyesName=val);
                break;

            case MessageCommands.isNPCNextSkin:
                await UpdatePreference( () => NextSkin(), (msg, val) => msg.skinName=val);
                break;

            default:
                Debug.LogError(DEBUG_PREFIX+"Error, unknown MessageCommand received: " + message.Command.ToString());
                break;
        }
    }

    //Incoming preference changes pushed by Preference Manager
    private Task HandlePrefResponseAsync(PreferenceResponseMessage message)
    {
        iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString(), message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isPreferencesResponse:
                pRM = message;                              //Store the whole lot in a single object, easier to handle changes later!
                
                //Eyes
                if (!string.IsNullOrEmpty(pRM.eyesName)) 
                    SetNPCEyesPreset(pRM.eyesName);
                if (debug) 
                    Debug.Log(DEBUG_PREFIX + "Set eyes to " + pRM.eyesName);
                
                //Skin
                if (!string.IsNullOrEmpty(pRM.skinName)) 
                    SetNPCSkinPreset(pRM.skinName);
                if (debug) 
                    Debug.Log(DEBUG_PREFIX + "Set skin to " + pRM.skinName);
                
                break;
            
            default:
                Debug.LogError(DEBUG_PREFIX + "Error, unknown MessageCommand received!");
                break;
        }
        return Task.CompletedTask;
    }
}
