# WebRTCManager Implementation Guide

## Overview
The `WebRTCManager` class provides a complete WebRTC implementation with audio, video, and data channel support using Unity's WebRTC package and WebSocket signaling.

## Features
- **Audio Streaming**: Bidirectional audio using AudioSource
- **Video Streaming**: Bidirectional video using Camera capture
- **Data Channel**: Binary and text messaging between peers
- **WebSocket Signaling**: Server-based signaling for connection setup
- **Event-Driven**: Clean event system for UI integration

## Setup Instructions

### 1. Inspector Configuration
Attach the `WebRTCManager` script to a GameObject and configure:
- **Server URL**: WebSocket signaling server (default: `ws://localhost:8080`)
- **Local Camera**: Camera component for video streaming
- **Local Audio Source**: AudioSource component for audio streaming

### 2. Required Components
```csharp
// Example GameObject setup
GameObject webRTCObject = new GameObject("WebRTC Manager");
WebRTCManager manager = webRTCObject.AddComponent<WebRTCManager>();

// Assign camera
manager.localCamera = Camera.main;

// Create and assign audio source
AudioSource audioSource = webRTCObject.AddComponent<AudioSource>();
manager.localAudioSource = audioSource;
```

## Public API

### Room Management
```csharp
// Create a new room (as initiator)
manager.CreateRoom();

// Join an existing room
manager.JoinRoom("room-code-here");

// Get current room ID
string roomId = manager.GetRoomId();
```

### Data Channel Communication
```csharp
// Send text message
manager.SendDataChannelMessage("Hello from Unity!");

// Send binary data
byte[] data = System.Text.Encoding.UTF8.GetBytes("Binary message");
manager.SendDataChannelBytes(data);
```

### Media Access
```csharp
// Get remote video texture (for displaying on UI or 3D object)
Texture remoteVideo = manager.GetRemoteVideoTexture();

// Attach remote audio to an AudioSource for playback
AudioSource speaker = GetComponent<AudioSource>();
manager.AttachRemoteAudio(speaker);

// Access remote tracks directly
VideoStreamTrack videoTrack = manager.RemoteVideoTrack;
AudioStreamTrack audioTrack = manager.RemoteAudioTrack;
```

### Connection Management
```csharp
// Close connection and cleanup
manager.CloseConnection();
```

## Events

Subscribe to events for connection state changes:

```csharp
void Start()
{
    WebRTCManager manager = GetComponent<WebRTCManager>();
    
    // Room events
    manager.OnRoomCreated += HandleRoomCreated;
    manager.OnRoomJoined += HandleRoomJoined;
    manager.OnError += HandleError;
    
    // Connection events
    manager.OnConnectionEstablished += HandleConnected;
    manager.OnConnectionClosed += HandleDisconnected;
}

void HandleRoomCreated(string roomId)
{
    Debug.Log($"Room created: {roomId}");
}

void HandleRoomJoined(string roomId)
{
    Debug.Log($"Joined room: {roomId}");
}

void HandleError(string error)
{
    Debug.LogError($"Error: {error}");
}

void HandleConnected()
{
    Debug.Log("WebRTC connection established!");
}

void HandleDisconnected()
{
    Debug.Log("WebRTC connection closed");
}
```

## Data Channel Messages

Override the `OnDataChannelMessage` method or create a custom handler:

```csharp
public class CustomWebRTCManager : WebRTCManager
{
    protected override void OnDataChannelMessage(byte[] data)
    {
        string message = System.Text.Encoding.UTF8.GetString(data);
        // Handle custom data
        Debug.Log($"Received: {message}");
    }
}
```

## Usage Example

```csharp
using UnityEngine;

public class WebRTCExample : MonoBehaviour
{
    public WebRTCManager webRTCManager;
    public UnityEngine.UI.RawImage remoteVideoDisplay;
    public AudioSource remoteAudioOutput;
    
    void Start()
    {
        // Subscribe to events
        webRTCManager.OnRoomCreated += OnRoomCreated;
        webRTCManager.OnConnectionEstablished += OnConnected;
    }
    
    public void CreateNewRoom()
    {
        webRTCManager.CreateRoom();
    }
    
    public void JoinExistingRoom(string roomCode)
    {
        webRTCManager.JoinRoom(roomCode);
    }
    
    void OnRoomCreated(string roomId)
    {
        Debug.Log($"Share this room code: {roomId}");
    }
    
    void OnConnected()
    {
        // Display remote video
        if (remoteVideoDisplay != null)
        {
            StartCoroutine(UpdateRemoteVideo());
        }
        
        // Play remote audio
        webRTCManager.AttachRemoteAudio(remoteAudioOutput);
    }
    
    System.Collections.IEnumerator UpdateRemoteVideo()
    {
        while (true)
        {
            Texture texture = webRTCManager.GetRemoteVideoTexture();
            if (texture != null)
            {
                remoteVideoDisplay.texture = texture;
            }
            yield return new WaitForSeconds(0.1f);
        }
    }
    
    public void SendChatMessage(string message)
    {
        webRTCManager.SendDataChannelMessage(message);
    }
}
```

## Architecture

### Initiator vs Joiner Flow

**Initiator (Room Creator):**
1. Calls `CreateRoom()`
2. WebSocket sends "create" message
3. Receives "room_created" response with room ID
4. Initializes WebRTC (creates data channel)
5. When second peer joins, creates and sends offer
6. Receives answer from joiner
7. ICE candidates exchanged
8. Connection established

**Joiner:**
1. Calls `JoinRoom(roomId)`
2. WebSocket sends "join" message
3. Receives "room_joined" response
4. Initializes WebRTC (waits for data channel)
5. Receives offer from initiator
6. Creates and sends answer
7. ICE candidates exchanged
8. Connection established

### Signaling Flow

```
Client A (Initiator)          Server          Client B (Joiner)
       |                         |                    |
       |--create---------------->|                    |
       |<------room_created------|                    |
       |                         |                    |
       |                         |<--------join-------|
       |                         |------room_joined-->|
       |                         |                    |
       |--offer----------------->|--offer------------>|
       |                         |                    |
       |<-------answer-----------|<-------answer------|
       |                         |                    |
       |--ice_candidate--------->|--ice_candidate---->|
       |<------ice_candidate-----|<------ice_candidate|
       |                         |                    |
       [Connection Established]  |  [Connection Established]
```

## Notes

- **STUN Server**: Uses Google's public STUN server by default. For production, configure your own TURN server for NAT traversal.
- **Video Quality**: Default resolution is 1280x720. Modify in `SetupLocalMedia()`.
- **Audio**: Make sure AudioSource has an AudioClip or uses microphone input.
- **Cleanup**: Always call `CloseConnection()` before destroying the GameObject.
- **Threading**: WebRTC operations use Unity coroutines for async operations.

## Troubleshooting

**No video streaming:**
- Ensure `localCamera` is assigned in the inspector
- Check that camera is enabled and rendering
- Verify WebRTC.Update() coroutine is running

**No audio streaming:**
- Ensure `localAudioSource` is assigned
- Check that AudioSource has audio input
- For microphone: `AudioSource.clip = Microphone.Start(null, true, 10, 44100);`

**Data channel not opening:**
- Check WebSocket connection is established
- Verify signaling messages are being sent/received
- Ensure both peers have completed ICE exchange

**Connection fails:**
- Check STUN/TURN server configuration
- Verify both peers are connected to signaling server
- Check firewall/NAT settings
