using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking; // Required for UnityWebRequest.EscapeURL
using System.Text.RegularExpressions;
using imessages;

//-----------------------------------------------------------
// Project GAIA - 2026
// Developed by DigitalPlusPlus
// This code is licensed under GPL 3.0
//-----------------------------------------------------------

/// <summary>
/// Retrieves context for the LLM from Google Web Search and opens a website in a browser
///  MessageBus:    YES
///  Tool-Calling:  YES
/// </summary>


public class RAG_Google_WebSearch : MonoBehaviour, IRagWebService, IAIComponent
{
    private string apiKey;
    private string searchEngineId;
    [SerializeField] private int maxCrawlCharacters = 500;                                  //Maximum to the size of the individual URL page contents
    [SerializeField] private int topNDefault = 5;                                           //if not provided we will pick 5 results
    const string apiURI = "https://www.googleapis.com/customsearch/v1";

    [SerializeField] bool debug;
    const string DEBUG_PREFIX = "RAG_GOOGLE_WS: ";                                          //prefix we use for debugging
    iMessage iM = new iMessage();

    API_Keys api_Keys;

    private MessageBus _messageBus;
    public ComponentID ComponentId { get; set; }



    private string CleanHtmlContent(string html)
    {
        // Remove HTML comments
        string noComments = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);

        // Remove script tags and their content
        string noScripts = Regex.Replace(noComments, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Remove style tags and their content
        string noStyles = Regex.Replace(noScripts, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Remove all remaining HTML tags
        string noTags = Regex.Replace(noStyles, @"<[^>]*>", "");

        // Decode HTML entities (e.g., &amp; to &)
        string decodedText = System.Net.WebUtility.HtmlDecode(noTags);

        // Replace multiple newlines/whitespace with single newlines and ensure no excessive leading/trailing whitespace on lines
        string cleanText = Regex.Replace(decodedText, @"\s*\n\s*", "\n").Trim();
        cleanText = Regex.Replace(cleanText, @"[ \t]+", " "); // Replace multiple spaces/tabs with single space

        return cleanText;
    }


    //Public function call, called by GetTopLinks and possibly by AI Orchestrator when a user directly wants to know about a site 
    // we truncate the length to maxLen to avoid we overload our LLM
    public async Task<string> GetURLContent(string url, int maxLen)
    {
        string rtn = "";
        if (debug) Debug.Log(DEBUG_PREFIX + "Crawling URL: " + url);

        UnityWebRequest request = new UnityWebRequest(url, "GET");
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        await request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            rtn = CleanHtmlContent(request.downloadHandler.text);

            if (rtn.Length > maxLen)
                rtn=rtn.Substring(0, maxLen);                       //truncate the string to the maximum size - not very smart but avoids LLM rate overload

            if (debug)
                Debug.Log(DEBUG_PREFIX + rtn);
        }
        return rtn;
    }


    //Public function call, used by AI Orchestrator
    // Finds the top n URLs and then crawls these url's by calling GetURLContent
    public async Task<string> GetTopLinks(string question, int topn)
    {
        //Requesting is simple, we don't need to send a JSON so we just construct a GET WebRequest!
        string toSend = apiURI + "?key=" + apiKey + "&cx=" + searchEngineId + "&q=" + UnityWebRequest.EscapeURL(question) + "&num=" + topn;

        if (debug)
            Debug.Log(DEBUG_PREFIX + toSend);

        //Http request
        UnityWebRequest request = new UnityWebRequest(toSend, "GET");
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string rtn = "";
            SimplifiedSearchResponse response = JsonUtility.FromJson<SimplifiedSearchResponse>(request.downloadHandler.text);
            if (response != null && response.searchInformation != null && response.items != null)
            {
                int cnt = 0;
                foreach (var item in response.items)
                {
                    cnt++;
                    if (cnt > topn) return rtn;         //we stop when we have found the best n items
                    if (debug)
                        Debug.Log(DEBUG_PREFIX + item.link);

                    //Now we open the URLs and add the contents of the pages
                    rtn += "\n===\n";
                    rtn += "URL:" + item.link;
                    rtn += "CONTENT:" + await GetURLContent(item.link, maxCrawlCharacters);
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


    //=====================================================================================
    // IAIComponent IMPLEMENTATION
    //=====================================================================================
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public async Task Init(MessageBus messageBus)
    {
        _messageBus = messageBus;                                   //Ensure we link to the active bus
        ComponentId = ComponentID.RAG_Web_Service;                  //Make yourself known

        Debug.Log(DEBUG_PREFIX + "Initializing");
        await _messageBus.Subscribe<RAGWSRequestMessage>(HandleRAGWSRequestAsync);
        Debug.Log(DEBUG_PREFIX + " started and subscribed to RAGWSRequestMessage.");

        //We first retrieve the API keys from the API Key component
        api_Keys = GetComponent<API_Keys>();
        if (!api_Keys)
            Debug.LogError(DEBUG_PREFIX + "Cannot find the API Keys component, please check the Inspector!");
        else
        {
            apiKey = api_Keys.GetAPIKey("Google_API_Key");
            searchEngineId = api_Keys.GetAPIKey("Google_SearchID");
        }

        if ((apiKey == "") || (searchEngineId == ""))
            Debug.LogWarning(DEBUG_PREFIX + "Warning: API key and/or search engine id not found, check API Key File!");

        await Task.Delay(0);    //dummy for now
    }


    // Unsubscribes from the message bus.
    public async Task Stop()
    {
        await _messageBus.Unsubscribe<RAGWSRequestMessage>(HandleRAGWSRequestAsync);
        Debug.Log(DEBUG_PREFIX + ComponentId + " stopped.");
    }


    private void OnDestroy()
    {
        //Cleanup

        // Gracefully unsubscribe when the object is destroyed.
        _ = Stop();
    }


    //=====================================================================================
    // MESSAGE BUS HANDLERS
    //=====================================================================================

    //Outgoing messages
    public async Task RespondMessage(string content, ComponentID requestorId, bool isError)
    {
        RAGWSResponseMessage message = new RAGWSResponseMessage()
        {
            Command = MessageCommands.isRAGWSResponse,
            Content = content,
            SenderId = ComponentId,
            TargetId = requestorId,
            IsError = false
        };
        await _messageBus.Publish<RAGWSResponseMessage>(message);
    }

    public async Task RespondMessage(MessageCommands originatingCommand, string content, ComponentID requestorId, bool isError) //overload with 1 addl parameter
    {
        RAGWSResponseMessage message = new RAGWSResponseMessage()
        {
            Command = MessageCommands.isRAGWSResponse,
            Content = content,
            SenderId = ComponentId,
            IsError = false,
            TargetId = requestorId,
            originatingMessageCommand = originatingCommand
        };
        await _messageBus.Publish<RAGWSResponseMessage>(message);
    }


    private async Task HandleRAGWSRequestAsync(RAGWSRequestMessage message)
    {
        if (debug)
            iM.Log(ComponentId.ToString(), message.SenderId.ToString(), message.Command.ToString() + "TOP " + message.topn, message.Content);

        //Lets see what we received and act accordingly
        switch (message.Command)
        {
            case MessageCommands.isRAGWSGetTopLinks:                                                                //Expects topN to be provided in the message
                string result = await GetTopLinks(message.Content, message.topn > 0 ? message.topn : topNDefault);  //Action, default if topn is missing/zero
                if (result!=null)
                    await RespondMessage(message.Command, result, message.SenderId, false);                         //Respond with content
                else 
                    await RespondMessage(message.Command, "could not get top links", message.SenderId, true);       //Respond with error
                break;


            case MessageCommands.isRAGWSGetURLContent:                                                                  
                string result2 = await GetURLContent(message.Content, maxCrawlCharacters);                          //Action
                if (result2!=null)
                    await RespondMessage(message.Command, result2, message.SenderId, false);                        //Respond with content
                else 
                    await RespondMessage(message.Command, "could not get URL Content", message.SenderId, true);     //Respond with error
                break;
                 

            //We received nonsense.
            default:
                await RespondMessage(message.Command, "Unknown message type", message.SenderId, true);              //Respond with error
                break;
        }
    }
}
