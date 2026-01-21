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

        /*
        RMS means Root Mean Square. It is a statistical measure used to quantify the magnitude of a varying quantity. 
        In audio processing, RMS is commonly used to measure the average power or 
        loudness of an audio signal

        Utterance is a single spoken word, statement, or vocal sound made by a person. Think of a sentence.
        */
        [SerializeField] private int defaultMicIndex = 0;

        private readonly string fileName = "output.wav";
        // Length of each chunk (seconds) to transcribe while recording continues.
        private readonly int duration = 1;
        [SerializeField] private float speechRmsThreshold = 0.01f;
        [SerializeField] private float endSilenceSeconds = 0.2f;
        [SerializeField] private float maxUtteranceSeconds = 4f;

        private AudioClip clip; // Current mic capture buffer.
        public bool isRecording; // Whether we should keep cycling chunks.
        private float time; // Timer for the current chunk.
        private string micName;
        public OpenAIApi openai = new OpenAIApi(); //API Key to OpenAI
        private bool isTranscribing; // Whether a transcription request is in flight.
        private AudioClip pendingClip; // Keep the most recent chunk while a request is in flight.
        private readonly List<float> utteranceBuffer = new List<float>();
        private bool inSpeech; // Whether we are currently in a speech segment.
        private float silenceTimer; // Timer for silence at end of speech segment.
        private int sampleRate; // Cached sample rate of the mic.
        private int channels; // Cached channel count of the mic.

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
            if (clipToTranscribe == null || IsSilent(clipToTranscribe, speechRmsThreshold))
            {
                return; // Skip transcription on silence/empty clip.
            }

            isTranscribing = true;
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
                aiManager.AddMessage(msg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"There is an error in Whisper: {ex.Message}");
            }
            finally
            {
                isTranscribing = false;
                if (pendingClip != null)
                {
                    var next = pendingClip;
                    pendingClip = null;
                    TranscribeClip(next);
                }
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
            ProcessChunk(clipToTranscribe);
        }

        // Process a finished chunk for speech segments.
        private void ProcessChunk(AudioClip clipToProcess)
        {
            if (clipToProcess == null)
            {
                return;
            }

            if (sampleRate == 0)
            {
                sampleRate = clipToProcess.frequency;
                channels = clipToProcess.channels;
            }

            var rms = GetRms(clipToProcess);
            var isSilent = rms < speechRmsThreshold;

            if (!isSilent)
            {
                inSpeech = true;
                silenceTimer = 0f;
                AppendSamples(clipToProcess);

                var currentSeconds = (float)utteranceBuffer.Count / (sampleRate * channels);

                // If we've exceeded max utterance length, flush it now.
                if (currentSeconds >= maxUtteranceSeconds)
                {
                    FlushUtterance();
                }
                return;
            }

            if (!inSpeech)
            {
                return;
            }

            silenceTimer += duration;
            // If we've reached the end of speech, flush the utterance.
            if (silenceTimer >= endSilenceSeconds)
            {
                FlushUtterance();
            }
        }

        // Append samples from the given clip to the utterance buffer.
        private void AppendSamples(AudioClip clipToAppend)
        {
            var samples = new float[clipToAppend.samples * clipToAppend.channels];
            clipToAppend.GetData(samples, 0);
            utteranceBuffer.AddRange(samples);
        }

        // Flush the current utterance buffer (package into a single audio clip) 
        // and send it for transcription.
        private void FlushUtterance()
        {
            if (utteranceBuffer.Count == 0)
            {
                inSpeech = false;
                silenceTimer = 0f;
                return;
            }

            var totalSamples = utteranceBuffer.Count / channels;
            var utteranceClip = AudioClip.Create("utterance", totalSamples, channels, sampleRate, false);
            utteranceClip.SetData(utteranceBuffer.ToArray(), 0);

            utteranceBuffer.Clear();
            inSpeech = false;
            silenceTimer = 0f;

            if (isTranscribing)
            {
                pendingClip = utteranceClip;
                return;
            }

            TranscribeClip(utteranceClip);
        }

        // Simple RMS check to skip silent chunks.
        private static bool IsSilent(AudioClip clipToCheck, float rmsThreshold)
        {
            return GetRms(clipToCheck) < rmsThreshold;
        }

        private static float GetRms(AudioClip clipToCheck)
        {
            //Compares the audio clip's RMS to a threshold value. And check if it is below the threshold or 
            //higher. To determine if the clip is silent or not.
            var samples = new float[clipToCheck.samples * clipToCheck.channels];
            clipToCheck.GetData(samples, 0);

            double sum = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                sum += s * s;
            }

            return Mathf.Sqrt((float)(sum / samples.Length));
        }
    }
}
