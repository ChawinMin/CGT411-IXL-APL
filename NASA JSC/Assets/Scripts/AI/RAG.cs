using System.Collections;
using System.Text;
using Samples.Whisper;
using UnityEngine;
using UnityEngine.Networking;
using System;
using TMPro;

public class RAG : MonoBehaviour
{
    [Serializable]
    private class AskRequest
    {
        public string question;
    }

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

    public void AskQuestion(string question)
    {
        StartCoroutine(Ask(question));
    }

    public IEnumerator Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            Debug.LogWarning("Ask called with an empty question.");
            yield break;
        }

        userQuestion = question;
        Debug.Log($"This is the user question (RAG.cs): {userQuestion}");

        var reqObj = new AskRequest { question = question };
        string json = JsonUtility.ToJson(reqObj);

        using var req = new UnityWebRequest(askURL, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Ask failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
            answerFromRAG = string.Empty;
            yield break;
        }

        var responseText = req.downloadHandler?.text ?? string.Empty;
        string parsedAnswer = responseText;

        try
        {
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
            Debug.LogWarning($"Could not parse RAG JSON response, using raw text: {ex.Message}");
        }

        // Gets the final answer from RAG and sends it to the AIManager
        answerFromRAG = parsedAnswer;
        Debug.Log("RAG Answer (RAG.cs): " + answerFromRAG);

        // Confirm that information is sent to AIManager
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
