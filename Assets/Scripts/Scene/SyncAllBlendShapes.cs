using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class SyncAllBlendShapes : MonoBehaviour
{
    [Header("Connect to Shapes")]
    [SerializeField] GameObject gen9Shape;
    [SerializeField] GameObject gen9Mouth;
    [SerializeField] GameObject gen9Brow;
    [SerializeField] GameObject gen9Lash;
    [SerializeField] GameObject gen9Tear;
    [SerializeField] GameObject gen9Eyes;

    SkinnedMeshRenderer gen9Shape_SMR, gen9Mouth_SMR, gen9Brow_SMR, gen9Lash_SMR, gen9Tear_SMR, gen9Eyes_SMR;
    Mesh gen9Shape_Mesh;
    Transform camPos;       //link to the position of the XR camera (ie. the eyes of the player)

    [SerializeField]
    GameObject npcLEye, npcREye;
    Animator avtAnimator;

    [Header("Eye Controls")]

    [SerializeField]
    float blinkInterval = 5;            //Blink interval - 5 seconds default

    [SerializeField]
    float blinkDuration = 1f;
    //float[] timeRemaining, timeHold;  //Enable this to enable all blendshapes to change dynamically
    float timeRemainingBlink, timeHoldBlink;

    [SerializeField]
    bool enableBlink;                   //Enable/Disable

    bool isActiveBlink;                 //Currently active
    //bool[] direction;                   //Ramping up BS (true) or ramping down (false)
    //int[] maxVal;                       //Maximum value of the Blendshape [0-100], set realtime by calling SetExpression

    [SerializeField]
    bool lookAtMe = false;              //eyes of NPC try to follow you and look at you

    //Definition of states of blendshapes
    public const int ON = 100;
    public const int OFF = 0;
    public const int HALF = 50;
    public const int THIRD = 30;

    [Header("BlendShapes")] //here we set the BlendShape names as per the main shape
    [SerializeField] private string blink;
    [SerializeField] private string lookHorizontal;
    [SerializeField] private string lookVertical;
    [SerializeField] private string smile;
    [SerializeField] private string serious;
    [SerializeField] private string annoyed;
    [SerializeField] private string squint;

    [HideInInspector] public int BLINK, LOOKH, LOOKV;
    [HideInInspector] public int SMILE, SERIOUS, ANNOYED, SQUINT;

    [SerializeField]
    bool debug;
    string DEBUG_PREFIX = "SyncAllBlendShapes:";
    //int numBlendShapes = 0;

    // Start is called before the first frame update
    void Start()
    {
        avtAnimator = GetComponent<Animator>();

        gen9Shape_SMR = gen9Shape.GetComponent<SkinnedMeshRenderer>();
        gen9Mouth_SMR = gen9Mouth.GetComponent<SkinnedMeshRenderer>();
        gen9Brow_SMR = gen9Brow.GetComponent<SkinnedMeshRenderer>();
        gen9Lash_SMR = gen9Lash.GetComponent<SkinnedMeshRenderer>();
        gen9Tear_SMR = gen9Tear.GetComponent<SkinnedMeshRenderer>();
        gen9Eyes_SMR = gen9Eyes.GetComponent<SkinnedMeshRenderer>();
        gen9Shape_Mesh = gen9Shape_SMR.sharedMesh;

        //Each Skinned Mesh Renderer will have different id's for the BlendShapes - Gets INDEX for each BlendShape
        LOOKV = AddBlendShape(lookVertical);
        LOOKH = AddBlendShape(lookHorizontal);
        BLINK = AddBlendShape(blink);
        SERIOUS = AddBlendShape(serious);
        SMILE = AddBlendShape(smile);
        ANNOYED = AddBlendShape(annoyed);
        SQUINT = AddBlendShape(squint);
        //if (debug) Debug.Log(DEBUG_PREFIX + LOOKV + " " + LOOKH + " " + BLINK + " " + SERIOUS + " " + SMILE + " " + ANNOYED + " " + SQUINT);

        /*Not used yet
        //Allocate memory for the remaining time, time comparison and enableBlendShape arrays
        numBlendShapes = gen9Shape_Mesh.blendShapeCount;
        timeRemaining = new float[numBlendShapes];  //how much time of the BS is remaining
        timeHold = new float[numBlendShapes];       //in case we need a recurring BS like BLINK
        direction = new bool[numBlendShapes];
        maxVal = new int[numBlendShapes];
        isActiveBlink = false;
        */

        //Blink timer stuff
        enableBlink = true;
        isActiveBlink = false;

        /* Not used yet
        timeRemaining[BLINK] = blinkInterval; 
        timeHold[BLINK] = blinkDuration;      
        */
        timeRemainingBlink = blinkInterval;

        //Eye tracking
        camPos = GameObject.Find("Camera").transform;      //player eyes
    }


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
        //Synchronize all blendshapes at each cycle
        if (avtAnimator.GetBool("isTalking"))   //add other activities where the blendshape must be updated
            SyncBlendShapesCycle();

        //Update blinking
        if (enableBlink) BlinkManager();

        //Not used in Stalland - optimization needed to avoid unnecessary calls in Update() loop
        /*for (int i = 0; i < numBlendShapes; i++)
        {
            if ((i != LOOKV) && (i != LOOKH) && (i != BLINK))             //Skip eye stuff
                  BSManager(i, 100, timeHold[i]);
        }*/

        //Update Eye tracking
        if (lookAtMe)
        {
            EyeToXRTrackingUpdate();
        }
    }


    /* Not used yet
    //These public methods set the expression for a certain duration and a certain ramp-up time
    public void SetExpression(int bs, float rampup, int maxValue)
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + "SET Expression " + bs + " for " + rampup + " seconds");

        timeRemaining[bs] = rampup;
        timeHold[bs] = rampup;          //store the total ramp time for reference per frame
        direction[bs] = true;
        maxVal[bs] = maxValue;          //Store the max the expression should go for in this particular call (in %)
        //IMPORTANT: Update() is now reducing timeRemaining[bs] each cycle
    }


    public void ResetExpression(int bs, float rampdown)
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + "RESET Expression " + bs + " for " + rampdown + " seconds");

        timeRemaining[bs] = rampdown;
        timeHold[bs] = rampdown;        //store the total ramp time for reference per frame
        direction[bs] = false;

        //IMPORTANT: Update() is now reducing timeRemaining[bs] each cycle
    }
    */

    //run a cycle to sync all BSs
    private void SyncBlendShapesCycle()
    {
        for (int i = 0; i < gen9Shape_Mesh.blendShapeCount; i++)    //for each blendshape
        {
            float bsVal = gen9Shape_SMR.GetBlendShapeWeight(i);
            gen9Mouth_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Brow_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Lash_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Tear_SMR.SetBlendShapeWeight(i, bsVal);
            gen9Eyes_SMR.SetBlendShapeWeight(i, bsVal);
        }
    }

    /* Not used yet
    //Plays a Blendshape, if isStarting then BS increases to target value within ramp time
    // if !isStarting then BS decreases to 0 within ramp time
    private void BSManager(int bs, int maxVal, float ramp )
    {
        if (bs == -1) return;                                                   //skip non-defined blendshapes

        if (timeRemaining[bs] > 0)
        {
            timeRemaining[bs] -= Time.deltaTime;
            int val = Mathf.Max((direction[bs] ? (int)(maxVal * (1 - timeRemaining[bs] / ramp)) : (int)(100 * timeRemaining[bs] / ramp)), 0);
            BlendFace(bs, val);

            //Debug.Log(DEBUG_PREFIX + " Time Remaining:" + timeRemaining[bs] + " Ramp=" + ramp + " BS Value=" + val);
        }
    }
    */


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
                if (isActiveBlink) BlendFace(BLINK, OFF);                     //blink off

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


    //When talked to the NPC, it should smile a little
    public void SetListen(bool value)
    {
        BlendFace(SQUINT, value ? 50 : 0);
    }


    //When talked to the NPC, it should smile a little
    public void SetSmile(bool value)
    {
        BlendFace(SMILE, value ? 18 : 0);
    }

    public void SetSerious(bool value)
    {
        BlendFace(SERIOUS, value ? 25 : 0);
    }

}
