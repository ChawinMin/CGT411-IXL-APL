using System.Collections;
using System.Collections.Generic;
using Samples.Whisper;
using UnityEngine;
using OpenAI;
using System.Threading.Tasks;
using UnityEditor.MPE;
using System;

public class AIManager : MonoBehaviour
{
    private OpenAIApi APIKey = new OpenAIApi(); //API Key to OpenAI

    public Whisper whisper; //Reference to Whisper Script

    private bool hasNewMessage; //Flag to indicate a new message has been added

    public List<ChatMessage> speechList = new List<ChatMessage>(); //The speech list to give to AIManager

    public List<string> aiResponses = new List<string>(); //List to hold AI responses

    private const string AIWordcount = "30"; //Limit AI responses to 30 words

    private string lastUserContent; //Track last user content to avoid repeated sends

    [SerializeField]
    private string promptAI = 
    "You are a NASA mission assistant helping stackholders understand what is happening in NASA Johnson Space Center Mission Control Center. "
        + "Provide clear, concise, and accurate information based on NASA protocols and procedures. "
        + "Keep responses relevant to space missions and astronaut activities." + $" Do not go over {AIWordcount} words in your response.";

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
                aiResponses.Add(res.Choices[0].Message.Content); //Store the AI response
                Debug.Log($"AI: {res.Choices[0].Message.Content}");
                speechList.Clear(); //Clear the speech list after processing
                
            }
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
        
    }

    private void Update()
    {
        //Debug.Log("AI Manager Update Loop"); //Debug line to ensure Update is running
        if(hasNewMessage)
        {
            //Debug.Log("New message detected, sending request to OpenAI."); //Debug line to confirm new message detection
            hasNewMessage = false; //Reset the flag
            SendRequest();
        }
        
    }
}
