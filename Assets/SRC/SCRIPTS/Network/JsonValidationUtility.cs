using System;
using UnityEngine;

/// <summary>
/// Lightweight helper around JsonUtility that reports parse errors without throwing to callers.
/// </summary>
public static class JsonValidationUtility
{
    public static bool TryParse<T>(string json, out T result, out string errorMessage)
    {
        result = default;
        errorMessage = null;

        if (string.IsNullOrEmpty(json))
        {
            errorMessage = "JSON payload is empty.";
            return false;
        }

        try
        {
            result = JsonUtility.FromJson<T>(json);
            if (result == null)
            {
                errorMessage = "JSON payload produced a null object.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }
}
