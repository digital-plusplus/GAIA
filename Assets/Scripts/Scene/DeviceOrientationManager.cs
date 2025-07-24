using UnityEngine;
using UnityEngine.Rendering;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Simplified Open-Source version of Device Orientation Manager component 
///  Just the basic strawman, not actually using device orientation and assuming Landscape orientation
///  MessageBus:    NO
///  Tool-Calling:  NO
/// </summary>


public class DeviceOrientationManager : MonoBehaviour
{
    DeviceOrientation curOrientation, oldOrientation;           //To determine whether we're in portrait or in landscape mode state changes
    [SerializeField] public Vector3 imagePositionLandscape;     //Where in the screen do we spawn the webcam and TTI images
    private Vector3 imagePosition;                              //Handled by Update(), depends on realtime orientation
    private Quaternion imageRotation;                           // idem
    private int curCam;                                         //0=no cam, 1=rear cam, 2=front cam - to compensate portrait+frontcam==upside down!
    [SerializeField] private Shader unLitShader;
    
    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "DOM: ";


    void Start()
    {
        //Initialize orientation change detection
        SetImageFrameOrientation();                              //Reposition imageFrame   
    }


    //This initializes the Gyro and sets the internal variables that detect state change in Update()
    public void InitializeDeviceOrientation()
    {
        if (debug)
            Debug.Log(DEBUG_PREFIX + "Device orientation initialized");
    }


    public void SetImageFrameOrientation()
    {
        if (debug) Debug.Log(DEBUG_PREFIX + "UpdateImageFrameWithOrientation to " + curOrientation.ToString() + ", camera = " + curCam);

        imagePosition = imagePositionLandscape;
        imageRotation = Quaternion.Euler(0, 0, 0);
                
        GameObject genImg = GameObject.Find("ImageFrame(Clone)");                   //We want to display what the camera is seeing on screen
        if (genImg)
        {
            genImg.transform.position = imagePosition;
            genImg.transform.rotation = imageRotation;
        }
        else Debug.Log(DEBUG_PREFIX + "ImageFrame is not available so can't rotate it");
    }
    

    //Generic method to fill the imageframe with a texture - the ImageFrame is also part of the UI hence its located here 
    // called by TTI components and LLM_Vision components
    public void ShowImageFrame(Texture texture, int invertPortrait)
    {
        //Position the ImageFrame acc to the current device orientation
        curCam = invertPortrait;                                            //Copy current camera for changes in orientation
        SetImageFrameOrientation();                                         //ENforce the position and rotation based on the current state

        GameObject genImg = GameObject.Find("ImageFrame(Clone)");           //We want to display what the camera is seeing on screen
        if (!genImg)
        {
            genImg = Resources.Load<GameObject>("ImageFrame");
            genImg = Instantiate(genImg, imagePosition, Quaternion.Euler(7.44f,0, 0)*imageRotation);
        }

        if (!genImg) Debug.Log("Can't load the ImageFrame for the webcam output");
        else
        {
            Material myNewMaterial = new Material(unLitShader);             //assign explicitly in Inspector. Find() doesnt always work
            myNewMaterial.SetTexture("_BaseMap", texture);
            myNewMaterial.SetInt("_Cull", (int)CullMode.Off);               //To Test on iOS!
            
            genImg.GetComponent<MeshRenderer>().material = myNewMaterial;                                      
        }
    }
    
    //Cleanup code as Android can be sloppy with releasing 
    public void DestroyImageFrame()
    {
        GameObject genImg = GameObject.Find("ImageFrame(Clone)");
        if (genImg) Destroy(genImg);
    }    
}
