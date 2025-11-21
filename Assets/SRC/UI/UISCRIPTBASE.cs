using UnityEngine;
using UnityEngine.UIElements;

public class UISCRIPTBASE : MonoBehaviour
{
    [SerializeField] private GameObject uIDocumentUiMain;
    [SerializeField] private GameObject webRTC;
    [SerializeField] private GameObject uIDocumentLoadingUi;
    [SerializeField] private GameObject xRInteractionManager;
    [SerializeField] private GameObject xROrigin;
    [SerializeField] private GameObject xRCameraOfset;
    [SerializeField] private GameObject xRMainCamera;
    [SerializeField] private GameObject aRSession;
    [SerializeField] private GameObject UIDoccumentARControl;
    [SerializeField] private VisualTreeAsset loadingTemplate;
    [SerializeField] private string defaultConnectingMessage = "Connecting to room...";
    
    private void ToogleXR(bool isEnabled)
    {
        xRInteractionManager.SetActive(isEnabled);
        xROrigin.SetActive(isEnabled);
        xRCameraOfset.SetActive(isEnabled);
        xRMainCamera.SetActive(isEnabled);
        aRSession.SetActive(isEnabled);
        UIDoccumentARControl.SetActive(isEnabled);
        uIDocumentLoadingUi.SetActive(isEnabled);
    }

    private WebRTCManager webrtcManager;

    private string roomID;
    private Label currentRoomCodeLabel;
    private Button currentCreateConfirmButton;
    private bool isAwaitingRoomCreation;
    private const string DefaultRoomCodePlaceholder = "XXXX";
    private const string RequestingRoomCodeMessage = "Requesting...";

    public VisualTreeAsset joinTemplate;
    public VisualTreeAsset createTemplate;
    private VisualTreeAsset mainTemplate;           
    private Label loadingStatusLabel;
    private Button cancelConnectButton;
    private string currentLoadingMessage;

    private Button jnrm;
    private Button crrm;
    private enum UIStatePage
    {
        MainMenu,
        JoinRoom,
        CreateRoom,
        Connecting,
        InSession
    }
    private UIStatePage currentPage;

    void OnEnable()
    {
        webrtcManager = webRTC.GetComponent<WebRTCManager>();
        currentPage = UIStatePage.MainMenu;

        // Subscribe to WebRTC events
        if (webrtcManager != null)
        {
            webrtcManager.OnRoomCreated += HandleRoomCreated;
            webrtcManager.OnRoomJoined += HandleRoomJoined;
            webrtcManager.OnError += HandleError;
            webrtcManager.OnConnectionEstablished += HandleConnectionEstablished;
            webrtcManager.OnConnectionClosed += HandleConnectionClosed;
            webrtcManager.OnSignalingReconnectScheduled += HandleSignalingReconnectScheduled;
            webrtcManager.OnSignalingReconnectFailed += HandleSignalingReconnectFailed;
            webrtcManager.OnSignalingReconnectCancelled += HandleSignalingReconnectCancelled;
        }

        var doc = uIDocumentUiMain.GetComponent<UIDocument>();
        if (doc == null)
        {
            Debug.LogWarning("UIDocument not found on GameObject assigned to UISCRIPTBASE");
            return;
        }
        mainTemplate = doc.visualTreeAsset;
        var root = doc.rootVisualElement;
        if (root == null) return;
        jnrm = root.Q<Button>("joinButton");
        crrm = root.Q<Button>("createButton");
        if (crrm != null)
        {
            crrm.clicked += OnCreate;
        }
        if (jnrm != null)
        {
            jnrm.clicked += OnJoin;
        }
        ToogleXR(false);
    }

    void OnDisable()
    {
        DetachCreatePageBindings(true);
        if (crrm != null)
        {
            crrm.clicked -= OnCreate;
        }
        if (jnrm != null)
        {
            jnrm.clicked -= OnJoin;
        }

        // Unsubscribe from WebRTC events
        if (webrtcManager != null)
        {
            webrtcManager.OnRoomCreated -= HandleRoomCreated;
            webrtcManager.OnRoomJoined -= HandleRoomJoined;
            webrtcManager.OnError -= HandleError;
            webrtcManager.OnConnectionEstablished -= HandleConnectionEstablished;
            webrtcManager.OnConnectionClosed -= HandleConnectionClosed;
            webrtcManager.OnSignalingReconnectScheduled -= HandleSignalingReconnectScheduled;
            webrtcManager.OnSignalingReconnectFailed -= HandleSignalingReconnectFailed;
            webrtcManager.OnSignalingReconnectCancelled -= HandleSignalingReconnectCancelled;
        }
    }
    void OnJoin()
    {
        DetachCreatePageBindings(true);
        var doc = uIDocumentUiMain.GetComponent<UIDocument>();
        if (doc == null)
        {
            Debug.LogWarning("UIDocument not found when trying to open Join page");
            return;
        }
        if (joinTemplate == null)
        {
            Debug.LogWarning("joinTemplate not assigned in inspector");
            return;
        }

        currentPage = UIStatePage.JoinRoom;
        doc.visualTreeAsset = joinTemplate;
        var root = doc.rootVisualElement;
        if (root == null) return;

        var back = root.Q<Button>("backButton");
        var joinConfirm = root.Q<Button>("joinConfirmButton");
        var roomCodeField = root.Q<TextField>("roomCodeField");

        if (back != null)
        {
            back.clicked += () => ShowMain(doc);
        }

        if (joinConfirm != null)
        {
            joinConfirm.clicked += () =>
            {
                var code = roomCodeField != null ? roomCodeField.value : string.Empty;
                if (!string.IsNullOrEmpty(code))
                {
                    Debug.Log($"Join requested for room: {code}");
                    ShowConnectingPage("Joining room...");
                    webrtcManager.JoinRoom(code);
                }
                else
                {
                    Debug.LogWarning("Room code is empty. Cannot join.");
                }
            };
        }
    }

    
    void OnCreate()
    {
        var doc = uIDocumentUiMain.GetComponent<UIDocument>();
        if (doc == null)
        {
            Debug.LogWarning("UIDocument not found when trying to open Create page");
            return;
        }
        if (createTemplate == null)
        {
            Debug.LogWarning("createTemplate not assigned in inspector");
            return;
        }

        DetachCreatePageBindings(false);
        currentPage = UIStatePage.CreateRoom;
        doc.visualTreeAsset = createTemplate;

        var root = doc.rootVisualElement;
        if (root == null) return;

        var back = root.Q<Button>("backButton");
        currentCreateConfirmButton = root.Q<Button>("createConfirmButton");
        currentRoomCodeLabel = root.Q<Label>("roomcode");

        if (back != null)
        {
            back.clicked += () => ShowMain(doc);
        }

        if (currentCreateConfirmButton != null)
        {
            currentCreateConfirmButton.clicked += HandleCreateConfirmClicked;
        }

        SyncCreatePageUI();
    }
    private void ShowMain(UIDocument doc)
    {
        if (doc == null) return;
        DetachCreatePageBindings(true);
        if (mainTemplate != null)
        {
            doc.visualTreeAsset = mainTemplate;
        }

  
        var root = doc.rootVisualElement;
        if (root == null) return;

        if (crrm != null) crrm.clicked -= OnCreate;
        if (jnrm != null) jnrm.clicked -= OnJoin;

        jnrm = root.Q<Button>("joinButton");
        crrm = root.Q<Button>("createButton");
        if (crrm != null)
        {
            crrm.clicked += OnCreate;
        }
        if (jnrm != null)
        {
            jnrm.clicked += OnJoin;
        }

        currentPage = UIStatePage.MainMenu;
    }

    public string SetRoomID()
    {
        roomID = webrtcManager.GetRoomId();
        return roomID;
    }

    private void HandleRoomCreated(string roomId)
    {
        roomID = roomId;
        Debug.Log($"UI: Room created with ID: {roomId}");
        isAwaitingRoomCreation = false;
        
        if (currentPage == UIStatePage.CreateRoom)
        {
            SyncCreatePageUI();
        }
    }

    private void HandleRoomJoined(string roomId)
    {
        roomID = roomId;
        Debug.Log($"UI: Joined room with ID: {roomId}");

        if (currentPage == UIStatePage.Connecting)
        {
            UpdateLoadingMessage("Room joined. Finalizing connection...");
        }
    }

    private void HandleError(string errorMessage)
    {
        Debug.LogError($"UI: Error - {errorMessage}");
        if (currentPage == UIStatePage.CreateRoom)
        {
            isAwaitingRoomCreation = false;
            SyncCreatePageUI();
        }

        if (currentPage == UIStatePage.Connecting || currentPage == UIStatePage.InSession)
        {
            ExitToLobby("Encountered signaling error");
        }
    }

    private void HandleConnectionEstablished()
    {
        EnterDrawingSession();
    }

    private void HandleConnectionClosed()
    {
        if (currentPage == UIStatePage.InSession || currentPage == UIStatePage.Connecting)
        {
            ExitToLobby("Connection closed");
        }
    }

    private void HandleSignalingReconnectScheduled(int attempt, float delay)
    {
        if (currentPage == UIStatePage.Connecting)
        {
            UpdateLoadingMessage($"Reconnecting (attempt {attempt}) in {delay:0.0}s...");
        }
    }

    private void HandleSignalingReconnectFailed()
    {
        ExitToLobby("Unable to reconnect to signaling server");
    }

    private void HandleSignalingReconnectCancelled()
    {
        if (currentPage == UIStatePage.Connecting)
        {
            UpdateLoadingMessage(defaultConnectingMessage);
        }
    }

    private void HandleCreateConfirmClicked()
    {
        if (webrtcManager == null)
        {
            Debug.LogWarning("WebRTCManager reference missing; cannot create/join room.");
            return;
        }

        if (string.IsNullOrEmpty(roomID))
        {
            if (isAwaitingRoomCreation)
                return;

            Debug.Log("Creating room...");
            isAwaitingRoomCreation = true;
            SyncCreatePageUI();
            webrtcManager.CreateRoom();
        }
        else
        {
            Debug.Log($"Entering room: {roomID}");
            ShowConnectingPage("Connecting to room...");
            webrtcManager.JoinRoom(roomID);
        }
    }

    private void SyncCreatePageUI()
    {
        if (currentPage != UIStatePage.CreateRoom)
            return;

        if (currentRoomCodeLabel != null)
        {
            if (isAwaitingRoomCreation)
            {
                currentRoomCodeLabel.text = RequestingRoomCodeMessage;
            }
            else
            {
                currentRoomCodeLabel.text = string.IsNullOrEmpty(roomID) ? DefaultRoomCodePlaceholder : roomID;
            }
        }

        if (currentCreateConfirmButton == null)
            return;

        if (isAwaitingRoomCreation)
        {
            currentCreateConfirmButton.text = "Creating...";
            currentCreateConfirmButton.SetEnabled(false);
        }
        else if (string.IsNullOrEmpty(roomID))
        {
            currentCreateConfirmButton.text = "Create";
            currentCreateConfirmButton.SetEnabled(true);
        }
        else
        {
            currentCreateConfirmButton.text = "Enter";
            currentCreateConfirmButton.SetEnabled(true);
        }
    }

    private void ShowConnectingPage(string message)
    {
        if (loadingTemplate == null)
        {
            Debug.LogWarning("[UISCRIPTBASE] Loading template is not assigned; cannot show connecting state.");
            return;
        }

        if (uIDocumentUiMain != null && !uIDocumentUiMain.activeSelf)
        {
            uIDocumentUiMain.SetActive(true);
        }

        var doc = uIDocumentUiMain != null ? uIDocumentUiMain.GetComponent<UIDocument>() : null;
        if (doc == null)
        {
            Debug.LogWarning("[UISCRIPTBASE] UIDocument reference missing; cannot show connecting page.");
            return;
        }

        DetachCreatePageBindings(true);
        DetachLoadingPageBindings();
        currentPage = UIStatePage.Connecting;
        doc.visualTreeAsset = loadingTemplate;

        var root = doc.rootVisualElement;
        if (root == null)
            return;

        loadingStatusLabel = root.Q<Label>("loadingMessage");
        cancelConnectButton = root.Q<Button>("cancelButton");
        if (cancelConnectButton != null)
        {
            cancelConnectButton.clicked += CancelPendingConnection;
        }

        UpdateLoadingMessage(string.IsNullOrEmpty(message) ? defaultConnectingMessage : message);
    }

    private void UpdateLoadingMessage(string message)
    {
        currentLoadingMessage = string.IsNullOrEmpty(message) ? defaultConnectingMessage : message;
        if (loadingStatusLabel != null)
        {
            loadingStatusLabel.text = currentLoadingMessage;
        }
    }

    private void DetachLoadingPageBindings()
    {
        if (cancelConnectButton != null)
        {
            cancelConnectButton.clicked -= CancelPendingConnection;
            cancelConnectButton = null;
        }

        loadingStatusLabel = null;
        currentLoadingMessage = null;
    }

    private void CancelPendingConnection()
    {
        Debug.Log("[UISCRIPTBASE] Connection cancelled by user");
        webrtcManager?.LeaveRoomAndCleanup();
        ExitToLobby("Cancelled join/create flow");
    }

    private void DetachCreatePageBindings(bool resetRequestState)
    {
        if (currentCreateConfirmButton != null)
        {
            currentCreateConfirmButton.clicked -= HandleCreateConfirmClicked;
            currentCreateConfirmButton = null;
        }

        currentRoomCodeLabel = null;

        if (resetRequestState)
        {
            isAwaitingRoomCreation = false;
        }
    }

    private void EnterDrawingSession()
    {
        if (currentPage == UIStatePage.InSession)
            return;

        DetachLoadingPageBindings();
        currentPage = UIStatePage.InSession;
        if (uIDocumentUiMain != null && uIDocumentUiMain.activeSelf)
        {
            uIDocumentUiMain.SetActive(false);
        }

        ToogleXR(true);
    }

    private void ExitToLobby(string reason = null)
    {
        DetachLoadingPageBindings();
        DetachCreatePageBindings(true);

        if (uIDocumentUiMain != null && !uIDocumentUiMain.activeSelf)
        {
            uIDocumentUiMain.SetActive(true);
        }

        var doc = uIDocumentUiMain != null ? uIDocumentUiMain.GetComponent<UIDocument>() : null;
        if (doc != null)
        {
            ShowMain(doc);
        }

        currentPage = UIStatePage.MainMenu;
        ToogleXR(false);

        if (!string.IsNullOrEmpty(reason))
        {
            Debug.Log($"[UISCRIPTBASE] {reason}");
        }
    }

    public void LeaveCurrentSession()
    {
        webrtcManager?.LeaveRoomAndCleanup();
        ExitToLobby("Left room");
    }
}

