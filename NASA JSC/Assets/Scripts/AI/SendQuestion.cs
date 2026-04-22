using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OpenAI;
using TMPro;

/// <summary>
/// Sends a user's question through the RAG pipeline and then forwards it to the AI manager.
/// </summary>
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

    /// <summary>
    /// Finds the AIManager and RAG components used to process submitted questions.
    /// </summary>
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
    
    /// <summary>
    /// Starts the coroutine that submits a new question.
    /// </summary>
    /// <param name="questionToSend">The user question to send through the AI workflow.</param>
    public void Send(string questionToSend){
        StartCoroutine(SendQuestionToRAGAndAI(questionToSend));
    }

    /// <summary>
    /// Sends the question to RAG first, waits briefly for context, then forwards the question to AIManager.
    /// </summary>
    /// <param name="question">The text of the user's question.</param>
    /// <returns>Coroutine used to coordinate the RAG and AI request order.</returns>
    private IEnumerator SendQuestionToRAGAndAI(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            // Ignore empty submissions so no unnecessary requests are made.
            yield break;
        }

        if (rag != null)
        {
            // Clear any older RAG result before sending a new retrieval request.
            rag.answerFromRAG = string.Empty;
            rag.AskQuestion(question);
            Debug.Log("Question sent to RAG: " + question);
            process_text.text = "Question sent to RAG";

            // Default values for timeout and elapsed
            const float timeoutSeconds = 15f;
            float elapsed = 0f;

            // Wait until RAG returns context or the timeout is reached.
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
                // Pass the retrieved context to AIManager before the user message is added.
                aiManager.RAGInfomration = rag.answerFromRAG;
            }
        }

        if (aiManager != null)
        {
            // Add the user question to the AI conversation so the response pipeline can begin.
            aiManager.AddMessage(new ChatMessage
            {
                Role = "user",
                Content = question
            });
            Debug.Log("Question sent to AIManager: " + question);
        }
    }

    /// <summary>
    /// Listens for the Enter key and submits the current question once per unique input.
    /// </summary>
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!string.IsNullOrWhiteSpace(questionToSend) && questionToSend != previousQuestion)
            {
                // Prevent repeated sends of the same text and clear the field after submission.
                process_text.text = "Question recieved";
                Send(questionToSend);
                previousQuestion = questionToSend;
                questionToSend = string.Empty; // Clear the input after sending
            }
        }
    }
}
