using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class ElevenLabsManager : MonoBehaviour
{
    private AIManager aiManager; //Reference to AIManager Script

    private void Awake()
    {
        try
        {
           aiManager = FindObjectOfType<AIManager>();//Reference to AIManager Script
        }
        catch (Exception ex)
        {
            Debug.LogWarning("AIManager script not found: " + ex.Message);
        }
        
    }
}
