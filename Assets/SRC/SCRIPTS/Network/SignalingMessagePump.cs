using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ensures NativeWebSocket DispatchMessageQueue runs every frame even if the owning MonoBehaviour is disabled.
/// Any SignalingClient can register its Tick() delegate so WebSocket callbacks keep flowing while menus disable XR objects.
/// </summary>
public sealed class SignalingMessagePump : MonoBehaviour
{
    private static SignalingMessagePump _instance;
    private readonly HashSet<Action> pumps = new HashSet<Action>();
    private readonly List<Action> invocationBuffer = new List<Action>();

    public static void Register(Action pump)
    {
        if (pump == null)
            return;

        EnsureInstance();
        if (_instance.pumps.Add(pump))
        {
            // Keep list capacity in sync to avoid allocations when pumping frequently.
            if (_instance.invocationBuffer.Capacity < _instance.pumps.Count)
            {
                _instance.invocationBuffer.Capacity = _instance.pumps.Count;
            }
        }
    }

    public static void Unregister(Action pump)
    {
        if (_instance == null || pump == null)
            return;

        _instance.pumps.Remove(pump);
    }

    private static void EnsureInstance()
    {
        if (_instance != null)
            return;

        var go = new GameObject("SignalingMessagePump");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SignalingMessagePump>();
    }

    private void Update()
    {
        if (pumps.Count == 0)
            return;

        invocationBuffer.Clear();
        invocationBuffer.AddRange(pumps);

        foreach (var pump in invocationBuffer)
        {
            try
            {
                pump?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SignalingMessagePump] Pump invocation failed: {ex.Message}");
            }
        }
    }
}
