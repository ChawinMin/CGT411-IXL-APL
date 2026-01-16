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

    public List<ChatMessage> speechList = new List<ChatMessage>(); //The speech list to give to AIManager

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
                Debug.Log($"{m.Role} {m.Content}");
            }
           if(res.Choices != null && res.Choices.Count > 0)
            {
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
        SendRequest();
    }
}
