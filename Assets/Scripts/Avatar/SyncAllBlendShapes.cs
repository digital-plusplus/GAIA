using System.Collections.Generic;
using UnityEngine;
using System;
using imessages;
using System.Threading.Tasks;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Synchronizes movement & expressions across all blendshapes
///  MessageBus:    YES
///  Tool-Calling:  NO
/// </summary>

public class SyncAllBlendShapes : MonoBehaviour, IAIComponent
{
    //Flexible expressions class that we can define in the Inspector and index with a single index

    [Serializable]
    public class BlendShapeLayer
    {
        public string blendShapeName;               // De exacte naam van de blendshape (bijv. "v_squint")
        [Range(0, 100)] public int limit = 100;     // De specifieke limiet voor DEZE blendshape   
        [HideInInspector] public int index = -1;    // Wordt gevuld in Start()
}

    [Serializable]
    public class ExpressionConfig
    {
        public string emotionLabel; 
        public List<BlendShapeLayer> layers = new List<BlendShapeLayer>();
    }

    public enum ExpressionId        //the order in Inspector must be the same!
    {
        Gentle, Annoyed, Thinking, All
    }


    [Header("Connect to Shapes")]
    [SerializeField] GameObject gen9Shape;
    [SerializeField] GameObject gen9Mouth;
    [SerializeField] GameObject gen9Brow;
    [SerializeField] GameObject gen9Lash;
    [SerializeField] GameObject gen9Tear;
    [SerializeField] GameObject gen9Eyes;

    //Skinned mesh renderers for the above GameObjects
    SkinnedMeshRenderer gen9Shape_SMR, gen9Mouth_SMR, gen9Brow_SMR, gen9Lash_SMR, gen9Tear_SMR, gen9Eyes_SMR;
    Mesh gen9Shape_Mesh;
    
    [SerializeField]
    GameObject npcLEye, npcREye;
    Animator avtAnimator;

    [Header("Eye Controls")]

    [SerializeField] float blinkInterval = 5;            //Blink interval - 5 seconds default
    [SerializeField] Transform camPos;
    [SerializeField] float blinkDuration = 1f;


    float[] timeRemaining, timeHold;    //Enable this to enable all blendshapes to change dynamically
    float timeRemainingBlink, timeHoldBlink;
    int numBlendShapes;

    [SerializeField]
    bool enableBlink;                   //Enable/Disable

    bool isActiveBlink;                 //Currently active
    bool[] direction;                   //Ramping up BS (true) or ramping down (false)
    int[] maxVal;                       //Maximum value of the Blendshape [0-100], set realtime by calling SetExpression

    [SerializeField]
    bool lookAtMe = false;              //eyes of NPC try to follow you and look at you
    private bool initialized = false;   //To detect whether Init() is completed 

    //Definition of states of blendshapes
    public const int ON = 100;
    public const int EYESOPEN = 0;     //not 100% opened for NPCF4

    private List<ExpressionConfig> expressionConfigs = new List<ExpressionConfig>();
    [HideInInspector] public int BLINK, LOOKH, LOOKV;                          //Specific Blendshapes for the eyes

    [Header("Eye BlendShapes")] //here we set the BlendShape names as per the main shape, their ranges (%) and their default value
    [SerializeField] private string blink;
    [SerializeField] private string lookHorizontal;
    [SerializeField] private string lookVertical;
    
    [Header("Debug")]
    [SerializeField] bool debug;
    string DEBUG_PREFIX = "SyncAllBlendShapes:";

    //MessageBus
    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }
    iMessage iM = new iMessage();
    

    //=========================
    //Component Implementation
    //=========================

    //Simple helper to make the Start() code more readable
    private int AddBlendShape(string aBS)
    {
        int rtnValue = gen9Shape_Mesh.GetBlendShapeIndex(aBS);
        if (debug)
            Debug.Log(DEBUG_PREFIX + transform.name.ToString() + " added BlendShape " + aBS + " has index " + rtnValue);

        return rtnValue;
    }


    // Update is called once per frame
    void Update()
    {
        if (!initialized) return;                   //Not ready yet

        //Synchronize all blendshapes at each cycle
        if (avtAnimator.GetBool("isTalking"))   //add other activities where the blendshape must be updated
            SyncBlendShapesCycle();

        //Update blinking
        if (enableBlink) BlinkManager();

        //Not used yet - optimization needed to avoid unnecessary calls in Update() loop
        for (int i = 0; i < numBlendShapes; i++)
        {
            if ((i != LOOKV) && (i != LOOKH) && (i != BLINK))             //Skip eye stuff
                  BSManager(i, maxVal[i], timeHold[i]);
        }

        //Update Eye tracking
        if (lookAtMe)
        {
            EyeToXRTrackingUpdate();
        }
    }


    //run a cycle to sync all BSs
    private void SyncBlendShapesCycle()
    {
        for (int i = 0; i < gen9Shape_Mesh.blendShapeCount; i++)    //for each blendshape
        {
            float bsVal = gen9Shape_SMR.GetBlendShapeWeight(i);

            //Set mandatory SMRs
            gen9Mouth_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Brow_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Lash_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Tear_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Eyes_SMR.SetBlendShapeWeight(i, bsVal);
        }
    }

   
    //Plays a Blendshape, if isStarting then BS increases to target value within ramp time
    // if !isStarting then BS decreases to 0 within ramp time
    private void BSManager(int bs, int maxv, float ramp )
    {
        if (bs == -1) return;                                                   //skip non-defined blendshapes

        if (timeRemaining[bs] > 0)
        {
            timeRemaining[bs] -= Time.deltaTime;
            int val = Mathf.Max((direction[bs] ? (int)(maxv * (1 - timeRemaining[bs] / ramp)) : (int)(maxv * timeRemaining[bs] / ramp)), 0);
            BlendFace(bs, val);
        }
    }
    

    //Manages blinking of the NPC
    private void BlinkManager()
    {
        //Blink timer
        if (timeRemainingBlink > 0)
        {
            timeRemainingBlink -= Time.deltaTime;
        }
        else
        {
            if (!isActiveBlink) BlendFace(BLINK, ON);                          //blink on

            isActiveBlink = true;                                             //avoid constant setting the weight at each cycle
            timeHoldBlink -= Time.deltaTime;

            if (timeHoldBlink < 0)
            {
                if (isActiveBlink) BlendFace(BLINK, EYESOPEN);                     //blink off

                isActiveBlink = false;
                timeHoldBlink = blinkDuration;
                timeRemainingBlink = blinkInterval * UnityEngine.Random.Range(-blinkInterval / 4, blinkInterval / 4);      //add some random noise
            }
        }
    }


    //NPC eyes follow the player
    //
    private void EyeToXRTrackingUpdate()
    {
        float bsoH, bsoV;
        Transform lE = npcLEye.transform;

        Vector3 delta = Quaternion.Inverse(lE.rotation) * (camPos.position - lE.position);     //Compensate for NPC rotation
        Vector3 deltaH, deltaV;

        deltaH = delta; deltaH.y = 0;        //XZ plane for horizontal tracking
        deltaV = delta; deltaV.x = 0;        //YZ plane for vertical tracking

        bsoH = Mathf.Min(Mathf.Max(100 * Mathf.Asin(deltaH.x) / delta.magnitude, -70), 70);
        bsoV = Mathf.Min(Mathf.Max(200 * Mathf.Asin(deltaV.y) / delta.magnitude, -70), 70);

        //Debug.Log(bsoH + " " + bsoV);

        BlendFace(LOOKH, (int)bsoH);
        BlendFace(LOOKV, (int)bsoV);
    }


    public void BlendFace(int what, int value)
    {
        if (what == -1)
        {
            Debug.LogError(DEBUG_PREFIX + " Illegal BlendShape index (-1), check whether the Blendshapes are configured in Inspector!");
            return;
        }
        gen9Shape_SMR.SetBlendShapeWeight(what, value);
        SyncBlendShapesCycle();
    }


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    // Start is called before the first frame update
    public async Task Init(MessageBus messageBus)
    {
        avtAnimator = GetComponent<Animator>();

        //Find the mandatory Skinned Mesh Renderers
        gen9Shape_SMR = gen9Shape.GetComponent<SkinnedMeshRenderer>();
        gen9Mouth_SMR = gen9Mouth.GetComponent<SkinnedMeshRenderer>();
        gen9Brow_SMR = gen9Brow.GetComponent<SkinnedMeshRenderer>();
        gen9Lash_SMR = gen9Lash.GetComponent<SkinnedMeshRenderer>();
        gen9Tear_SMR = gen9Tear.GetComponent<SkinnedMeshRenderer>();
        gen9Eyes_SMR = gen9Eyes.GetComponent<SkinnedMeshRenderer>();

        //Main shape mesh that dicates all other blendshapes
        gen9Shape_Mesh = gen9Shape_SMR.sharedMesh;

        //Each Skinned Mesh Renderer will have different id's for the BlendShapes - Gets INDEX for each BlendShape
        LOOKV = AddBlendShape(lookVertical);
        LOOKH = AddBlendShape(lookHorizontal);
        BLINK = AddBlendShape(blink);

        //Now we populate the BlendShape class information with the data from the Inspector
        foreach (var config in expressionConfigs)    
            foreach (var layer in config.layers)
                layer.index = AddBlendShape(layer.blendShapeName);

        //Allocate memory for the remaining time, time comparison and enableBlendShape arrays
        numBlendShapes = gen9Shape_Mesh.blendShapeCount;
        timeRemaining = new float[numBlendShapes];  //how much time of the BS is remaining
        timeHold = new float[numBlendShapes];       //in case we need a recurring BS like BLINK
        direction = new bool[numBlendShapes];
        maxVal = new int[numBlendShapes];
        isActiveBlink = false;
        
        //Blink timer stuff
        enableBlink = true;
        isActiveBlink = false;

        //Not used yet
        timeRemaining[BLINK] = blinkInterval; 
        timeHold[BLINK] = blinkDuration;           
        timeRemainingBlink = blinkInterval;

        //Eye tracking
        if (!camPos)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the camera object needed for eye tracking, check Inspector!");

        //Set initial eyes open state
        BlendFace(BLINK, EYESOPEN);

        //Initialize Layers for ExpressionConfigs
        foreach (var config in expressionConfigs)    
        {
            foreach (var layer in config.layers)
            {
                layer.index = AddBlendShape(layer.blendShapeName);
                if (layer.index == -1)
                    Debug.LogError($"{DEBUG_PREFIX} WARNING: Blendshape '{layer.blendShapeName}' not found for emotion '{config.emotionLabel}'!");
            }
        }

        initialized = true;

        await Task.Delay(0);        //Keep the compiler happy
    }

    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
        await Task.Delay(0);        //Keep the compiler happy
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
        await Task.Delay(0);        //not implemented in Open Source version
    }
}
