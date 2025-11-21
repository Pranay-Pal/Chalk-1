using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class LineDrawProperties : MonoBehaviour
{
    [SerializeField] private UISCRIPTBASE uiController;

    // UI is removed, but keeping this script for potential future use
    // or for handling leave session via other means (e.g., back button)
    
    public void LeaveSession()
    {
        if (uiController != null)
        {
            uiController.LeaveCurrentSession();
        }
        else
        {
            Debug.LogWarning("[LineDrawProperties] UI controller reference missing; cannot leave session.");
        }
    }
}