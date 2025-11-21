using System;
using UnityEngine;

[Serializable]
public enum DrawingEventType
{
    Start,
    Point,
    End,
    Undo,
    Clear
}

[Serializable]
public class DrawingEvent
{
    public string type; // "start", "point", "end", "undo", "clear"
    public float x;
    public float y;
    public float z;
    public float r;
    public float g;
    public float b;
    public float width;
    public string senderId;
    public string lineId;
    
    // For joiner's 2D screen coordinates (normalized 0-1)
    public float screenX;
    public float screenY;
    public bool isScreenCoordinate; // true if this is 2D screen pos, false if 3D world pos

    // Helper to create a Start event (3D world coordinates)
    public static DrawingEvent CreateStart(Vector3 pos, Color color, float width, string lineId = null, string senderId = null)
    {
        return new DrawingEvent
        {
            type = "start",
            x = pos.x, y = pos.y, z = pos.z,
            r = color.r, g = color.g, b = color.b,
            width = width,
            lineId = lineId,
            senderId = senderId,
            isScreenCoordinate = false
        };
    }
    
    // Helper to create a Start event with 2D screen coordinates (for joiner)
    public static DrawingEvent CreateStartScreen(Vector2 screenPos, Color color, float width, string lineId = null, string senderId = null)
    {
        return new DrawingEvent
        {
            type = "start",
            screenX = screenPos.x,
            screenY = screenPos.y,
            r = color.r, g = color.g, b = color.b,
            width = width,
            lineId = lineId,
            senderId = senderId,
            isScreenCoordinate = true
        };
    }

    // Helper to create a Point event (3D world coordinates)
    public static DrawingEvent CreatePoint(Vector3 pos, string lineId = null, string senderId = null)
    {
        return new DrawingEvent
        {
            type = "point",
            x = pos.x, y = pos.y, z = pos.z,
            lineId = lineId,
            senderId = senderId,
            isScreenCoordinate = false
        };
    }
    
    // Helper to create a Point event with 2D screen coordinates (for joiner)
    public static DrawingEvent CreatePointScreen(Vector2 screenPos, string lineId = null, string senderId = null)
    {
        return new DrawingEvent
        {
            type = "point",
            screenX = screenPos.x,
            screenY = screenPos.y,
            lineId = lineId,
            senderId = senderId,
            isScreenCoordinate = true
        };
    }

    // Helper to create End/Undo/Clear events
    public static DrawingEvent CreateTypeOnly(string type, string lineId = null, string senderId = null)
    {
        return new DrawingEvent { type = type, lineId = lineId, senderId = senderId };
    }
}
