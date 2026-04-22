using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OpenAI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

namespace Samples.Whisper
{
    /// <summary>
    /// Captures microphone input, detects speech segments, sends them to Whisper, and forwards the transcript into the AI flow.
    /// </summary>
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

        [Header("UI References")]
        [SerializeField] private TMP_Text process_text; // Reference to the UI Text element for displaying processing status.

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

        /// <summary>
        /// Supported response formats returned by the transcription service.
        /// </summary>
        [Serializable]
        private class TranscriptionResponse
        {
            public string text;
            public string transcription;
            public string response;
        }

        /// <summary>
        /// Finds the AIManager and RAG components needed after transcription completes.
        /// </summary>
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

        /// <summary>
        /// Selects the configured microphone and starts recording when the scene begins.
        /// </summary>
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

        /// <summary>
        /// Starts a new microphone capture chunk.
        /// </summary>
        private void StartRecording()
        {
            isRecording = true;
            time = 0f;

#if !UNITY_WEBGL
            // Record a short chunk so speech detection can evaluate audio continuously.
            clip = Microphone.Start(micName, false, duration, 44100);
#endif
        }

        /// <summary>
        /// Waits for RAG to finish retrieving context before the user message is sent to AIManager.
        /// </summary>
        /// <param name="transcribedText">The transcript that should be enriched with RAG context.</param>
        /// <returns>Coroutine used to wait for the retrieval result.</returns>
        private IEnumerator WaitForRAGResponse(string transcribedText)
        {
            if (rag == null)
            {
                // If RAG is unavailable, send the transcript directly to the AI pipeline.
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
                    process_text.text = "RAG response timed out. Continuing without context.";
                    break;
                }
                yield return null; // Wait for the next frame
            }

            if (!string.IsNullOrWhiteSpace(rag.answerFromRAG) && aiManager != null)
            {
                aiManager.RAGInfomration = rag.answerFromRAG;
                Debug.Log($"Received RAG answer (whisper.cs): {aiManager.RAGInfomration}");
                process_text.text = "RAG response received. Sending to AI Manager.";
            }

            // Forward the user's spoken message after RAG handling is complete.
            SendUserMessageToAIManager(transcribedText);
        }

        /// <summary>
        /// Wraps transcribed text in a chat message and gives it to the AI manager.
        /// </summary>
        /// <param name="transcribedText">The spoken text returned by Whisper.</param>
        private void SendUserMessageToAIManager(string transcribedText)
        {
            if (aiManager == null || string.IsNullOrWhiteSpace(transcribedText))
            {
                // Do nothing if there is nowhere to send the transcript.
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

        /// <summary>
        /// Sends a finished chunk to server-side Whisper without blocking recording.
        /// </summary>
        /// <param name="clipToTranscribe">The completed audio clip containing the detected utterance.</param>
        /// <returns>Coroutine used to wait for the transcription request.</returns>
        private IEnumerator TranscribeClipCoroutine(AudioClip clipToTranscribe)
        {
            if (clipToTranscribe == null || IsSilent(clipToTranscribe, speechRmsThreshold))
            {
                yield break; // Skip transcription on silence/empty clip.
            }

            // Prevent overlapping uploads while one transcription request is already running.
            isTranscribing = true;
            // Create a unique filename so chunk saves don't overwrite each other.
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var chunkFileName = $"{timestamp}_{fileName}";
            byte[] data = SaveWav.Save(chunkFileName, clipToTranscribe);

            // Build the multipart form expected by the Whisper endpoint.
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
                    // Continue draining queued utterances after a failed request.
                    var failedNext = pendingClips.Dequeue();
                    StartCoroutine(TranscribeClipCoroutine(failedNext));
                }
                yield break;
            }

            // Use the raw response text first, then try to extract the transcript from JSON.
            var responseText = req.downloadHandler?.text ?? string.Empty;
            var transcribedText = responseText;

            try
            {
                // Accept several field names so backend response changes remain compatible.
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
                // Keep the raw response if the JSON structure is unexpected.
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
                // Send the user question to the AIManager
                SendUserMessageToAIManager(transcribedText);
            }

            isTranscribing = false;
            if (pendingClips.Count > 0)
            {
                // Start the next queued utterance once this transcription finishes.
                var next = pendingClips.Dequeue();
                StartCoroutine(TranscribeClipCoroutine(next));
            }
        }

        /// <summary>
        /// Handles mute toggling and processes microphone chunks while recording is active.
        /// </summary>
        private void Update()
        {
            //Check for M key press or the 'A' button on the Meta Quest controller to toggle mute/unmute
            if ((Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) || OVRInput.GetDown(OVRInput.Button.One))
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

        /// <summary>
        /// Process a finished chunk for speech segments.
        /// </summary>
        /// <param name="clipToProcess">The most recently recorded microphone chunk.</param>
        private void ProcessChunk(AudioClip clipToProcess)
        {
            if (clipToProcess == null)
            {
                return;
            }

            if (sampleRate == 0)
            {
                // Cache audio format values the first time a chunk is processed.
                sampleRate = clipToProcess.frequency;
                channels = clipToProcess.channels;
            }

            var rms = GetRms(clipToProcess);
            var isSilent = rms < speechRmsThreshold;

            if (!inSpeech)
            {
                if (isSilent)
                {
                    // Save a short lead-in while idle so the start of speech is not clipped.
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

                // if the buffer gets longer than the maximum listening window send to whisper
                var currentSeconds = (float)utteranceBuffer.Count / (sampleRate * channels);
                if (currentSeconds >= maxUtteranceSeconds)
                {
                    FlushUtterance();
                    Debug.Log("Max utterance length reached, flushing to Whisper.");
                }
                return;
            }

            // If the maximum silence has been reached send the current speech to whispter
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
                process_text.text = "Processing...";
            }
        }

        /// <summary>
        /// Keep a rolling pre-speech buffer so we can prepend a short lead-in at speech start.
        /// </summary>
        /// <param name="clipToCapture">Audio chunk captured before speech begins.</param>
        private void CapturePreSpeech(AudioClip clipToCapture)
        {
            var samples = new float[clipToCapture.samples * clipToCapture.channels];
            clipToCapture.GetData(samples, 0);
            preSpeechBuffer.AddRange(samples);

            // Trim the buffer so it only keeps the desired amount of pre-roll audio.
            var maxPreSpeechSamples = Mathf.Max(1, (int)(preSpeechSeconds * sampleRate * channels));
            if (preSpeechBuffer.Count > maxPreSpeechSamples)
            {
                preSpeechBuffer.RemoveRange(0, preSpeechBuffer.Count - maxPreSpeechSamples);
            }
        }

        /// <summary>
        /// Append samples from the given clip to the utterance buffer.
        /// </summary>
        /// <param name="clipToAppend">Audio chunk that belongs to the current spoken utterance.</param>
        private void AppendSamples(AudioClip clipToAppend)
        {
            var samples = new float[clipToAppend.samples * clipToAppend.channels];
            clipToAppend.GetData(samples, 0);
            utteranceBuffer.AddRange(samples);
            Debug.Log("Samples appended to utterance buffer.");
            process_text.text = "Listening...";
        }

        /// <summary>
        /// Flush the current utterance buffer (package into a single audio clip) 
        /// and send it for transcription.
        /// </summary>
        private void FlushUtterance()
        {
            if (utteranceBuffer.Count == 0)
            {
                // Reset detection state even if there is nothing to send.
                inSpeech = false;
                silenceTimer = 0f;
                return;
            }

            // Build a single clip from the accumulated utterance samples.
            var totalSamples = utteranceBuffer.Count / channels;
            var utteranceClip = AudioClip.Create("utterance", totalSamples, channels, sampleRate, false);
            utteranceClip.SetData(utteranceBuffer.ToArray(), 0);

            utteranceBuffer.Clear();
            inSpeech = false;
            silenceTimer = 0f;

            if (isTranscribing)
            {
                // Queue the utterance so recording can continue while the current upload finishes.
                pendingClips.Enqueue(utteranceClip);
                Debug.Log("Reading in transcription in progress");
                return;
            }

            StartCoroutine(TranscribeClipCoroutine(utteranceClip));
            Debug.Log("Transcription has been sent to Whisper.");
        }

        /// <summary>
        /// Simple RMS check to skip silent chunks.
        /// </summary>
        /// <param name="clipToCheck">Audio clip to evaluate.</param>
        /// <param name="rmsThreshold">Minimum RMS value required to treat the clip as non-silent.</param>
        /// <returns><c>true</c> if the clip is below the silence threshold; otherwise, <c>false</c>.</returns>
        private static bool IsSilent(AudioClip clipToCheck, float rmsThreshold)
        {
            return GetRms(clipToCheck) < rmsThreshold;
        }

        /// <summary>
        /// Calculates the RMS loudness value of an audio clip.
        /// </summary>
        /// <param name="clipToCheck">Audio clip whose sample power should be measured.</param>
        /// <returns>The RMS value of the clip's samples.</returns>
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
