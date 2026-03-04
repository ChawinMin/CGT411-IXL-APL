using System.Collections;
using System.Collections.Generic;
using Samples.Whisper;
using UnityEngine;
using OpenAI;
using System.Threading.Tasks;
//using UnityEditor.MPE;
using System;

public class AIManager : MonoBehaviour
{
    [Header("References")]
    private OpenAIApi APIKey = new OpenAIApi(); //API Key to OpenAI
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

    private void Awake()
    {
        try
        {
            whisper = FindObjectOfType<Whisper>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Whisper script not found: " + ex.Message);
        }
    }

    public void Start()
    {
        /*
        Check to ensure that the API is valid and reachable
        */

        //Check if the API Key is set
        if(APIKey == null)
        {
            Debug.LogError("API Key is not set in the AIManager.");
            return;
        }
    }

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

    public async void SendRequest()
    {
        if (isSendingRequest || speechList.Count == 0)
        {
            return;
        }

        isSendingRequest = true;

        //Add the RAG information to the prompt for additional context for the AI response
        promptAI += $"In your response, pick the most important information in {RAGInfomration} but do not go over {AIWordcount} words."; 
        Debug.Log($"Recieved RAG Information (AIManager.cs): {RAGInfomration}"); //Debug line to confirm RAG information is being received

        var messages = new List<ChatMessage>();
        //Add the system prompt first
        messages.Add(new ChatMessage
        {
            Role = "system",
            Content = promptAI
        });
        messages.AddRange(speechList); //Add all messages from the speech list

        //Create the chat completion request based on the messages and prompts
        var req = new CreateChatCompletionRequest()
        {
            Model = "gpt-5-nano",
            Messages = messages
        };

        try
        {
           var res = await APIKey.CreateChatCompletion(req);
           foreach(var m in speechList)
            {
                Debug.Log($"{m.Role}: {m.Content}");
            }
           if(res.Choices != null && res.Choices.Count > 0)
            {
                var aiText = res.Choices[0].Message.Content;
                aiResponses.Add(aiText); // Store the AI response
                Debug.Log($"AI: {aiText}");
                OnAIResponseReady?.Invoke(aiText); // Trigger TTS immediately (no polling delay)
                speechList.Clear(); // Clear the speech list after processing
                promptAI = ""; //Reset the prompt to avoid repeated RAG information in future requests
                
            }
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
        finally
        {
            isSendingRequest = false;
        }
        
    }

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
