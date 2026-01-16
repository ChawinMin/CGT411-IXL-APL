using OpenAI;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Samples.Whisper
{
    public class Whisper : MonoBehaviour
    {

        [SerializeField] private int defaultMicIndex = 0;

        private readonly string fileName = "output.wav";
        // Length of each chunk (seconds) to transcribe while recording continues.
        private readonly int duration = 1;

        private AudioClip clip; // Current mic capture buffer.
        private bool isRecording; // Whether we should keep cycling chunks.
        private float time; // Timer for the current chunk.
        private string micName;
        public OpenAIApi openai = new OpenAIApi(); //API Key to OpenAI

        public AIManager aiManager; //Reference to AIManager Script

        private void Awake()
        {
            try
            {
                aiManager = FindObjectOfType<AIManager>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("AIManager script not found: " + ex.Message);
            }
            
        }

        private void Start()
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogWarning("Microphone not supported on WebGL.");
            return;
            #else

            var devices = Microphone.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("No microphone devices found.");
                return;
            }

            var index = Mathf.Clamp(defaultMicIndex, 0, devices.Length - 1);
            micName = devices[index];
            Debug.Log($"Using mic: {micName} (index {index})");

            #endif
            StartRecording(); // Begin recording immediately on start.
        }

        // Starts a new mic recording chunk.
        private void StartRecording()
        {
            isRecording = true;
            time = 0f;

            #if !UNITY_WEBGL
            clip = Microphone.Start(micName, false, duration, 44100);
            #endif
        }

        // Sends a finished chunk to Whisper without blocking recording.
        private async void TranscribeClip(AudioClip clipToTranscribe)
        {
            // Create a unique filename so chunk saves don't overwrite each other.
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var chunkFileName = $"{timestamp}_{fileName}";
            byte[] data = SaveWav.Save(chunkFileName, clipToTranscribe);

            var req = new CreateAudioTranscriptionsRequest
            {
                FileData = new FileData() { Data = data, Name = "audio.wav" },
                Model = "whisper-1",
                Language = "en"
            };

            try
            {
                var res = await openai.CreateAudioTranscription(req);
                //Debug.Log($"Printing in Whisper Script: {res.Text}");

                var msg = new ChatMessage()
                {
                    Role = "user",
                    Content = res.Text
                };
                //Pass into the AI Manager's speech list
                aiManager.speechList.Add(msg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"There is an error in Whisper: {ex.Message}");
            }
        }

        private void Update()
        {
            if (!isRecording)
            {
                return;
            }

            time += Time.deltaTime;
            if (time < duration)
            {
                return;
            }

            // Stop the mic to finalize the chunk, then immediately restart.
            #if !UNITY_WEBGL
            Microphone.End(micName);
            #endif

            // Grab the finished clip, restart recording, and transcribe the old clip.
            var clipToTranscribe = clip;
            StartRecording();
            TranscribeClip(clipToTranscribe);
        }
    }
}