using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using UnityEngine.UI;
using UnityEngine.Networking;

public class ElevenLabsManager : MonoBehaviour
{
    [Serializable]
    private class TtsRequest
    {
        public string text;
    }

    [Header("References")]
    private AIManager aiManager; //Reference to AIManager Script
    private AudioVisualizer audioVisualizer; //Reference to AudioVisualizer Script

    [Header("Eleven Labs States")]
    [SerializeField] private string ttsUrl = "http://18.217.36.198:8000/speak";
    private AudioSource audioSource; //AudioSource to play TTS audio
    private bool isAITalking; //Flag to indicate if AI is currently talking
    private readonly Queue<string> pendingSpeech = new Queue<string>(); //Queue speech while current audio is playing


    private void Awake()
    {
        //Reference to AIManager Script
        try
        {
           aiManager = FindObjectOfType<AIManager>();//Reference to AIManager Script
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AIManager script not found: " + ex.Message);
        }

        try
        {
            audioVisualizer = FindObjectOfType<AudioVisualizer>();//Reference to AudioVisualizer Script
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AudioVisualizer script not found: " + ex.Message);
        }

        //Get AudioSource component
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            Debug.Log("AudioSource component added.");
        }
        audioSource.loop = false;
        
    }

    private void OnEnable()
    {
        if (aiManager != null)
        {
            aiManager.OnAIResponseReady += QueueOrSpeak;
        }
    }

    private void OnDisable()
    {
        if (aiManager != null)
        {
            aiManager.OnAIResponseReady -= QueueOrSpeak;
        }
    }

    private void QueueOrSpeak(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return;
        }

        if (isAITalking || audioSource.isPlaying)
        {
            pendingSpeech.Enqueue(responseText);
            return;
        }

        Debug.Log("Starting to speak AI response.");
        StartCoroutine(TalkCoroutine(responseText));
    }

    private IEnumerator TalkCoroutine(string responseText)
    {
        if (isAITalking) yield break; //Prevent overlapping TTS requests
        if (string.IsNullOrWhiteSpace(responseText)) yield break;

        isAITalking = true;

        var payload = new TtsRequest { text = responseText };
        var json = JsonUtility.ToJson(payload);
        var bodyRaw = Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest(ttsUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerAudioClip(ttsUrl, AudioType.MPEG);
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "audio/mpeg");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            var errorBody = string.Empty;
            var errorBytes = req.downloadHandler?.data;
            if (errorBytes != null && errorBytes.Length > 0)
            {
                errorBody = Encoding.UTF8.GetString(errorBytes);
            }
            Debug.LogError($"Speak endpoint failed: {req.responseCode} {req.error}\n{errorBody}");
            isAITalking = false;
            if (pendingSpeech.Count > 0)
            {
                var failedNext = pendingSpeech.Dequeue();
                StartCoroutine(TalkCoroutine(failedNext));
            }
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(req);
        if (clip == null)
        {
            Debug.LogWarning("Speak endpoint returned no audio clip.");
            isAITalking = false;
            yield break;
        }

        audioSource.PlayOneShot(clip);
        Debug.Log("Speak audio played.");
        if (audioVisualizer != null)
        {
            audioVisualizer.audioSource = audioSource; //Set the AudioSource reference in AudioVisualizer to sync visualizer with TTS audio
        }

        // Keep talking state true until playback completes to preserve queueing behavior.
        yield return new WaitWhile(() => audioSource != null && audioSource.isPlaying);

        isAITalking = false; //Reset the talking flag
        if (pendingSpeech.Count > 0)
        {
            var nextSpeech = pendingSpeech.Dequeue();
            Debug.Log("Playing next queued speech.");
            StartCoroutine(TalkCoroutine(nextSpeech));
        }
    }
         
}
