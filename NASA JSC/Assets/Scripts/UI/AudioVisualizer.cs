using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioVisualizer : MonoBehaviour
{
    [Header("References")]
    public AudioSource audioSource;
    public Transform[] bars;
 
    [Header("Settings")]
    public FrequencyFocusWindow frequencyFocusWindow;
    public float amplification = 1.0f;
    public float baseHeight = 0.0f;
    public FFTWindow fftWindow;
    public bool useDecibels;
 
    [Header("State")]
    public float[] spectrumData;
    
    /// <summary>
    /// Set the spectrum data that can be used
    /// </summary>
    void Awake()
    {
        // Must be a power of 2 number, between 64 and 8192
        spectrumData = new float[4096];
    }
    
    /// <summary>
    /// Update the talking bars depending on the amplitude of the audio clips -> show speech
    /// </summary>
    void Update()
    {
        audioSource.GetSpectrumData(spectrumData, 0, fftWindow);
        var blockSize = spectrumData.Length / bars.Length / (int)frequencyFocusWindow;
        for (int i = 0; i < bars.Length; ++i)
        {
            float sum = 0;
            for (int j = 0; j < blockSize; j++)
            {
                sum += spectrumData[i * blockSize + j];
            }
            sum /= blockSize;
            float amplitude = Mathf.Clamp(sum, 1e-7f, 1f);
            var scale = bars[i].localScale;
            if (useDecibels)
            {
                scale.y = -Mathf.Log10(amplitude) * amplification / 200;
            }
            else
            {
                scale.y = sum * amplification + baseHeight;
            }
            bars[i].localScale = scale;
        }
    }
}
 
/// <summary>
/// The different types of frequencies that can be used
/// </summary>
public enum FrequencyFocusWindow
{
    Entire = 1,
    FirstHalf = 2,
    FirstQuarter = 4,
    FirstEight = 8,
    FirstSixteenth = 16
}
