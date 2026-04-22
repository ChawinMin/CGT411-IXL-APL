using System.Collections;
using System.Collections.Generic;
using Samples.Whisper;
using UnityEngine;
using OpenAI;
using UnityEngine.Networking;
//using UnityEditor.MPE;
using System;
using System.Text;
using TMPro;

public class AIManager : MonoBehaviour
{
    // Payload sent to the local answer endpoint.
    [Serializable]
    private class AnswerRequest
    {
        public string prompt;
        public string question;
        public string rag;
    }

    // Supported response shapes returned by the endpoint.
    [Serializable]
    private class AnswerResponse
    {
        public string answer;
        public string response;
        public string text;
    }

    [Header("UI References")]
    [SerializeField] private TMP_Text process_text;

    [Header("References")]
    private const string askURL = "http://18.217.36.198:8000/answer";

    public Whisper whisper; //Reference to Whisper Script

    [Header("State")]
    private bool hasNewMessage; //Flag to indicate a new message has been added
    public List<ChatMessage> speechList = new List<ChatMessage>(); //The speech list to give to AIManager
    public List<string> aiResponses = new List<string>(); //List to hold AI responses
    public event Action<string> OnAIResponseReady; //Fires when a new AI response is available for TTS

    [Header("Debug")]
    private string lastUserContent; //Track last user content to avoid repeated sends
    private bool isSendingRequest; //Prevents overlapping chat-completion requests

    [Header("AI Prompt Settings")]
    public string RAGInfomration;
    private const string AIWordcount = "35"; //Limit AI responses to 35 words
    private const string FoundationsOfFlightOperationspart1 = "To instill within ourselves these qualities essential to professional excellence" + 
    "1. Discipline…Being able to follow as well as to lead, knowing that we must master ourselves before we can master our task" +
    "2. Competence…There being no substitute for total preparation and complete dedication, for space will not tolerate the careless or indifferent." +
    "3. Confidence…Believing in ourselves as well as others, knowing that we must master fear and hesitation before we can succeed." +
    "4. Responsibility…Realizing that it cannot be shifted to others, for it belongs to each of us; we must answer for what we do, or fail to do." + 
    "5. Toughness…Taking a stand when we must; to try again, and again, even if it means following a more difficult path" +
    "Teamwork…Respecting and utilizing the abilities of others, realizing that we work toward a common goal, for success depends upon the efforts of all." +
    "Vigilance…Always attentive to the dangers of spaceflight; never accepting success as a substitute for rigor in everything we do.";
    private const string FoundationsOfFlightOperationspart2 = "To always be aware that suddenly and unexpectedly we may find ourselves in a role where our performance has ultimate consequences.";
    private const string FoundationsOfFlightOperationspart3 = "To recognize that the greatest error is not to have tried and failed, but that in the trying we do not give it our best effort.";
    [SerializeField] private string promptAI = "You are a NASA mission assistant helping stackholders understand what is happening in NASA Johnson Space Center Mission Control Center. "
        + "Provide clear, concise, and accurate information based on NASA protocols and procedures. "
        + "Keep responses relevant to space missions and astronaut activities." + $"When generating a response you will follow the Foundations of Flight Operations as noted {FoundationsOfFlightOperationspart1}, {FoundationsOfFlightOperationspart2}, {FoundationsOfFlightOperationspart3}." 
        + $"When it comes to missions you will prioritize in the following 1) safety of the crew then 2) safety of the vehicle, and then 3) success of the mission. "
        + $" Do not go over {AIWordcount} words in your response.";

    // Cached so runtime changes to the working prompt can be reset if needed.
    private string originalPrompt;

    /// <summary>
    /// Finds the original prompt and references to the whisper script
    /// </summary>
    private void Awake()
    {
        try
        {
            // Finds the whisper script
            whisper = FindObjectOfType<Whisper>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Whisper script not found: " + ex.Message);
        }
        try
        {
            //Store the original prompt for future resets
            originalPrompt = promptAI; 
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Error with the original prompt: " + ex.Message);
        }
    }

    /// <summary>
    /// Queues a new chat message and marks it for processing.
    /// </summary>
    /// <param name="message">Message to add to the conversation state.</param>
    public void AddMessage(ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return; //Ignore empty/no-audio messages
        }

        if (message.Role == "user" && message.Content == lastUserContent)
        {
            return; //Skip duplicate user messages
        }

        speechList.Add(message);
        if (message.Role == "user")
        {
            lastUserContent = message.Content;
        }
        hasNewMessage = true; //Set the flag to indicate a new message has been added
    }

    public void SendRequest()
    {
        if (isSendingRequest || speechList.Count == 0)
        {
            // Avoid duplicate requests and ignore sends with no conversation content.
            return;
        }

        StartCoroutine(SendRequestCoroutine());
    }

    /// <summary>
    /// Recieve the RAG information, user question, and the system prompts and then generate a response
    /// </summary>
    /// <returns>The AI response based on the RAG information, prompts, and question</returns>
    private IEnumerator SendRequestCoroutine()
    {
        // Lock the request pipeline so Update() does not start another send mid-flight.
        isSendingRequest = true;

        // Build a readable copy of the conversation and track the latest user message.
        var questionBuilder = new StringBuilder();
        string latestUserQuestion = string.Empty;
        foreach (var msg in speechList)
        {
            // Preserve the full exchange for debugging, but send the most recent user question.
            questionBuilder.AppendLine($"{msg.Role}: {msg.Content}");
            if (msg.Role == "user")
            {
                latestUserQuestion = msg.Content;
            }
        }

        var requestPayload = new AnswerRequest
        {
            // Use the base system prompt captured during startup.
            prompt = originalPrompt,
            // Prefer the newest user question, but fall back to the whole transcript if needed.
            question = string.IsNullOrWhiteSpace(latestUserQuestion) ? questionBuilder.ToString() : latestUserQuestion,
            // RAG content is optional, so default to an empty string when none is available.
            rag = RAGInfomration ?? string.Empty
        };

        // Convert the request object into JSON before sending it to the answer service.
        var json = JsonUtility.ToJson(requestPayload);

        // Create a POST request and attach the JSON body plus expected response handling.
        using var req = new UnityWebRequest(askURL, "POST");
        var bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "application/json");

        // Log the outgoing request so backend issues can be traced from the Unity console.
        Debug.Log($"Recieved RAG Information (AIManager.cs): {RAGInfomration}");
        process_text.text = "AIManager recieved RAG info";
        Debug.Log($"Prompt being sent to AI endpoint:\n{requestPayload.prompt}\nUser Question:\n{requestPayload.question}\nRAG Information:\n{requestPayload.rag}");
        Debug.Log($"Sending prompt to endpoint: {askURL}");
        foreach(var m in speechList)
        {
            Debug.Log($"{m.Role}: {m.Content}");
        }

        // Pause the coroutine until the web request completes.
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // Exit early on transport or server failure and release the send lock.
            Debug.LogError($"AI endpoint failed: {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
            isSendingRequest = false;
            yield break;
        }

        // Start with the raw response in case the backend returns plain text instead of JSON.
        var responseText = req.downloadHandler?.text ?? string.Empty;
        var aiText = responseText;
        process_text.text = "AI response received, processing...";

        try
        {
            // Accept a few common property names so minor backend response changes do not break parsing.
            var parsed = JsonUtility.FromJson<AnswerResponse>(responseText);
            if (parsed != null)
            {
                if (!string.IsNullOrWhiteSpace(parsed.answer))
                {
                    aiText = parsed.answer;
                }
                else if (!string.IsNullOrWhiteSpace(parsed.response))
                {
                    aiText = parsed.response;
                }
                else if (!string.IsNullOrWhiteSpace(parsed.text))
                {
                    aiText = parsed.text;
                }
            }
        }
        catch (Exception ex)
        {
            // If JSON parsing fails, keep using the raw response text.
            Debug.LogWarning($"Could not parse AI endpoint JSON response, using raw text: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(aiText))
        {
            // Save the answer, notify any listeners, and clear processed conversation data.
            aiResponses.Add(aiText); // Store the AI response
            Debug.Log($"AI: {aiText}");
            process_text.text = "AI response received, processing...";
            OnAIResponseReady?.Invoke(aiText); // Trigger TTS immediately (no polling delay)
            speechList.Clear(); // Clear the speech list after processing
        }

        // Allow the next message to be sent.
        isSendingRequest = false;

    }
    
    /// <summary>
    /// Watches for newly queued speech and kicks off the request coroutine.
    /// </summary>
    private void Update()
    {
        //Debug.Log("AI Manager Update Loop"); //Debug line to ensure Update is running
        if(hasNewMessage && !isSendingRequest)
        {
            //Debug.Log("New message detected, sending request to OpenAI."); //Debug line to confirm new message detection
            hasNewMessage = false; //Reset the flag
            SendRequest();
        }
        
    }
}
