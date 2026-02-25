using System.Collections;
using System.Text;
using Samples.Whisper;
using UnityEngine;
using UnityEngine.Networking;
using System;

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

    [Header("RAG Settings")]
    private const string askURL = "http://18.222.26.106:8000/ask";

    [Header("User Question")]
    public string userQuestion = "What does a flight director do?";
    public string answerFromRAG;
    public Whisper whisper;
    public event Action<string> OnRAGResponseReady;

    public void Awake()
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

    //Testing
    public void Start()
    {
        AskQuestion(userQuestion);
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
        Debug.Log($"This is the user question: {userQuestion}");

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

        answerFromRAG = parsedAnswer;
        Debug.Log("RAG Answer: " + answerFromRAG);
        OnRAGResponseReady?.Invoke(answerFromRAG);
    }
}
