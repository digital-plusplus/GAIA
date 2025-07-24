using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Networking; // Required for UnityWebRequest.EscapeURL
using System.Collections;
using System.Text.RegularExpressions;

/// <summary>
/// Retrieves context for the LLM from Google Web Search
/// 20250419 DigitalPlusPlus
/// </summary>
public class RAG_Google_WebSearch : MonoBehaviour
{
    private string apiKey;
    private string searchEngineId;
    [SerializeField]
    private int maxResults;                                     //Maximum nr of results we want to receive
    const string apiURI = "https://www.googleapis.com/customsearch/v1";

    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "RAG_GOOGLE_WS: ";              //prefix we use for debugging

    AI_Orchestrator aiO;                                        //link to the AI Orchestrator
    API_Keys api_Keys;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Init()
    {
        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else
        {
            apiKey = api_Keys.GetAPIKey("Google_API_Key");
            searchEngineId = api_Keys.GetAPIKey("Google_SearchID");
            if (debug) 
                Debug.Log(DEBUG_PREFIX + "API Key: " + apiKey + ", Search Engine ID: " + searchEngineId);
        }
        
        if ((apiKey == "")||(searchEngineId == ""))
            Debug.LogWarning(DEBUG_PREFIX + "Warning: API key and/or search engine id not found, check API Key File!");

        aiO = GetComponent<AI_Orchestrator>();
        if (!aiO)
        {
            Debug.LogError(DEBUG_PREFIX + "AI Orchestrator component not found!");
            return;
        }
    }


    public static string RemoveHTMLTags(string input)
    {
        // Regular expression to match HTML tags
        string pattern = "<.*?>";

        // Replace HTML tags with an empty string
        return Regex.Replace(input, pattern, string.Empty);
    }


    //Public function call, called by GetTopLinks and possibly by AI Orchestrator when a user directly wants to know about a site 
    public async Task<string> GetURLContent(string url)
    {
        string rtn = "";

        UnityWebRequest request = new UnityWebRequest(url, "GET");
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        await request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            rtn = RemoveHTMLTags(request.downloadHandler.text);
        }
        return rtn;
    }

    //Public function call, used by AI Orchestrator
    public async Task<string> GetTopLinks(string question, int topn)
    {
        //Requesting is simple, we don't need to send a JSON so we just construct a GET WebRequest!
        string toSend = apiURI + "?key=" + apiKey + "&cx=" + searchEngineId + "&q=" + UnityWebRequest.EscapeURL(question) + "&num=" + topn;

        if (debug) 
            Debug.Log(DEBUG_PREFIX + toSend);

        //aiO.Say("Sure, searching the WEB");

        UnityWebRequest request = new UnityWebRequest(toSend, "GET");
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            //Debug.Log(DEBUG_PREFIX + request.downloadHandler.text);
            string rtn = "";
            SimplifiedSearchResponse response = JsonUtility.FromJson<SimplifiedSearchResponse>(request.downloadHandler.text);
            if (response != null && response.searchInformation != null && response.items != null)
            {
                int cnt = 0;
                foreach (var item in response.items)
                {
                    cnt++;
                    if (cnt > topn) return rtn;
                    if (debug)
                        Debug.Log(DEBUG_PREFIX + item.link);
                    //rtn += "URL:" + item.link + ", Snippet:" + item.snippet;
                    //Now we open the URLs and add the contents of the pages
                    rtn += "\n===\n";
                    rtn += "URL:" + item.link;
                    rtn += "CONTENT:" + await GetURLContent(item.link);
                    rtn += "===\n";                    
                }
            }
            return rtn;
        }
        else
        {
            Debug.LogError(DEBUG_PREFIX + request.error);
            return null;
        }
    }

    [System.Serializable]
    public class SimplifiedItem
    {
        public string snippet;
        public string link;
    }

    [System.Serializable]
    public class SimplifiedSearchInformation
    {
        public string totalResults;
    }

    [System.Serializable]
    public class SimplifiedSearchResponse
    {
        public SimplifiedSearchInformation searchInformation;
        public SimplifiedItem[] items;
    }


}
