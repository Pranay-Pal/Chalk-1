// using UnityEngine;
// using NativeWebSocket;
// using System;
// using System.IO;
// using System.Collections.Generic;
// using System.Data.Common;
// using Unity.WebRTC;

// public class WebRTCManager: MonoBehaviour
// {
//     private WebSocket webSocket;
//     private enum User
//     {
//         CreateRoom,
//         JoinRoom
//     }
//     private enum MessageType
//     {
//         create,
//         room_created,
//         join,
//         room_joined,
//         signal,
//         error
//     }
//     [Serializable]
//     private class SignalPayload
//     {
//         public string sdp;
//         public string sdpType;
//         public string candidate;
//         public string sdpMid;
//         public int sdpMLineIndex = -1;
//     }

//     [Serializable]
//     private class ClientMessage
//     {
//         public string type;
//         public string roomId;
//         public SignalPayload payload;
//     }

//     [Serializable]
//     private class ServerMessage
//     {
//         public string type;
//         public string roomId;
//         public string myId;
//         public string senderId;
//         public SignalPayload payload;
//         public string message;
//     }

//     private User currentUser;
//     private String roomId;
//     private String MyID;
//     private List<String> peerIDs = new List<String>();

//     private RTCPeerConnection peerConnection;
    
//     async void Start()
//     {

        
//         webSocket = new WebSocket("wss://signaling-server-d1e9.onrender.com");
//         peerConnection = new RTCPeerConnection();

//         if(currentUser == User.CreateRoom){
//             Debug.Log("Creating Room");
//             webSocket.OnOpen += () =>
//             {
//                 Debug.Log("WebSocket Opened for Creating Room");
//                 if(webSocket.State==WebSocketState.Open){
//                     Debug.Log("WebSocket is Open");
//                     webSocket.SendText("{\"type\":\"create\"}");
//                 }
//                 var offervar = peerConnection.CreateOffer();
//                 var descr=offervar.Desc;
                

//             };
//             webSocket.OnError += (e) =>
//             {
//                 Debug.LogError("WebSocket Error: " + e);
//             };
//             webSocket.OnClose += (e) =>
//             {
//                 Debug.Log("WebSocket Closed with code " + e);
//             };
//             webSocket.OnMessage += (bytes) =>
//             {
//                 Debug.Log("WebSocket Message received - " + System.Text.Encoding.UTF8.GetString(bytes));
//                 HandleServerMessage(System.Text.Encoding.UTF8.GetString(bytes));
//             };
//         } else if(currentUser == User.JoinRoom){
//             Debug.Log("Joining Room");
//         }

//         await webSocket.Connect();
//     }

//     void Update()
//     {
//         webSocket?.DispatchMessageQueue();
//     }
//     void HandleServerMessage(string message)
//     {
//         var msg = JsonUtility.FromJson<ServerMessage>(message);
//         if (msg == null || string.IsNullOrEmpty(msg.type)) return;

//         switch (msg.type)
//         {
//             case "room_created":
//                 roomId = msg.roomId;
//                 MyID = msg.myId;
//                 Debug.Log($"Room created: {roomId} as {MyID}.");
//                 break;

//             case "room_joined":
//                 roomId = msg.roomId;
//                 MyID = msg.myId;
//                 Debug.Log($"Joined room {roomId} as {MyID}.");
//                 break;

//             case "signal":
//                 if (msg.payload != null)
//                 {
//                     if(!peerIDs.Contains(msg.senderId)){
//                         peerIDs.Add(msg.senderId);
//                     }
//                 }
//                 break;

//             case "error":
//                 Debug.LogError("Signaling error: " + msg.message);
//                 break;
//         }
//     }
// }


using UnityEngine;
using Unity.WebRTC;
using NativeWebSocket;
using System;
using System.Collections;
using System.Collections.Generic;

public class WebRTCManager : MonoBehaviour
{
    [Header("WebSocket Settings")]
    [SerializeField] private string signalingServerUrl = "wss://signaling-server-d1e9.onrender.com";
    public bool isInitiator = false;
    private string roomId;
    public string myId;

    private SignalingClient signalingClient;
    private enum PendingSignalingAction { None, CreateRoom, JoinRoom }
    private PendingSignalingAction pendingSignalingAction = PendingSignalingAction.None;
    private string pendingJoinRoomId;

    // Events for UI callbacks
    public event Action<string> OnRoomCreated;
    public event Action<string> OnRoomJoined;
    public event Action<string> OnError;
    public event Action OnConnectionEstablished;
    public event Action OnConnectionClosed;
    public event Action<DrawingEvent> OnDrawingDataReceived;
    public event Action<int, float> OnSignalingReconnectScheduled;
    public event Action OnSignalingReconnectCancelled;
    public event Action OnSignalingReconnectFailed;

    //---------WebRTC Setup---------//
    [Header("WebRTC Settings")]
    public Camera localCamera;
    public AudioSource localAudioSource;
    
    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogs = true;
    
    [Header("Signaling Reliability")]
    [SerializeField] private bool autoReconnectToSignaling = true;
    [SerializeField] private int maxReconnectAttempts = 5;
    [SerializeField] private float reconnectBaseDelaySeconds = 1.5f;
    [SerializeField] private float reconnectMaxDelaySeconds = 15f;

    [Header("ICE Config Source")]
    [SerializeField] private bool loadIceServersFromResources = true;
    [SerializeField] private string iceServerConfigResourcePath = "ice_servers";
    
    [Header("ICE Servers Configuration")]
    public string[] stunServers = new string[]
    {
        "stun:relay1.expressturn.com:3480"
    };
    
    public TurnServerConfig[] turnServers = new TurnServerConfig[]
    {
        new TurnServerConfig
        {
            urls = new string[] { 
                "turn:relay1.expressturn.com:3480?transport=tcp"
            },
            username = "000000002078464525",
            credential = "tH+jLmUffQ9q9zfefHaSq0RPWko="
        }
    };
    
    [Serializable]
    public class TurnServerConfig
    {
        public string[] urls;
        public string username;
        public string credential;
    }

    [Serializable]
    private class IceServerFileConfig
    {
        public string[] stun;
        public TurnServerConfig[] turn;
    }

    [Serializable]
    private class IceServerArrayWrapper
    {
        public IceServerArrayEntry[] items;
    }

    [Serializable]
    private class IceServerArrayEntry
    {
        public string urls;
        public string username;
        public string credential;
    }
    
    private RTCPeerConnection peerConnection;
    private RTCDataChannel dataChannel;
    
    private VideoStreamTrack localVideoTrack;
    private AudioStreamTrack localAudioTrack;
    private MediaStream localStream;
    private MediaStream remoteStream;
    
    public VideoStreamTrack RemoteVideoTrack { get; private set; }
    public AudioStreamTrack RemoteAudioTrack { get; private set; }
    
    private List<RTCIceCandidate> pendingIceCandidates = new List<RTCIceCandidate>();
    private bool remoteDescriptionSet = false;

    private bool remotePeerReady = false;
    private bool readySignalSent = false;
    private bool negotiationInProgress = false;
    private bool iceServersLoadedFromResource = false;
    private bool isShuttingDown;
    private bool isLeavingRoom;
    private string lastRequestedRoomId;

    

    [Serializable]
    private class SignalPayload
    {
        public string sdp;
        public string sdpType; // 'offer' or 'answer'
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex = -1;
        public string command;
    }

    //---------WebRTC Setup-------------------------------------------------//
    RTCPeerConnection localConnection;
    RTCPeerConnection remoteConnection;
    RTCDataChannel sendChannel;


    void Start()
    {
        StartCoroutine(WebRTC.Update());
        // Don't connect to signaling server until user creates/joins room
        // EnsureSignalingClient() will be called by CreateRoom/JoinRoom
    }

    private void EnsureSignalingClient()
    {
        if (signalingClient != null)
        {
            ConfigureSignalingClientOptions();
            return;
        }

        signalingClient = new SignalingClient(signalingServerUrl);
        SignalingMessagePump.Register(signalingClient.Tick);
        signalingClient.OnConnected += OnSignalingConnected;
        signalingClient.OnDisconnected += HandleSignalingDisconnected;
        signalingClient.OnError += HandleSignalingError;
        signalingClient.OnRoomCreated += HandleSignalingRoomCreated;
        signalingClient.OnRoomJoined += HandleSignalingRoomJoined;
        signalingClient.OnSignal += HandleSignalEnvelope;
        signalingClient.OnReconnectScheduled += HandleSignalingReconnectScheduled;
        signalingClient.OnReconnectCancelled += HandleSignalingReconnectCancelled;
        signalingClient.OnReconnectFailed += HandleSignalingReconnectFailed;
        ConfigureSignalingClientOptions();
    }

    private void ConfigureSignalingClientOptions()
    {
        if (signalingClient == null)
            return;

        signalingClient.AutoReconnect = autoReconnectToSignaling;
        signalingClient.MaxReconnectAttempts = maxReconnectAttempts;
        signalingClient.ReconnectBaseDelaySeconds = reconnectBaseDelaySeconds;
        signalingClient.ReconnectMaxDelaySeconds = reconnectMaxDelaySeconds;
    }

    private void EnsureIceServerConfigLoaded()
    {
        if (!loadIceServersFromResources || iceServersLoadedFromResource)
            return;

        var textAsset = Resources.Load<TextAsset>(iceServerConfigResourcePath);
        if (textAsset == null)
        {
            Debug.LogWarning($"[WebRTCManager] ICE config resource '{iceServerConfigResourcePath}' not found.");
            iceServersLoadedFromResource = true;
            return;
        }

        if (TryLoadStructuredIceConfig(textAsset.text))
        {
            iceServersLoadedFromResource = true;
            return;
        }

        if (TryLoadArrayIceConfig(textAsset.text))
        {
            iceServersLoadedFromResource = true;
            return;
        }

        Debug.LogError("[WebRTCManager] Failed to parse ICE server config: unsupported format.");
        iceServersLoadedFromResource = true;
    }

    private bool TryLoadStructuredIceConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith("["))
            return false;

        if (!JsonValidationUtility.TryParse(json, out IceServerFileConfig config, out string error) || config == null)
        {
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"[WebRTCManager] Structured ICE config parse error: {error}");
            return false;
        }

        if ((config.stun == null || config.stun.Length == 0) && (config.turn == null || config.turn.Length == 0))
            return false;

        if (config.stun != null && config.stun.Length > 0)
            stunServers = config.stun;

        if (config.turn != null && config.turn.Length > 0)
            turnServers = config.turn;

        Debug.Log("[WebRTCManager] Loaded ICE servers from structured resource file.");
        return true;
    }

    private bool TryLoadArrayIceConfig(string json)
    {
        string wrappedJson = $"{{\"items\":{json}}}";
        if (!JsonValidationUtility.TryParse(wrappedJson, out IceServerArrayWrapper wrapper, out string error) || wrapper?.items == null)
        {
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"[WebRTCManager] Array ICE config parse error: {error}");
            return false;
        }

        var stunList = new List<string>();
        var turnList = new List<TurnServerConfig>();

        foreach (var entry in wrapper.items)
        {
            if (entry == null || string.IsNullOrEmpty(entry.urls))
                continue;

            if (entry.urls.StartsWith("stun", StringComparison.OrdinalIgnoreCase))
            {
                stunList.Add(entry.urls);
            }
            else if (entry.urls.StartsWith("turn", StringComparison.OrdinalIgnoreCase))
            {
                turnList.Add(new TurnServerConfig
                {
                    urls = new[] { entry.urls },
                    username = entry.username,
                    credential = entry.credential
                });
            }
        }

        if (stunList.Count == 0 && turnList.Count == 0)
            return false;

        if (stunList.Count > 0)
            stunServers = stunList.ToArray();

        if (turnList.Count > 0)
            turnServers = turnList.ToArray();

        Debug.Log("[WebRTCManager] Loaded ICE servers from array resource file.");
        return true;
    }

    private void OnSignalingConnected()
    {
        Debug.Log("[WebRTCManager] Signaling connected");
        isLeavingRoom = false;
        ExecutePendingSignalingAction();
    }

    private void HandleSignalingDisconnected(WebSocketCloseCode code)
    {
        Debug.Log($"[WebRTCManager] Signaling disconnected ({code})");

        if (isShuttingDown || isLeavingRoom)
            return;

        CloseConnection();
        OnConnectionClosed?.Invoke();

        if (!string.IsNullOrEmpty(lastRequestedRoomId))
        {
            pendingSignalingAction = PendingSignalingAction.JoinRoom;
            pendingJoinRoomId = lastRequestedRoomId;
        }
    }

    private void HandleSignalingError(string message)
    {
        Debug.LogError($"[WebRTCManager] Signaling error: {message}");
        OnError?.Invoke(message);
    }

    private void HandleSignalingRoomCreated(string createdRoomId, string clientId)
    {
        roomId = createdRoomId;
        myId = clientId;
        lastRequestedRoomId = createdRoomId;
        Debug.Log($"[WebRTCManager] Room created successfully. Room ID: {roomId}, Client ID: {clientId}");
        OnRoomCreated?.Invoke(roomId);
    }

    private void HandleSignalingRoomJoined(string joinedRoomId, string clientId)
    {
        roomId = joinedRoomId;
        myId = clientId;
        lastRequestedRoomId = joinedRoomId;
        Debug.Log($"[WebRTCManager] Room joined successfully. Room ID: {roomId}, Client ID: {clientId}");
        OnRoomJoined?.Invoke(roomId);

        if (!isInitiator)
        {
            Debug.Log("[WebRTCManager] Sending ready signal as joiner");
            // Small delay to ensure peer connection is fully initialized
            StartCoroutine(SendReadySignalDelayed());
        }
    }

    private IEnumerator SendReadySignalDelayed()
    {
        // Wait a frame to ensure peer connection is fully set up
        yield return null;
        
        if (peerConnection == null)
        {
            Debug.LogError("[WebRTCManager] Cannot send ready signal - peer connection is null!");
            yield break;
        }
        
        SendReadySignal();
    }

    private void HandleSignalingReconnectScheduled(int attempt, float delay)
    {
        Debug.Log($"[WebRTCManager] Signaling reconnect attempt {attempt} in {delay:0.0}s");
        OnSignalingReconnectScheduled?.Invoke(attempt, delay);
    }

    private void HandleSignalingReconnectCancelled()
    {
        OnSignalingReconnectCancelled?.Invoke();
    }

    private void HandleSignalingReconnectFailed()
    {
        Debug.LogError("[WebRTCManager] Unable to reconnect to signaling server.");
        OnSignalingReconnectFailed?.Invoke();
        OnError?.Invoke("Signaling reconnection attempts exhausted.");
    }
    public void CreateRoom()
    {
        Debug.Log("[WebRTCManager] CreateRoom called");
        if (!EnsureSignalingReadyOrQueue(PendingSignalingAction.CreateRoom))
            return;

        BeginCreateRoom();
    }
    
    public string GetRoomId()
    {
        return roomId;
    }
    
    public void JoinRoom(string roomId)
    {
        Debug.Log($"[WebRTCManager] JoinRoom called with room ID: {roomId}");
        if (string.IsNullOrWhiteSpace(roomId))
        {
            Debug.LogError("[WebRTCManager] Cannot join without a room ID.");
            return;
        }

        if (!EnsureSignalingReadyOrQueue(PendingSignalingAction.JoinRoom, roomId))
            return;

        BeginJoinRoom(roomId);
    }

    private void BeginCreateRoom()
    {
        Debug.Log("[WebRTCManager] BeginCreateRoom - Setting up as initiator");
        isLeavingRoom = false;
        isInitiator = true;
        remotePeerReady = false;
        readySignalSent = false;
        negotiationInProgress = false;

        InitializeWebRTC();
        signalingClient.CreateRoom();
    }

    private void BeginJoinRoom(string targetRoomId)
    {
        Debug.Log($"[WebRTCManager] BeginJoinRoom - Setting up as joiner for room: {targetRoomId}");
        isLeavingRoom = false;
        isInitiator = false;
        remotePeerReady = true; // joiner will answer once offer arrives
        readySignalSent = false;
        negotiationInProgress = false;
        lastRequestedRoomId = targetRoomId;

        InitializeWebRTC();
        signalingClient.JoinRoom(targetRoomId);
    }

    private bool EnsureSignalingReadyOrQueue(PendingSignalingAction action, string roomToJoin = null)
    {
        EnsureSignalingClient();

        if (signalingClient != null && signalingClient.IsConnected)
            return true;

        pendingSignalingAction = action;
        pendingJoinRoomId = roomToJoin;
        Debug.Log($"[WebRTCManager] Signaling not ready; queued action {action}. Connecting...");
        signalingClient.Connect();
        return false;
    }

    private void ExecutePendingSignalingAction()
    {
        if (pendingSignalingAction == PendingSignalingAction.None)
            return;

        var action = pendingSignalingAction;
        var targetRoom = pendingJoinRoomId;
        pendingSignalingAction = PendingSignalingAction.None;
        pendingJoinRoomId = null;

        switch (action)
        {
            case PendingSignalingAction.CreateRoom:
                Debug.Log("[WebRTCManager] Executing pending CreateRoom action");
                // Set up state before initializing WebRTC
                isInitiator = true;
                remotePeerReady = false;
                readySignalSent = false;
                negotiationInProgress = false;
                
                InitializeWebRTC();
                signalingClient.CreateRoom();
                break;
            case PendingSignalingAction.JoinRoom:
                if (string.IsNullOrWhiteSpace(targetRoom))
                {
                    Debug.LogWarning("[WebRTCManager] Pending join action missing room id; ignoring.");
                    break;
                }
                Debug.Log($"[WebRTCManager] Executing pending JoinRoom action for room {targetRoom}");
                // Set up state before initializing WebRTC
                isInitiator = false;
                remotePeerReady = true;
                readySignalSent = false;
                negotiationInProgress = false;
                lastRequestedRoomId = targetRoom;
                
                InitializeWebRTC();
                signalingClient.JoinRoom(targetRoom);
                break;
        }
    }

    private void HandleSignalEnvelope(SignalEnvelope envelope)
    {
        if (envelope == null || string.IsNullOrEmpty(envelope.payload))
            return;

        if (!string.IsNullOrEmpty(myId) && !string.IsNullOrEmpty(envelope.senderId) && envelope.senderId == myId)
            return;

        SignalPayload payload = null;
        try
        {
            payload = JsonUtility.FromJson<SignalPayload>(envelope.payload);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WebRTCManager] Failed to parse signal payload: {ex.Message}");
        }

        if (payload == null)
            return;

        if (!string.IsNullOrEmpty(payload.command))
        {
            if (payload.command.Equals("ready", StringComparison.OrdinalIgnoreCase))
            {
                remotePeerReady = true;
                Debug.Log("[WebRTCManager] Remote peer signaled ready");
                TryCreateOffer();
            }
            return;
        }

        HandleSignal(payload);
    }

    private void SendReadySignal()
    {
        if (isInitiator || readySignalSent)
            return;

        var payload = new SignalPayload { command = "ready" };
        signalingClient?.SendSignal(JsonUtility.ToJson(payload));
        readySignalSent = true;
        Debug.Log("[WebRTCManager] Ready signal sent");
    }

    private void TryCreateOffer()
    {
        if (!isInitiator || !remotePeerReady || negotiationInProgress)
            return;

        if (peerConnection == null)
        {
            Debug.LogWarning("[WebRTCManager] Cannot create offer before peer connection is initialized");
            return;
        }

        negotiationInProgress = true;
        StartCoroutine(CreateOfferCoroutine());
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            isShuttingDown = true;
            LeaveRoomAndCleanup();
        }
        else
        {
            isShuttingDown = false;
            EnsureSignalingClient();
        }
    }

    private void OnApplicationQuit()
    {
        isShuttingDown = true;
        LeaveRoomAndCleanup();
    }

    private void OnDestroy()
    {
        isShuttingDown = true;
        LeaveRoomAndCleanup();
        DisposeSignalingClient();
    }

    private void DisposeSignalingClient()
    {
        if (signalingClient == null)
            return;

        SignalingMessagePump.Unregister(signalingClient.Tick);
        signalingClient.Dispose();
        signalingClient = null;
    }

    //---------WebRTC Setup-------------------------------------------------//
    
    private void InitializeWebRTC()
    {
        if (peerConnection != null)
        {
            Debug.LogWarning("[WebRTCManager] Peer connection already exists, cleaning up before re-initialization");
            CloseConnection();
        }

        Debug.Log("[WebRTCManager] Initializing WebRTC peer connection...");
        EnsureIceServerConfigLoaded();
        RTCConfiguration config = default;

        List<RTCIceServer> iceServersList = new List<RTCIceServer>();
        
        if (stunServers != null && stunServers.Length > 0)
        {
            foreach (var stunUrl in stunServers)
            {
                if (!string.IsNullOrEmpty(stunUrl))
                {
                    iceServersList.Add(new RTCIceServer { urls = new[] { stunUrl } });
                    Debug.Log($"Added STUN server: {stunUrl}");
                }
            }
        }
        
        if (turnServers != null && turnServers.Length > 0)
        {
            foreach (var turnConfig in turnServers)
            {
                if (turnConfig.urls != null && turnConfig.urls.Length > 0)
                {
                    var iceServer = new RTCIceServer
                    {
                        urls = turnConfig.urls,
                        username = turnConfig.username,
                        credential = turnConfig.credential,
                        credentialType = RTCIceCredentialType.Password
                    };
                    iceServersList.Add(iceServer);
                    Debug.Log($"Added TURN server: {string.Join(", ", turnConfig.urls)} (user: {turnConfig.username})");
                }
            }
        }
        
        if (iceServersList.Count == 0)
        {
            iceServersList.Add(new RTCIceServer { urls = new[] {
                "stun:relay1.expressturn.com:3480"
            } });
            Debug.LogWarning("No ICE servers configured. Using default STUN server.");
        }
        
        config.iceServers = iceServersList.ToArray();
        config.iceTransportPolicy = RTCIceTransportPolicy.All;

        peerConnection = new RTCPeerConnection(ref config);

        peerConnection.OnIceCandidate = candidate =>
        {
            if (!string.IsNullOrEmpty(candidate.Candidate))
            {
                SendIceCandidate(candidate);
            }
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log($"ICE connection state: {state}");
            if (state == RTCIceConnectionState.Connected)
            {
                Debug.Log("[WebRTCManager] WebRTC connection established successfully");
                OnConnectionEstablished?.Invoke();
                
                // Close signaling server connection - no longer needed after WebRTC peer connection is established
                // Signaling server only helps with initial connection setup (SDP exchange, ICE candidates)
                // All further communication (video, audio, data channel) goes directly through WebRTC peer-to-peer
                if (signalingClient != null && signalingClient.IsConnected)
                {
                    Debug.Log("[WebRTCManager] WebRTC P2P established - disconnecting from signaling server");
                    signalingClient.Disconnect();
                }
            }
            else if (state == RTCIceConnectionState.Failed)
            {
                Debug.LogError("[WebRTCManager] ICE connection failed. This usually indicates NAT traversal issues.");
                Debug.LogError("[WebRTCManager] Please check: 1) TURN server credentials, 2) Firewall settings, 3) Network connectivity");
                
                // Attempt ICE restart
                if (peerConnection != null && isInitiator)
                {
                    Debug.Log("[WebRTCManager] Attempting ICE restart...");
                    StartCoroutine(RestartIceCoroutine());
                }
                else
                {
                    OnConnectionClosed?.Invoke();
                }
            }
            else if (state == RTCIceConnectionState.Disconnected)
            {
                Debug.LogWarning("[WebRTCManager] ICE connection disconnected");
                OnConnectionClosed?.Invoke();
            }
            else if (state == RTCIceConnectionState.Closed)
            {
                Debug.Log("[WebRTCManager] ICE connection closed");
                OnConnectionClosed?.Invoke();
            }
        };

        peerConnection.OnTrack = e =>
        {
            Debug.Log($"Received track: {e.Track.Kind}");
            if (remoteStream == null)
            {
                remoteStream = new MediaStream();
            }
            remoteStream.AddTrack(e.Track);

            if (e.Track is VideoStreamTrack videoTrack)
            {
                RemoteVideoTrack = videoTrack;
                Debug.Log("Remote video track received");
            }
            else if (e.Track is AudioStreamTrack audioTrack)
            {
                RemoteAudioTrack = audioTrack;
                Debug.Log("Remote audio track received");
            }
        };

        SetupLocalMedia();

        if (isInitiator)
        {
            CreateDataChannel();
        }
        else
        {
            peerConnection.OnDataChannel = channel =>
            {
                dataChannel = channel;
                SetupDataChannelHandlers();
                Debug.Log("Data channel received from remote peer");
            };
        }
    }


    private void SetupLocalMedia()
    {
        localStream = new MediaStream();

        if (localCamera != null)
        {
            if (isInitiator)
            {
                // Only creator (initiator) shares their AR camera view
                localVideoTrack = localCamera.CaptureStreamTrack(1280, 720);
                peerConnection.AddTrack(localVideoTrack, localStream);
                Debug.Log($"[WebRTCManager] Local video track added (Creator - AR Camera)");
            }
            else
            {
                // Joiner does NOT share camera - they only receive creator's video and send 2D drawing coordinates
                Debug.Log("[WebRTCManager] Joiner mode - Not streaming camera (will receive creator's AR view)");
            }
        }
        else
        {
            Debug.LogWarning("[WebRTCManager] Local camera not assigned. Video streaming disabled.");
        }

        if (localAudioSource != null)
        {
            localAudioTrack = new AudioStreamTrack(localAudioSource);
            peerConnection.AddTrack(localAudioTrack, localStream);
            Debug.Log("[WebRTCManager] Local audio track added");
        }
        else
        {
            Debug.LogWarning("[WebRTCManager] Local audio source not assigned. Audio streaming disabled.");
        }
    }

    private void CreateDataChannel()
    {
        RTCDataChannelInit init = new RTCDataChannelInit();
        dataChannel = peerConnection.CreateDataChannel("dataChannel", init);
        SetupDataChannelHandlers();
        Debug.Log("Data channel created");
    }

    private void SetupDataChannelHandlers()
    {
        dataChannel.OnOpen = () =>
        {
            Debug.Log("[WebRTCManager] Data channel OPENED - ready to send/receive drawing data");
        };

        dataChannel.OnClose = () =>
        {
            Debug.Log("[WebRTCManager] Data channel CLOSED");
        };

        dataChannel.OnMessage = bytes =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            if (enableDebugLogs) Debug.Log($"[WebRTCManager] Data channel message received: {message}");
            
            try
            {
                var drawingEvent = JsonUtility.FromJson<DrawingEvent>(message);
                if (drawingEvent != null && !string.IsNullOrEmpty(drawingEvent.type))
                {
                    Debug.Log($"[WebRTCManager] Drawing event received: type={drawingEvent.type}, lineId={drawingEvent.lineId}");
                    OnDrawingDataReceived?.Invoke(drawingEvent);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCManager] Failed to parse drawing event: {e.Message}");
            }

            OnDataChannelMessage(bytes);
        };
    }

    private IEnumerator CreateOfferCoroutine()
    {
        var op = peerConnection.CreateOffer();
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"Failed to create offer: {op.Error.message}");
            negotiationInProgress = false;
            yield break;
        }

        var desc = op.Desc;
        var setLocal = peerConnection.SetLocalDescription(ref desc);
        yield return setLocal;

        if (setLocal.IsError)
        {
            Debug.LogError($"Failed to set local description: {setLocal.Error.message}");
            negotiationInProgress = false;
            yield break;
        }

        Debug.Log("Offer created and set as local description");

        
        SendOffer(desc);
        negotiationInProgress = false;
    }

    private IEnumerator RestartIceCoroutine()
    {
        if (peerConnection == null)
        {
            Debug.LogWarning("[WebRTCManager] Cannot restart ICE: peer connection is null");
            yield break;
        }

        Debug.Log("[WebRTCManager] Restarting ICE...");
        
        // Create new offer with iceRestart option
        RTCOfferAnswerOptions options = new RTCOfferAnswerOptions
        {
            iceRestart = true
        };

        var op = peerConnection.CreateOffer(ref options);
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"[WebRTCManager] Failed to create ICE restart offer: {op.Error.message}");
            OnConnectionClosed?.Invoke();
            yield break;
        }

        var desc = op.Desc;
        var setLocal = peerConnection.SetLocalDescription(ref desc);
        yield return setLocal;

        if (setLocal.IsError)
        {
            Debug.LogError($"[WebRTCManager] Failed to set local description for ICE restart: {setLocal.Error.message}");
            OnConnectionClosed?.Invoke();
            yield break;
        }

        Debug.Log("[WebRTCManager] ICE restart offer created and sent");
        SendOffer(desc);
    }

    private void HandleSignal(SignalPayload payload)
    {
        if (peerConnection == null)
        {
            Debug.LogWarning("[WebRTCManager] Received signal but peer connection is null. Ignoring.");
            return;
        }

        if (!string.IsNullOrEmpty(payload.sdp))
        {
            
            RTCSessionDescription desc = new RTCSessionDescription
            {
                sdp = payload.sdp,
                type = payload.sdpType == "offer" ? RTCSdpType.Offer : RTCSdpType.Answer
            };

            StartCoroutine(HandleRemoteDescriptionCoroutine(desc));
        }
        else if (!string.IsNullOrEmpty(payload.candidate))
        {
            
            RTCIceCandidateInit candidateInit = new RTCIceCandidateInit
            {
                candidate = payload.candidate,
                sdpMid = payload.sdpMid,
                sdpMLineIndex = payload.sdpMLineIndex
            };

            RTCIceCandidate candidate = new RTCIceCandidate(candidateInit);
            
            if (remoteDescriptionSet)
            {
                peerConnection.AddIceCandidate(candidate);
                Debug.Log("ICE candidate added");
            }
            else
            {
                pendingIceCandidates.Add(candidate);
                Debug.Log("ICE candidate queued (waiting for remote description)");
            }
        }
    }

    private IEnumerator HandleRemoteDescriptionCoroutine(RTCSessionDescription desc)
    {
        var setRemote = peerConnection.SetRemoteDescription(ref desc);
        yield return setRemote;

        if (setRemote.IsError)
        {
            Debug.LogError($"Failed to set remote description: {setRemote.Error.message}");
            yield break;
        }

        remoteDescriptionSet = true;
        Debug.Log($"Remote description set: {desc.type}");

        foreach (var candidate in pendingIceCandidates)
        {
            peerConnection.AddIceCandidate(candidate);
            Debug.Log("Pending ICE candidate added");
        }
        pendingIceCandidates.Clear();

        if (desc.type == RTCSdpType.Offer)
        {
            var answerOp = peerConnection.CreateAnswer();
            yield return answerOp;

            if (answerOp.IsError)
            {
                Debug.LogError($"Failed to create answer: {answerOp.Error.message}");
                yield break;
            }

            var answerDesc = answerOp.Desc;
            var setLocal = peerConnection.SetLocalDescription(ref answerDesc);
            yield return setLocal;

            if (setLocal.IsError)
            {
                Debug.LogError($"Failed to set local description: {setLocal.Error.message}");
                yield break;
            }

            Debug.Log("Answer created and set as local description");

            // Send answer via signaling server
            SendAnswer(answerDesc);
        }
    }

    //---------Signaling Methods (WebSocket)---------//

    private void SendOffer(RTCSessionDescription desc)
    {
        var payload = new SignalPayload
        {
            sdp = desc.sdp,
            sdpType = "offer"
        };
        signalingClient?.SendSignal(JsonUtility.ToJson(payload));
        Debug.Log("Offer sent via signaling");
    }

    private void SendAnswer(RTCSessionDescription desc)
    {
        var payload = new SignalPayload
        {
            sdp = desc.sdp,
            sdpType = "answer"
        };
        signalingClient?.SendSignal(JsonUtility.ToJson(payload));
        Debug.Log("Answer sent via signaling");
    }

    private void SendIceCandidate(RTCIceCandidate candidate)
    {
        var payload = new SignalPayload
        {
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = candidate.SdpMLineIndex ?? -1
        };
        signalingClient?.SendSignal(JsonUtility.ToJson(payload));
        Debug.Log("ICE candidate sent via signaling");
    }

    //---------Public Methods for External Use---------//

    public void SendDataChannelMessage(string message)
    {
        if (dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open)
        {
            dataChannel.Send(message);
            Debug.Log($"Sent message via data channel: {message}");
        }
        else
        {
            Debug.LogWarning("Data channel is not open. Cannot send message.");
        }
    }

    public void SendDrawingEvent(DrawingEvent drawingEvent)
    {
        if (drawingEvent == null) return;
        
        if (dataChannel == null)
        {
            Debug.LogWarning("[WebRTCManager] Cannot send drawing event - data channel is null");
            return;
        }
        
        if (dataChannel.ReadyState != RTCDataChannelState.Open)
        {
            Debug.LogWarning($"[WebRTCManager] Cannot send drawing event - data channel state is {dataChannel.ReadyState}. Event type: {drawingEvent.type}");
            return;
        }
        
        string json = JsonUtility.ToJson(drawingEvent);
        SendDataChannelMessage(json);
    }

    public void SendDataChannelBytes(byte[] data)
    {
        if (dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open)
        {
            dataChannel.Send(data);
            Debug.Log($"Sent {data.Length} bytes via data channel");
        }
        else
        {
            Debug.LogWarning("Data channel is not open. Cannot send data.");
        }
    }


    public Texture GetRemoteVideoTexture()
    {
        return RemoteVideoTrack?.Texture;
    }

    public bool IsDataChannelReady()
    {
        return dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open;
    }

    public RTCDataChannelState GetDataChannelState()
    {
        return dataChannel != null ? dataChannel.ReadyState : RTCDataChannelState.Closed;
    }


    public void AttachRemoteAudio(AudioSource audioSource)
    {
        if (RemoteAudioTrack != null && audioSource != null)
        {
            audioSource.SetTrack(RemoteAudioTrack);
            audioSource.loop = true;
            audioSource.Play();
            Debug.Log("Remote audio attached to AudioSource");
        }
        else
        {
            Debug.LogWarning("Cannot attach remote audio. Track or AudioSource is null.");
        }
    }

    public void LeaveRoomAndCleanup()
    {
        isLeavingRoom = true;
        pendingSignalingAction = PendingSignalingAction.None;
        pendingJoinRoomId = null;
        lastRequestedRoomId = null;
        roomId = null;
        myId = null;
        CloseConnection();
        signalingClient?.Disconnect();
    }

    public void CloseConnection()
    {
        Debug.Log("[WebRTCManager] Closing WebRTC connection and cleaning up resources...");
        
        if (dataChannel != null)
        {
            dataChannel.Close();
            dataChannel.Dispose();
            dataChannel = null;
        }

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        if (localVideoTrack != null)
        {
            localVideoTrack.Dispose();
            localVideoTrack = null;
        }

        if (localAudioTrack != null)
        {
            localAudioTrack.Dispose();
            localAudioTrack = null;
        }

        if (localStream != null)
        {
            localStream.Dispose();
            localStream = null;
        }

        if (remoteStream != null)
        {
            remoteStream.Dispose();
            remoteStream = null;
        }

        RemoteVideoTrack = null;
        RemoteAudioTrack = null;
        remoteDescriptionSet = false;
        pendingIceCandidates.Clear();
        remotePeerReady = false;
        readySignalSent = false;
        negotiationInProgress = false;

        Debug.Log("[WebRTCManager] WebRTC connection closed and resources cleaned up");
    }


    protected virtual void OnDataChannelMessage(byte[] data)
    {
        string message = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"Data channel message: {message}");
    }

    void handleSendChannelStatusChange()
    {
        Debug.Log("Send channel status: " + sendChannel.ReadyState);
    }

}
