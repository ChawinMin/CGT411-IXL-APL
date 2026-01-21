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
        speechList.Add(message);
        hasNewMessage = true; //Set the flag to indicate a new message has been added
    }

    public async void SendRequest()
    {
        var messages = new List<ChatMessage>(speechList);
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
