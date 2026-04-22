using System.Collections;
using System.Text;
using Samples.Whisper;
using UnityEngine;
using UnityEngine.Networking;
using System;
using TMPro;

/// <summary>
/// Sends user questions to the retrieval endpoint and forwards the returned context to the AI manager.
/// </summary>
public class RAG : MonoBehaviour
{
    /// <summary>
    /// Payload sent to the RAG ask endpoint.
    /// </summary>
    [Serializable]
    private class AskRequest
    {
        public string question;
    }

    /// <summary>
    /// Supported response formats returned by the RAG service.
    /// </summary>
    [Serializable]
    private class AskResponse
    {
        public string answer;
        public string response;
        public string text;
    }

    [Header("UI References")]
    [SerializeField] private TMP_Text process_text;

    [Header("References")]
    public Whisper whisper;
    public AIManager aiManager;

    [Header("RAG Settings")]
    private const string askURL = "http://18.217.36.198:8000/ask"; // old server: http://18.222.26.106:8000/ask

    [Header("User Question")]
    public string userQuestion;
    public string answerFromRAG;
    public event Action<string> OnRAGResponseReady;

    /// <summary>
    /// Finds the Whisper and AIManager components used by the RAG workflow.
    /// </summary>
    public void Awake()
    {
        try
        {
            whisper = FindObjectOfType<Whisper>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Whisper script not found in RAG.cs: " + ex.Message);
        }

        try
        {
            aiManager = FindObjectOfType<AIManager>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AIManager script not found in RAG.cs: " + ex.Message);
        }
    }   

    /// <summary>
    /// Starts the coroutine that sends a question to the RAG service.
    /// </summary>
    /// <param name="question">The user's question to retrieve supporting context for.</param>
    public void AskQuestion(string question)
    {
        StartCoroutine(Ask(question));
    }

    /// <summary>
    /// Sends the question to the backend, parses the response, and stores the retrieved context.
    /// </summary>
    /// <param name="question">The question being sent to the RAG endpoint.</param>
    /// <returns>Coroutine used to wait for the network response.</returns>
    public IEnumerator Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            // Avoid sending empty requests to the backend.
            Debug.LogWarning("Ask called with an empty question.");
            yield break;
        }

        // Cache the current question so other systems can inspect the latest request.
        userQuestion = question;
        Debug.Log($"This is the user question (RAG.cs): {userQuestion}");

        // Wrap the question in the JSON format expected by the ask endpoint.
        var reqObj = new AskRequest { question = question };
        string json = JsonUtility.ToJson(reqObj);

        // Configure the POST request and attach the serialized question body.
        using var req = new UnityWebRequest(askURL, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "application/json");

        // Wait for the retrieval service to respond.
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // Clear the cached answer if the request fails so stale data is not reused.
            Debug.LogError($"Ask failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
            answerFromRAG = string.Empty;
            yield break;
        }

        // Fall back to raw text in case the backend does not return a JSON object.
        var responseText = req.downloadHandler?.text ?? string.Empty;
        string parsedAnswer = responseText;

        try
        {
            // Accept several common property names so small backend changes do not break parsing.
            var resp = JsonUtility.FromJson<AskResponse>(responseText);
            if (resp != null)
            {
                if (!string.IsNullOrWhiteSpace(resp.answer))
                {
                    parsedAnswer = resp.answer;
                }
                else if (!string.IsNullOrWhiteSpace(resp.response))
                {
                    parsedAnswer = resp.response;
                }
                else if (!string.IsNullOrWhiteSpace(resp.text))
                {
                    parsedAnswer = resp.text;
                }
            }
        }
        catch (Exception ex)
        {
            // Keep using the raw response if JSON parsing fails.
            Debug.LogWarning($"Could not parse RAG JSON response, using raw text: {ex.Message}");
        }

        // Store the final retrieval result locally before sharing it with listeners.
        answerFromRAG = parsedAnswer;
        Debug.Log("RAG Answer (RAG.cs): " + answerFromRAG);

        // Only forward non-empty RAG data to downstream systems.
        if (answerFromRAG != string.Empty)
        {
            // Trigger the event to notify that the RAG response is ready
            OnRAGResponseReady?.Invoke(answerFromRAG);

            // Send the RAG answer to the AIManager
            aiManager.RAGInfomration = answerFromRAG;

            Debug.Log("Sent RAG answer to AIManager");
            process_text.text = "RAG response received and sent to AI Manager.";
        }

       
    }
}
