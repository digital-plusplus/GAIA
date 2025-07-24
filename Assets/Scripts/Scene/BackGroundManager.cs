using System.Collections.Generic;
using UnityEngine;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Manages the background/scene
///  MessageBus:    NO
///  Tool-Calling:  NO
/// </summary>

public class BackGroundManager : MonoBehaviour
{
    public List<Texture2D> backgroundTextures;
    private int currentTextureIndex = 0;

    const string DEBUG_PREFIX = "BACKGROUNDMANAGER: ";
    [SerializeField] bool debug;


    //Changes to the next available background, defined in the Inspector
    // returns the id of the new background for preference manager
    public int NextBackGround()
    {
        Material[] currentMaterials = GetComponent<MeshRenderer>().materials;

        currentTextureIndex =(currentTextureIndex+1)% backgroundTextures.Count;
        currentMaterials[0].SetTexture("_BaseMap", backgroundTextures[currentTextureIndex]);
        if (debug) 
            Debug.Log(DEBUG_PREFIX+ "switching to background "+ currentTextureIndex);

        return currentTextureIndex;
    }


    public void SetBackGround(int backGroundID)
    {
        Material[] currentMaterials = GetComponent<MeshRenderer>().materials;

        currentMaterials[0].SetTexture("_BaseMap", backgroundTextures[backGroundID]);
    }

}
