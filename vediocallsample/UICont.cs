using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UIElements;
using Videocall.Networking;
using Videocall.WebRTC;

public class UICont : MonoBehaviour
{
    private enum UiScreen
    {
        Main,
        Create,
        Join,
        Lobby,
        Chat,
        Video
    }

    [Header("UI References")]
    [SerializeField] private GameObject uiObject;
    [SerializeField] private VisualTreeAsset mainUI;
    [SerializeField] private VisualTreeAsset creatorUI;
    [SerializeField] private VisualTreeAsset joinorUI;
    [SerializeField] private VisualTreeAsset lobbyUI;
    [SerializeField] private VisualTreeAsset videoUI;
    [SerializeField] private VisualTreeAsset chatUI;

    [Header("Networking")]
    [SerializeField] private string signalingServerUrl = "wss://signaling-server-d1e9.onrender.com";
    [SerializeField] private TextAsset iceServersJson;

    private UIDocument uiDocument;
    private SignalingClient signalingClient;
    private RtcChatPeer rtcPeer;

    private UiScreen currentScreen = UiScreen.Main;
    private string roomId = string.Empty;
    private bool readySignalSent;

    private Label creatorRoomCodeLabel;
    private TextField joinRoomField;
    private ScrollView lobbyScroll;
    private Label lobbyRoomInfo;
    private ScrollView chatScroll;
    private TextField chatInput;
    private Button sendButton;
    private Button videoTabButton;
    private Image localVideoImage;
    private Image remoteVideoImage;
    private Button micToggleButton;
    private Button cameraToggleButton;
    private Button hangUpButton;
    private Button chatTabButton;
    private Label callStatusLabel;

    private readonly List<string> lobbyMessages = new();
    private readonly List<string> chatMessages = new();
    private RTCIceServer[] cachedIceServers;
    private Texture latestLocalTexture;
    private Texture latestRemoteTexture;
    private bool micMuted;
    private bool cameraMuted;
    private bool hasActiveCall;

    private void OnEnable()
    {
        if (uiObject == null)
        {
            Debug.LogError("UI controller is missing the UiObject reference.");
            enabled = false;
            return;
        }

        uiDocument = uiObject.GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument component missing on UiObject.");
            enabled = false;
            return;
        }

        SetupSignalingClient();
        SetupRtcPeer();
        ShowScreen(UiScreen.Main);
    }

    private void OnDisable()
    {
        if (signalingClient != null)
        {
            signalingClient.OnConnected -= HandleSocketConnected;
            signalingClient.OnDisconnected -= HandleSocketDisconnected;
            signalingClient.OnError -= HandleSocketError;
            signalingClient.OnRoomCreated -= HandleRoomCreated;
            signalingClient.OnRoomJoined -= HandleRoomJoined;
            signalingClient.OnSignal -= HandleSignal;
            signalingClient.Disconnect();
            signalingClient = null;
        }

        if (rtcPeer != null)
        {
            rtcPeer.OnSignalReady -= HandleRtcSignalReady;
            rtcPeer.OnChatMessageReceived -= AppendChatMessage;
            rtcPeer.OnStatusMessage -= AppendLobbyStatus;
            rtcPeer.OnChannelOpen -= HandleChannelOpen;
            rtcPeer.OnChannelClosed -= HandleChannelClosed;
            rtcPeer.OnLocalVideoTexture -= HandleLocalVideoTexture;
            rtcPeer.OnRemoteVideoTexture -= HandleRemoteVideoTexture;
            rtcPeer.HangUp();
        }
    }

    private void Update()
    {
        signalingClient?.Tick();
    }

    private void SetupSignalingClient()
    {
        signalingClient = new SignalingClient(signalingServerUrl);
        signalingClient.OnConnected += HandleSocketConnected;
        signalingClient.OnDisconnected += HandleSocketDisconnected;
        signalingClient.OnError += HandleSocketError;
        signalingClient.OnRoomCreated += HandleRoomCreated;
        signalingClient.OnRoomJoined += HandleRoomJoined;
        signalingClient.OnSignal += HandleSignal;
        signalingClient.Connect();
    }

    private void SetupRtcPeer()
    {
        rtcPeer = GetComponent<RtcChatPeer>();
        if (rtcPeer == null)
        {
            rtcPeer = gameObject.AddComponent<RtcChatPeer>();
        }

        rtcPeer.OnSignalReady += HandleRtcSignalReady;
        rtcPeer.OnChatMessageReceived += AppendChatMessage;
        rtcPeer.OnStatusMessage += AppendLobbyStatus;
        rtcPeer.OnChannelOpen += HandleChannelOpen;
        rtcPeer.OnChannelClosed += HandleChannelClosed;
        rtcPeer.OnLocalVideoTexture += HandleLocalVideoTexture;
        rtcPeer.OnRemoteVideoTexture += HandleRemoteVideoTexture;
    }

    private void ShowScreen(UiScreen screen)
    {
        currentScreen = screen;
        switch (screen)
        {
            case UiScreen.Main:
                uiDocument.visualTreeAsset = mainUI;
                break;
            case UiScreen.Create:
                uiDocument.visualTreeAsset = creatorUI;
                break;
            case UiScreen.Join:
                uiDocument.visualTreeAsset = joinorUI;
                break;
            case UiScreen.Lobby:
                uiDocument.visualTreeAsset = lobbyUI;
                break;
            case UiScreen.Chat:
                uiDocument.visualTreeAsset = chatUI;
                break;
            case UiScreen.Video:
                uiDocument.visualTreeAsset = videoUI;
                break;
        }

        switch (screen)
        {
            case UiScreen.Main:
                SetupMainScreen();
                break;
            case UiScreen.Create:
                SetupCreateScreen();
                break;
            case UiScreen.Join:
                SetupJoinScreen();
                break;
            case UiScreen.Lobby:
                SetupLobbyScreen();
                break;
            case UiScreen.Chat:
                SetupChatScreen();
                break;
            case UiScreen.Video:
                SetupVideoScreen();
                break;
        }
    }

    private void SetupMainScreen()
    {
        var root = uiDocument.rootVisualElement;
        root.Q<Button>("joinButton")?.RegisterCallback<ClickEvent>(_ => ShowScreen(UiScreen.Join));
        root.Q<Button>("createButton")?.RegisterCallback<ClickEvent>(_ => ShowScreen(UiScreen.Create));
    }

    private void SetupCreateScreen()
    {
        var root = uiDocument.rootVisualElement;
        creatorRoomCodeLabel = root.Q<Label>("roomCode");
        root.Q<Button>("backButton")?.RegisterCallback<ClickEvent>(_ => ShowScreen(UiScreen.Main));

        var enterButton = root.Q<Button>("enterButton");
        if (enterButton != null)
        {
            enterButton.clicked += () =>
            {
                readySignalSent = false;
                AppendLobbyStatus("Requesting a new room...");
                signalingClient?.RequestRoomCreation();
                ShowScreen(UiScreen.Lobby);
            };
        }
    }

    private void SetupJoinScreen()
    {
        var root = uiDocument.rootVisualElement;
        joinRoomField = root.Q<TextField>("roomCode");
        root.Q<Button>("backButton")?.RegisterCallback<ClickEvent>(_ => ShowScreen(UiScreen.Main));

        var enterButton = root.Q<Button>("enterButton");
        if (enterButton != null)
        {
            enterButton.clicked += () =>
            {
                var targetRoom = joinRoomField?.value?.Trim();
                if (string.IsNullOrEmpty(targetRoom))
                {
                    AppendLobbyStatus("Please enter a room code before joining.");
                    return;
                }

                readySignalSent = false;
                AppendLobbyStatus($"Joining room {targetRoom}...");
                signalingClient?.RequestRoomJoin(targetRoom);
                ShowScreen(UiScreen.Lobby);
            };
        }
    }

    private void SetupLobbyScreen()
    {
        var root = uiDocument.rootVisualElement;
        lobbyScroll = root.Q<ScrollView>("peopleScrollView");
        lobbyRoomInfo = root.Q<Label>("roomInfo");
        if (lobbyRoomInfo != null)
        {
            lobbyRoomInfo.text = string.IsNullOrEmpty(roomId) ? "Room: -" : $"Room: {roomId}";
        }

        lobbyScroll?.Clear();
        foreach (var message in lobbyMessages)
        {
            AppendLabel(lobbyScroll, message);
        }

        var startButton = root.Q<Button>("startButton");
        if (startButton != null)
        {
            startButton.clicked += () =>
            {
                if (rtcPeer != null && rtcPeer.HasOpenChannel)
                {
                    ShowScreen(videoUI != null ? UiScreen.Video : UiScreen.Chat);
                }
                else
                {
                    AppendLobbyStatus("Still waiting for the data channel to open...");
                }
            };
        }
    }

    private void SetupChatScreen()
    {
        var root = uiDocument.rootVisualElement;
        chatScroll = root.Q<ScrollView>("chatsScrollView");
        chatInput = root.Q<TextField>("chatInput");
        sendButton = root.Q<Button>("sendButton");
        videoTabButton = root.Q<Button>("videoTabButton");

        chatScroll?.Clear();
        foreach (var message in chatMessages)
        {
            AppendLabel(chatScroll, message);
        }

        if (sendButton != null)
        {
            sendButton.SetEnabled(rtcPeer != null && rtcPeer.HasOpenChannel);
            sendButton.clicked += SendChat;
        }

        if (videoTabButton != null)
        {
            videoTabButton.SetEnabled(videoUI != null && hasActiveCall);
            videoTabButton.clicked += () =>
            {
                if (videoUI != null && hasActiveCall)
                {
                    ShowScreen(UiScreen.Video);
                }
            };
        }
    }

    private void SetupVideoScreen()
    {
        var root = uiDocument.rootVisualElement;
        localVideoImage = root.Q<Image>("localVideoImage");
        remoteVideoImage = root.Q<Image>("remoteVideoImage");
        micToggleButton = root.Q<Button>("micToggleButton");
        cameraToggleButton = root.Q<Button>("cameraToggleButton");
        hangUpButton = root.Q<Button>("hangupButton");
        chatTabButton = root.Q<Button>("chatTabButton");
        callStatusLabel = root.Q<Label>("callStatusLabel");

        ApplyVideoTextures();
        UpdateAvButtons();

        if (micToggleButton != null)
        {
            micToggleButton.clicked += ToggleMic;
        }

        if (cameraToggleButton != null)
        {
            cameraToggleButton.clicked += ToggleCamera;
        }

        if (hangUpButton != null)
        {
            hangUpButton.clicked += HangUpCall;
        }

        if (chatTabButton != null)
        {
            chatTabButton.clicked += () => ShowScreen(UiScreen.Chat);
        }
    }

    private void ApplyVideoTextures()
    {
        if (localVideoImage != null)
        {
            localVideoImage.image = latestLocalTexture;
        }

        if (remoteVideoImage != null)
        {
            remoteVideoImage.image = latestRemoteTexture;
        }
    }

    private void UpdateAvButtons()
    {
        if (micToggleButton != null)
        {
            micToggleButton.text = micMuted ? "Unmute Mic" : "Mute Mic";
        }

        if (cameraToggleButton != null)
        {
            cameraToggleButton.text = cameraMuted ? "Turn Camera On" : "Turn Camera Off";
        }

        if (callStatusLabel != null)
        {
            callStatusLabel.text = hasActiveCall ? "In Call" : "Connecting...";
        }
    }

    private void ToggleMic()
    {
        micMuted = !micMuted;
        rtcPeer?.SetMicrophoneMuted(micMuted);
        UpdateAvButtons();
    }

    private void ToggleCamera()
    {
        cameraMuted = !cameraMuted;
        rtcPeer?.SetCameraMuted(cameraMuted);
        UpdateAvButtons();
    }

    private void HangUpCall()
    {
        hasActiveCall = false;
        rtcPeer?.HangUp();
        AppendLobbyStatus("Call ended.");
        sendButton?.SetEnabled(false);
        videoTabButton?.SetEnabled(false);
        ShowScreen(UiScreen.Lobby);
    }

    private void HandleLocalVideoTexture(Texture texture)
    {
        latestLocalTexture = texture;
        if (currentScreen == UiScreen.Video && localVideoImage != null)
        {
            localVideoImage.image = texture;
        }
    }

    private void HandleRemoteVideoTexture(Texture texture)
    {
        latestRemoteTexture = texture;
        if (currentScreen == UiScreen.Video && remoteVideoImage != null)
        {
            remoteVideoImage.image = texture;
        }
    }

    private void SendChat()
    {
        var message = chatInput?.value;
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        rtcPeer?.SendChatMessage(message.Trim());
        if (chatInput != null)
        {
            chatInput.value = string.Empty;
        }
    }

    private void HandleSocketConnected()
    {
        AppendLobbyStatus("Connected to the signaling server.");
    }

    private void HandleSocketDisconnected()
    {
        AppendLobbyStatus("Disconnected from the signaling server.");
    }

    private void HandleSocketError(string error)
    {
        AppendLobbyStatus($"Error: {error}");
    }

    private void HandleRoomCreated(string newRoomId, string clientId)
    {
        roomId = newRoomId;
        AppendLobbyStatus($"Room created. Share this code: {roomId}");
        if (creatorRoomCodeLabel != null)
        {
            creatorRoomCodeLabel.text = roomId;
        }

        ShowScreen(UiScreen.Lobby);
        StartRtcSession(true);
    }

    private void HandleRoomJoined(string joinedRoomId, string clientId)
    {
        roomId = joinedRoomId;
        AppendLobbyStatus($"Joined room {roomId}. Waiting for the host...");
        ShowScreen(UiScreen.Lobby);
        StartRtcSession(false);
    }

    private void HandleSignal(SignalEnvelope envelope)
    {
        if (envelope == null || string.IsNullOrEmpty(envelope.payload))
        {
            return;
        }

        try
        {
            var payload = JsonUtility.FromJson<SignalPayload>(envelope.payload);
            rtcPeer?.HandleRemoteSignal(payload);
        }
        catch (Exception exception)
        {
            AppendLobbyStatus($"Failed to read signal: {exception.Message}");
        }
    }

    private void HandleRtcSignalReady(string payloadJson)
    {
        signalingClient?.SendSignal(payloadJson);
    }

    private void HandleChannelOpen()
    {
        hasActiveCall = true;
        micMuted = false;
        cameraMuted = false;
        rtcPeer?.SetMicrophoneMuted(false);
        rtcPeer?.SetCameraMuted(false);
        AppendLobbyStatus("Peer connected. Starting call...");
        sendButton?.SetEnabled(true);
        videoTabButton?.SetEnabled(videoUI != null);
        if (videoUI != null)
        {
            ShowScreen(UiScreen.Video);
        }
        else
        {
            ShowScreen(UiScreen.Chat);
        }
    }

    private void HandleChannelClosed()
    {
        hasActiveCall = false;
        AppendLobbyStatus("Peer channel closed.");
        sendButton?.SetEnabled(false);
        videoTabButton?.SetEnabled(false);
        if (currentScreen == UiScreen.Video)
        {
            ShowScreen(UiScreen.Lobby);
        }
    }

    private void StartRtcSession(bool initiator)
    {
        cachedIceServers ??= BuildIceServers();
        rtcPeer?.Configure(cachedIceServers, initiator);
        readySignalSent = false;
        if (!initiator)
        {
            SendReadySignal();
        }
    }

    private void SendReadySignal()
    {
        if (readySignalSent)
        {
            return;
        }

        readySignalSent = true;
        var payload = JsonUtility.ToJson(SignalPayload.Ready());
        signalingClient?.SendSignal(payload);
        AppendLobbyStatus("Ready signal sent to the host.");
    }

    private void AppendLobbyStatus(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        lobbyMessages.Add(message);
        if (lobbyMessages.Count > 100)
        {
            lobbyMessages.RemoveAt(0);
        }

        AppendLabel(lobbyScroll, message);
    }

    private void AppendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        chatMessages.Add(message);
        if (chatMessages.Count > 200)
        {
            chatMessages.RemoveAt(0);
        }

        AppendLabel(chatScroll, message);
    }

    private void AppendLabel(ScrollView target, string text)
    {
        if (target == null)
        {
            return;
        }

        var label = new Label(text);
        target.Add(label);
        target.ScrollTo(label);
    }

    private RTCIceServer[] BuildIceServers()
    {
        var fallback = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
        };

        if (iceServersJson == null || string.IsNullOrWhiteSpace(iceServersJson.text))
        {
            return fallback;
        }

        try
        {
            var wrapper = JsonUtility.FromJson<IceServerList>("{\"servers\":" + iceServersJson.text + "}");
            if (wrapper?.servers == null || wrapper.servers.Length == 0)
            {
                return fallback;
            }

            var result = new List<RTCIceServer>();
            foreach (var entry in wrapper.servers)
            {
                if (string.IsNullOrEmpty(entry.urls))
                {
                    continue;
                }

                var server = new RTCIceServer { urls = new[] { entry.urls } };
                if (!string.IsNullOrEmpty(entry.username))
                {
                    server.username = entry.username;
                }
                if (!string.IsNullOrEmpty(entry.credential))
                {
                    server.credential = entry.credential;
                }

                result.Add(server);
            }

            return result.Count > 0 ? result.ToArray() : fallback;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to parse ICE servers: {exception.Message}");
            return fallback;
        }
    }

    [Serializable]
    private class IceServerList
    {
        public IceServerEntry[] servers;
    }

    [Serializable]
    private class IceServerEntry
    {
        public string urls;
        public string username;
        public string credential;
    }
}
