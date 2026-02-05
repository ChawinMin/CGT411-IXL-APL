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

public class ElevenLabsManager : MonoBehaviour
{
    

    private AIManager aiManager; //Reference to AIManager Script

    private AudioSource audioSource; //AudioSource to play TTS audio

    private bool isAITalking; //Flag to indicate if AI is currently talking

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
           var api = getElevenLabsAPIKey();
           Debug.Log("ElevenLabs API Key loaded successfully.");

        }
        catch (Exception ex)
        {
            Debug.LogWarning("ElevenLabs API Key not found: " + ex.Message);
        }

        //Get AudioSource component
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            Debug.Log("AudioSource component added.");
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

    public async void talk()
    {
        if (isAITalking) return; //Prevent overlapping TTS requests
        isAITalking = true;

        //Pass the AI response to ElevenLabs TTS and play the audio
        try
        {
            //Get the ElevenLabs API
            var api = getElevenLabsAPIKey();

            //Generate TTS audio from the latest AI response
            var voice = (await api.VoicesEndpoint.GetAllVoicesAsync()).FirstOrDefault(); 

            //Use the latest AI response
            var request = new TextToSpeechRequest(voice, aiManager.aiResponses[^1], outputFormat: OutputFormat.PCM_24000); 
            
            //Get the generated audio clip
            var voiceClip = await api.TextToSpeechEndpoint.TextToSpeechAsync(request);

            //Play the generated audio clip
            audioSource.PlayOneShot(voiceClip.AudioClip);
            Debug.Log("TTS audio played.");
        }
        finally
        {
            isAITalking = false; //Reset the talking flag
        }
    }

    public void Update()
    {
        //Check if audio is not playing, then play TTS audio
        if (!audioSource.isPlaying && aiManager.aiResponses.Count > 0 && audioSource.clip == null)
        {
            Debug.Log("Playing TTS audio...");
            talk();
            audioSource.clip = null; //Reset clip to avoid replaying
        }
    }
         
}
