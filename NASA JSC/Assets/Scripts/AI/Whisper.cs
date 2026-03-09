using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OpenAI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
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
        [Header("Mics and Audios")]
        [SerializeField] private int defaultMicIndex = 0;
        private readonly string fileName = "output.wav";
        // Length of each chunk (seconds) to transcribe while recording continues.
        private readonly int duration = 1;
        [SerializeField] private float speechRmsThreshold = 0.01f;
        [SerializeField] private float endSilenceSeconds = 0.2f;
        [SerializeField] private float maxUtteranceSeconds = 15f;
        [SerializeField] private float preSpeechSeconds = 0.5f;
        private AudioClip clip; // Current mic capture buffer.
        public bool isRecording; // Whether we should keep cycling chunks.
        private float time; // Timer for the current chunk.
        private string micName;

        [Header("Whisper Server")]
        [SerializeField] private string whisperUrl = "http://18.217.36.198:8000/transcribe";

        private bool isTranscribing; // Whether a transcription request is in flight.
        private readonly Queue<AudioClip> pendingClips = new Queue<AudioClip>(); // Queue chunks while a request is in flight.
        private readonly List<float> utteranceBuffer = new List<float>();
        private readonly List<float> preSpeechBuffer = new List<float>();

        [Header("Speech Detection State")]
        private bool inSpeech; // Whether we are currently in a speech segment.
        private float silenceTimer; // Timer for silence at end of speech segment.
        private int sampleRate; // Cached sample rate of the mic.
        private int channels; // Cached channel count of the mic.
        private bool isMuted = true; // Whether the microphone is muted.

        [Header("References and UI")]
        public GameObject UIMuteIcon; // Reference to the Mute Icon in the UI
        public AIManager aiManager; // Reference to AIManager Script
        public RAG rag; // Reference to RAG Script

        [Serializable]
        private class TranscriptionResponse
        {
            public string text;
            public string transcription;
            public string response;
        }

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

            try
            {
                rag = FindObjectOfType<RAG>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("RAG script not found: " + ex.Message);
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

        private IEnumerator WaitForRAGResponse(string transcribedText)
        {
            if (rag == null)
            {
                SendUserMessageToAIManager(transcribedText);
                yield break;
            }

            // Clear previous value so we wait for the new response.
            rag.answerFromRAG = string.Empty;
            rag.AskQuestion(transcribedText);

            const float timeoutSeconds = 15f;
            float elapsed = 0f;

            // Wait until the RAG response is ready
            while (string.IsNullOrEmpty(rag.answerFromRAG))
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeoutSeconds)
                {
                    Debug.LogWarning("Timed out waiting for RAG response. Continuing without RAG context.");
                    break;
                }
                yield return null; // Wait for the next frame
            }

            if (!string.IsNullOrWhiteSpace(rag.answerFromRAG) && aiManager != null)
            {
                aiManager.RAGInfomration = rag.answerFromRAG;
                Debug.Log($"Received RAG answer (whisper.cs): {aiManager.RAGInfomration}");
            }

            SendUserMessageToAIManager(transcribedText);
        }

        private void SendUserMessageToAIManager(string transcribedText)
        {
            if (aiManager == null || string.IsNullOrWhiteSpace(transcribedText))
            {
                return;
            }

            var msg = new ChatMessage
            {
                Role = "user",
                Content = transcribedText
            };

            // Pass into the AI Manager's speech list
            aiManager.AddMessage(msg);
        }

        // Sends a finished chunk to server-side Whisper without blocking recording.
        private IEnumerator TranscribeClipCoroutine(AudioClip clipToTranscribe)
        {
            if (clipToTranscribe == null || IsSilent(clipToTranscribe, speechRmsThreshold))
            {
                yield break; // Skip transcription on silence/empty clip.
            }

            isTranscribing = true;
            // Create a unique filename so chunk saves don't overwrite each other.
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var chunkFileName = $"{timestamp}_{fileName}";
            byte[] data = SaveWav.Save(chunkFileName, clipToTranscribe);

            var form = new WWWForm();
            form.AddBinaryData("file", data, "audio.wav", "audio/wav");
            form.AddField("model", "whisper-1");
            form.AddField("language", "en");

            using var req = UnityWebRequest.Post(whisperUrl, form);
            req.downloadHandler = new DownloadHandlerBuffer();

            Debug.Log($"Sending audio chunk to Whisper endpoint: {whisperUrl}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Whisper endpoint failed: {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                isTranscribing = false;
                if (pendingClips.Count > 0)
                {
                    var failedNext = pendingClips.Dequeue();
                    StartCoroutine(TranscribeClipCoroutine(failedNext));
                }
                yield break;
            }

            var responseText = req.downloadHandler?.text ?? string.Empty;
            var transcribedText = responseText;

            try
            {
                var parsed = JsonUtility.FromJson<TranscriptionResponse>(responseText);
                if (parsed != null)
                {
                    if (!string.IsNullOrWhiteSpace(parsed.text))
                    {
                        transcribedText = parsed.text;
                    }
                    else if (!string.IsNullOrWhiteSpace(parsed.transcription))
                    {
                        transcribedText = parsed.transcription;
                    }
                    else if (!string.IsNullOrWhiteSpace(parsed.response))
                    {
                        transcribedText = parsed.response;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not parse Whisper JSON response, using raw text: {ex.Message}");
            }

            Debug.Log($"Printing in Whisper Script: {transcribedText}");

            if (rag != null)
            {
                // Wait for RAG response before forwarding to AIManager.
                StartCoroutine(WaitForRAGResponse(transcribedText));
            }
            else
            {
                SendUserMessageToAIManager(transcribedText);
            }

            isTranscribing = false;
            if (pendingClips.Count > 0)
            {
                var next = pendingClips.Dequeue();
                StartCoroutine(TranscribeClipCoroutine(next));
            }
        }

        private void Update()
        {
            //Check for M key press to toggle mute/unmute
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                if (isMuted) //You are currently muted
                {
                    isMuted = false;
                    UIMuteIcon.SetActive(false); // Hide the mute icon
                }
                else //You are currently unmuted
                {
                    isMuted = true;
                    UIMuteIcon.SetActive(true); // Show the mute icon
                }
            }

            //If not muted, process as normal
            if (!isMuted)
            {
                // Handle chunk timing.
                if (!isRecording)
                {
                    return;
                }

                // Update timer and check if chunk is complete.
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

            if (!inSpeech)
            {
                if (isSilent)
                {
                    CapturePreSpeech(clipToProcess);
                    return;
                }

                // Speech just started: prepend a short pre-roll so first words are not clipped.
                inSpeech = true;
                silenceTimer = 0f;
                if (preSpeechBuffer.Count > 0)
                {
                    utteranceBuffer.AddRange(preSpeechBuffer);
                    preSpeechBuffer.Clear();
                }
                AppendSamples(clipToProcess);

                var currentSeconds = (float)utteranceBuffer.Count / (sampleRate * channels);
                if (currentSeconds >= maxUtteranceSeconds)
                {
                    FlushUtterance();
                    Debug.Log("Max utterance length reached, flushing to Whisper.");
                }
                return;
            }

            if (!isSilent)
            {
                silenceTimer = 0f;
                AppendSamples(clipToProcess);
                var currentSeconds = (float)utteranceBuffer.Count / (sampleRate * channels);
                if (currentSeconds >= maxUtteranceSeconds)
                {
                    FlushUtterance();
                    Debug.Log("Max utterance length reached, flushing to Whisper.");
                }
                return;
            }

            silenceTimer += duration;

            // If we've reached the end of speech, flush the utterance.
            if (silenceTimer >= endSilenceSeconds)
            {
                FlushUtterance();
                Debug.Log("End of speech detected, flushing to Whisper.");
            }
        }

        // Keep a rolling pre-speech buffer so we can prepend a short lead-in at speech start.
        private void CapturePreSpeech(AudioClip clipToCapture)
        {
            var samples = new float[clipToCapture.samples * clipToCapture.channels];
            clipToCapture.GetData(samples, 0);
            preSpeechBuffer.AddRange(samples);

            var maxPreSpeechSamples = Mathf.Max(1, (int)(preSpeechSeconds * sampleRate * channels));
            if (preSpeechBuffer.Count > maxPreSpeechSamples)
            {
                preSpeechBuffer.RemoveRange(0, preSpeechBuffer.Count - maxPreSpeechSamples);
            }
        }

        // Append samples from the given clip to the utterance buffer.
        private void AppendSamples(AudioClip clipToAppend)
        {
            var samples = new float[clipToAppend.samples * clipToAppend.channels];
            clipToAppend.GetData(samples, 0);
            utteranceBuffer.AddRange(samples);
            Debug.Log("Samples appended to utterance buffer.");
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
                pendingClips.Enqueue(utteranceClip);
                Debug.Log("Reading in transcription in progress");
                return;
            }

            StartCoroutine(TranscribeClipCoroutine(utteranceClip));
            Debug.Log("Transcription has been sent to Whisper.");
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