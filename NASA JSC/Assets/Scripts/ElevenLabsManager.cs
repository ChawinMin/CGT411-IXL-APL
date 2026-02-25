using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using ElevenLabs;
using System.IO;
using UnityEditor;
using UnityEngine.UI;
using ElevenLabs.TextToSpeech;
using System.Linq;
using Unity.VisualScripting;
using ElevenLabs.Voices;
using ElevenLabs.Models;

public class ElevenLabsManager : MonoBehaviour
{

    [Header("References")]
    private AIManager aiManager; //Reference to AIManager Script
    private AudioVisualizer audioVisualizer; //Reference to AudioVisualizer Script

    [Header("Eleven Labs States")]
    private AudioSource audioSource; //AudioSource to play TTS audio
    private bool isAITalking; //Flag to indicate if AI is currently talking
    private ElevenLabsClient apiClient; //Cached ElevenLabs client to avoid recreating on every line
    private Voice cachedVoice; //Cached voice to avoid calling GetAllVoices each request
    private readonly Queue<string> pendingSpeech = new Queue<string>(); //Queue speech while current audio is playing
    [System.Serializable] //Wrapper class for deserializing auth.json and loading ElevenLabs API Key

    private class AuthWrapper
    {
        public string ELEVEN_LABS_API_KEY;
    }


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

        //Load ElevenLabs API Key
        try
        {
           apiClient = getElevenLabsAPIKey();
           Debug.Log("ElevenLabs API Key loaded successfully.");

        }
        catch (Exception ex)
        {
            Debug.LogWarning("ElevenLabs API Key not found: " + ex.Message);
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

    private ElevenLabsClient getElevenLabsAPIKey()
    {
         //Load auth.json file
        var json = File.ReadAllText("C:/Users/cim09/.openai/auth.json");

        //Deserialize auth.json to get ElevenLabs API Key
        var authFile = JsonUtility.FromJson<AuthWrapper>(json);

        //Load Elevenlab Api Key
        var auth = new ElevenLabsAuthentication(authFile.ELEVEN_LABS_API_KEY);
        var api = new ElevenLabsClient(auth);
        
        return api;
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

        talk(responseText);
    }

    public async void talk(string responseText)
    {
        if (isAITalking) return; //Prevent overlapping TTS requests
        if (string.IsNullOrWhiteSpace(responseText) || apiClient == null) return;

        isAITalking = true;

        //Pass the AI response to ElevenLabs TTS and play the audio
        try
        {
            //Resolve voice once and reuse for subsequent TTS requests.
            if (cachedVoice == null)
            {
                cachedVoice = (await apiClient.VoicesEndpoint.GetAllVoicesAsync()).FirstOrDefault();
            }
            if (cachedVoice == null)
            {
                Debug.LogWarning("No ElevenLabs voice available.");
                return;
            }

            //Flash model is optimized for lower latency.
            var request = new TextToSpeechRequest(cachedVoice, responseText, model: Model.FlashV2_5, outputFormat: OutputFormat.PCM_24000); 
            
            //Get the generated audio clip
            var voiceClip = await apiClient.TextToSpeechEndpoint.TextToSpeechAsync(request);

            try
            {
                //Play the generated audio clip
                audioSource.PlayOneShot(voiceClip.AudioClip);
                audioVisualizer.audioSource = audioSource; //Set the AudioSource reference in AudioVisualizer to sync visualizer with TTS audio
                Debug.Log("TTS audio played.");
            }
            finally
            {
                //GeneratedClip owns NativeArray allocations; dispose to prevent leak warnings.
                voiceClip?.Dispose();
            }
        }
        finally
        {
            isAITalking = false; //Reset the talking flag
            if (pendingSpeech.Count > 0)
            {
                var nextSpeech = pendingSpeech.Dequeue();
                Debug.Log("Playing next queued speech.");
                talk(nextSpeech);
            }
        }
    }
         
}
