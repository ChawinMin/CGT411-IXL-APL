using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OpenAI;
using TMPro;

public class SendQuestion : MonoBehaviour
{
    [Header("References")]
    public AIManager aiManager;
    public RAG rag;

    [Header("UI References")]
    [SerializeField] private TMP_Text process_text; //Reference to process text UI element

    [Header("Question Settings")]
    [SerializeField] private string questionToSend;
    private string previousQuestion;

    public void Awake(){
        try
        {
            aiManager = FindObjectOfType<AIManager>();
            rag = FindObjectOfType<RAG>();
        }
        catch(Exception e)
        {
            Debug.Log(e.Message);
        }
    }
    
    public void Send(string questionToSend){
        StartCoroutine(SendQuestionToRAGAndAI(questionToSend));
    }

    private IEnumerator SendQuestionToRAGAndAI(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            yield break;
        }

        if (rag != null)
        {
            rag.answerFromRAG = string.Empty;
            rag.AskQuestion(question);
            Debug.Log("Question sent to RAG: " + question);
            process_text.text = "Question sent to RAG";


            const float timeoutSeconds = 15f;
            float elapsed = 0f;

            while (string.IsNullOrWhiteSpace(rag.answerFromRAG))
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeoutSeconds)
                {
                    Debug.LogWarning("Timed out waiting for RAG response. Sending question to AIManager without RAG context.");
                    break;
                }

                yield return null;
            }

            if (!string.IsNullOrWhiteSpace(rag.answerFromRAG) && aiManager != null)
            {
                aiManager.RAGInfomration = rag.answerFromRAG;
            }
        }

        if (aiManager != null)
        {
            aiManager.AddMessage(new ChatMessage
            {
                Role = "user",
                Content = question
            });
            Debug.Log("Question sent to AIManager: " + question);
        }
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!string.IsNullOrWhiteSpace(questionToSend) && questionToSend != previousQuestion)
            {
                process_text.text = "Question recieved";
                Send(questionToSend);
                previousQuestion = questionToSend;
                questionToSend = string.Empty; // Clear the input after sending
            }
        }
    }
}
