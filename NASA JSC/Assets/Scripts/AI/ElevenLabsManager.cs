using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// Sends AI text responses to the speech endpoint and plays the returned audio in sequence.
/// </summary>
public class ElevenLabsManager : MonoBehaviour
{
    /// <summary>
    /// Payload sent to the text-to-speech endpoint.
    /// </summary>
    [Serializable]
    private class TtsRequest
    {
        public string text;
    }

    [Header("References")]
    private AIManager aiManager; //Reference to AIManager Script
    private AudioVisualizer audioVisualizer; //Reference to AudioVisualizer Script

    [Header("UI References")]
    [SerializeField] private TMP_Text process_text; //Reference to process text UI element

    [Header("Eleven Labs States")]
    [SerializeField] private string ttsUrl = "http://18.217.36.198:8000/speak";
    private AudioSource audioSource; //AudioSource to play TTS audio
    private bool isAITalking; //Flag to indicate if AI is currently talking
    private readonly Queue<string> pendingSpeech = new Queue<string>(); //Queue speech while current audio is playing

    /// <summary>
    /// Finds required scene references and ensures an AudioSource is available for playback.
    /// </summary>
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

    /// <summary>
    /// Subscribes to AI responses when this component becomes active.
    /// </summary>
    private void OnEnable()
    {
        if (aiManager != null)
        {
            aiManager.OnAIResponseReady += QueueOrSpeak;
        }
    }

    /// <summary>
    /// Unsubscribes from AI responses when this component is disabled.
    /// </summary>
    private void OnDisable()
    {
        if (aiManager != null)
        {
            aiManager.OnAIResponseReady -= QueueOrSpeak;
        }
    }

    /// <summary>
    /// Starts speech immediately when idle or queues it if audio is already playing.
    /// </summary>
    /// <param name="responseText">AI-generated text to convert into speech.</param>
    private void QueueOrSpeak(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            // Ignore empty responses so unnecessary TTS calls are not made.
            return;
        }

        if (isAITalking || audioSource.isPlaying)
        {
            // Preserve response order by queueing new lines until current playback finishes.
            pendingSpeech.Enqueue(responseText);
            return;
        }

        Debug.Log("Starting to speak AI response.");
        process_text.text = "Speaking AI response...";
        StartCoroutine(TalkCoroutine(responseText));
    }

    /// <summary>
    /// Sends text to the speech service, plays the returned clip, and then advances the queue.
    /// </summary>
    /// <param name="responseText">Text that should be spoken aloud.</param>
    /// <returns>Coroutine used to wait for the request and audio playback lifecycle.</returns>
    private IEnumerator TalkCoroutine(string responseText)
    {
        if (isAITalking) yield break; //Prevent overlapping TTS requests
        if (string.IsNullOrWhiteSpace(responseText)) yield break;

        // Mark the system as busy before the network request starts.
        isAITalking = true;

        // Wrap the text in the request format expected by the backend.
        var payload = new TtsRequest { text = responseText };
        var json = JsonUtility.ToJson(payload);
        var bodyRaw = Encoding.UTF8.GetBytes(json);

        // Send the text payload and ask the server to return MPEG audio data.
        using var req = new UnityWebRequest(ttsUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerAudioClip(ttsUrl, AudioType.MPEG);
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "audio/mpeg");

        // Wait for the text-to-speech service to finish generating audio.
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // Try to surface any backend error details returned in the response body.
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
                // Continue with queued speech even if one request fails.
                var failedNext = pendingSpeech.Dequeue();
                StartCoroutine(TalkCoroutine(failedNext));
            }
            yield break;
        }

        // Convert the downloaded audio bytes into a Unity AudioClip.
        var clip = DownloadHandlerAudioClip.GetContent(req);
        if (clip == null)
        {
            Debug.LogWarning("Speak endpoint returned no audio clip.");
            isAITalking = false;
            yield break;
        }

        // Play the synthesized voice line locally.
        audioSource.PlayOneShot(clip);
        process_text.text = "Audio Finished";
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
            // Start the next queued line once the current clip has finished.
            var nextSpeech = pendingSpeech.Dequeue();
            Debug.Log("Playing next queued speech.");
            StartCoroutine(TalkCoroutine(nextSpeech));
        }
    }
         
}
