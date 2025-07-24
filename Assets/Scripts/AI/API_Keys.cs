using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// This component reads the api keys from a local file. This is a HIGHLY simplified NON_SECURE version of the non-GPL 
/// version that has remote key retrieval, encryption and iOS PList integration!
///  MessageBus:    NO
///  Tool-Calling:  NO
/// </summary>

//*************IMPORTANT*************************************************************************
//!! The API Key variables are loaded from a file apikeys.txt which is EXCLUDED by .gitignore !!!
//*************IMPORTANT*************************************************************************


public class API_Keys : MonoBehaviour
{
    
    [Header("Local Configuration(insecure)")]
    [SerializeField] public string filePath;               //file + path

    [SerializeField] bool debug;

    private Dictionary<string, string> apiKeys = new Dictionary<string, string>();          //The dictionary that contains the actual API keys 
    const string DEBUG_PREFIX="API_Keys: ";


    //Public method to request the API key for a service, API keys are all in a simple text file 
    public string GetAPIKey(string serviceName)
    {
        return (apiKeys.ContainsKey(serviceName) ? apiKeys[serviceName] : null);
    }


    public async Task Init()
    {
        //We check whether the server URL is provided, otherwise we will use a local stored file (debug only, insecure)
        ReadAPIKeys();   
        await Task.Delay(0);
    }


    //Read API keys from a simple text file, this file must NEVER be synchronized by GitHub!
    private void ReadAPIKeys()
    {
        TextAsset textFile = Resources.Load<TextAsset>(filePath);
        
        if (textFile!=null)
        {
            string[] lines = textFile.text.Split('\n');
            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts.Length == 2)
                    apiKeys[parts[0].Trim()] = parts[1].Trim();
                else Debug.LogError(DEBUG_PREFIX + "illegal API key line found, check your API keys file!");
            }
        }
        else Debug.LogError(DEBUG_PREFIX + "API keys file not found!");
    }
}
