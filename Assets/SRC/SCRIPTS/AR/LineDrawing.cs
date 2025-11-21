using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARSubsystems;
using System;
using System.Collections.Generic;

[System.Serializable]
public class LineData
{
    public List<Vector3> points = new List<Vector3>();
    public Color color = Color.white;
    public float width = 0.01f;
}

public class LineDrawing : MonoBehaviour
{
    private ARAnchorManager anchorManager;
    private ARPlaneManager planeManager;
    private bool leastOnePlaneDetected = false;
    private ARPointCloudManager pointCloudManager;
    private bool leastOnePointCloudDetected = false;
    private ARRaycastManager raycastManager;
    private static readonly List<ARRaycastHit> s_ArRaycastHits = new List<ARRaycastHit>();
    [SerializeField] private GameObject loading;

    [Header("Debug / Status")]
    [SerializeField] private Text statusText;
    [SerializeField] private bool enableDebugLogs = true;
    [SerializeField] private WebRTCManager webRTCManager;

    // Role detection
    private bool IsCreator => webRTCManager != null && webRTCManager.isInitiator;
    private bool IsJoiner => webRTCManager != null && !webRTCManager.isInitiator;

    private bool Drawable = false;
    private bool snapshotSentThisSession = false;
    private string LocalUserId => webRTCManager != null && !string.IsNullOrEmpty(webRTCManager.myId) ? webRTCManager.myId : "local";

    private List<ARAnchor> anchors = new List<ARAnchor>();
    private List<ARPlane> planes = new List<ARPlane>();
    private List<ARPointCloud> pointClouds = new List<ARPointCloud>();
    private List<LineRenderer> lines = new List<LineRenderer>();
    private HashSet<LineRenderer> anchoredLines = new HashSet<LineRenderer>();
    private readonly Dictionary<string, LineRenderer> lineIdToRenderer = new Dictionary<string, LineRenderer>();
    private readonly Dictionary<string, LineData> lineIdToData = new Dictionary<string, LineData>();
    private readonly Dictionary<string, string> lineIdToOwner = new Dictionary<string, string>();
    private readonly List<string> lineDrawOrder = new List<string>();
    [SerializeField] private float distanceFromCamera = 0.5f;
    LineRenderer presentLine;
    private string currentLineId;

    // Remote drawing state queue
    private static readonly Queue<DrawingEvent> remoteEventQueue = new Queue<DrawingEvent>();
    private static readonly object remoteQueueLock = new object();

    [SerializeField] private Material lineMaterial;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private float lineWidth = 0.01f;
    
    [Header("Available Colors for Random Assignment")]
    [SerializeField] private Color[] availableColors = new Color[]
    {
        Color.red,
        Color.green,
        Color.blue,
        Color.yellow,
        Color.white,
        new Color(1f, 0.5f, 0f), // Orange
        Color.magenta,
        Color.cyan
    };
    [Header("Android / Debug Helpers")]
    [Tooltip("If enabled and the new Input System isn't working on device, try legacy input as a fallback.")]
    [SerializeField] private bool enableLegacyInputFallback = false;

    [Header("UI Integration")]
    [SerializeField] private UIDocument[] uiDocumentsToBlockInput;
    [SerializeField] private string uiBlockingElementName = "LineDrawOptions";
    [SerializeField] private string uiBlockingStatusMessage = "UI interaction active - drawing paused";
    private readonly HashSet<int> uiPointerIdsBlocking = new HashSet<int>();
    private readonly HashSet<UIDocument> registeredUIDocuments = new HashSet<UIDocument>();
    private readonly Dictionary<UIDocument, VisualElement> uiBlockingElements = new Dictionary<UIDocument, VisualElement>();

    [SerializeField] private List<LineData> storedLines = new List<LineData>();
    private LineData currentLineData = null;

    [SerializeField] private float minPointDistance = 0.01f;
    
    private bool legacyFallbackLogged = false;
    private const float cameraToPlaneRatio = 8f / 9f;
    private const float minLineWidthValue = 0.0001f;

    public float CurrentLineWidth => lineWidth;
    public Color CurrentLineColor => lineColor;

    private bool pointerBlockedByUI = false;
    private bool currentLineStartedOverUI = false;
    private string lastPersistentStatus = string.Empty;

    void OnEnable()
    {
        // Check if we're in joiner mode (remote expert) vs creator mode (AR user)
        bool isJoinerMode = webRTCManager != null && !webRTCManager.isInitiator;
        
        if (isJoinerMode)
        {
            // Joiner mode: No AR needed, can draw immediately on video feed
            loading.SetActive(false);
            Drawable = true;
            UpdateStatus("Remote Expert Mode: Draw on video to annotate");
            if (enableDebugLogs) Debug.Log("[LineDrawing] Joiner mode - AR disabled, drawing on 2D video");
            
            // Disable all AR components for joiner
            var arPlaneManager = GetComponent<ARPlaneManager>();
            if (arPlaneManager != null) arPlaneManager.enabled = false;
            
            var arPointCloudManager = GetComponent<ARPointCloudManager>();
            if (arPointCloudManager != null) arPointCloudManager.enabled = false;
            
            var arAnchorManager = GetComponent<ARAnchorManager>();
            if (arAnchorManager != null) arAnchorManager.enabled = false;
            
            var arRaycastManager = GetComponent<ARRaycastManager>();
            if (arRaycastManager != null) arRaycastManager.enabled = false;
        }
        else
        {
            // Creator mode: Need AR planes and point clouds
            loading.SetActive(true);
            UpdateStatus("Searching for planes and point cloud...");
            if (enableDebugLogs) Debug.Log("[LineDrawing] Creator mode - AR enabled, waiting for planes");
            
            // Ensure AR components are enabled for creator
            var arPlaneManager = GetComponent<ARPlaneManager>();
            if (arPlaneManager != null) arPlaneManager.enabled = true;
            
            var arPointCloudManager = GetComponent<ARPointCloudManager>();
            if (arPointCloudManager != null) arPointCloudManager.enabled = true;
            
            var arAnchorManager = GetComponent<ARAnchorManager>();
            if (arAnchorManager != null) arAnchorManager.enabled = true;
            
            var arRaycastManager = GetComponent<ARRaycastManager>();
            if (arRaycastManager != null) arRaycastManager.enabled = true;
        }
        
        // Assign random color from available colors
        if (availableColors != null && availableColors.Length > 0)
        {
            lineColor = availableColors[UnityEngine.Random.Range(0, availableColors.Length)];
            if (enableDebugLogs) Debug.Log($"[LineDrawing] Assigned random color: {lineColor}");
        }
        
        // Only setup AR managers if in creator mode
        if (!isJoinerMode)
        {
            anchorManager = GetComponent<ARAnchorManager>();
            planeManager = GetComponent<ARPlaneManager>();
            if (planeManager != null)
                planeManager.trackablesChanged.AddListener(OnPlaneChanged);
            pointCloudManager = GetComponent<ARPointCloudManager>();
            if (pointCloudManager != null)
                pointCloudManager.trackablesChanged.AddListener(OnPointCloudsChanged);
            raycastManager = GetComponent<ARRaycastManager>();
        }

        EnsureEnhancedTouchSetup();
        RegisterUIDocumentListeners();

        if (webRTCManager != null)
        {
            webRTCManager.OnDrawingDataReceived += OnRemoteDrawingReceived;
            webRTCManager.OnConnectionEstablished += HandlePeerConnected;
            webRTCManager.OnConnectionClosed += HandlePeerDisconnected;
        }
    }

    void OnDisable()
    {
        if (EnhancedTouchSupport.enabled)
            EnhancedTouchSupport.Disable();
    #if UNITY_EDITOR
        TouchSimulation.Disable();
    #endif
        if (webRTCManager != null)
        {
            webRTCManager.OnDrawingDataReceived -= OnRemoteDrawingReceived;
            webRTCManager.OnConnectionEstablished -= HandlePeerConnected;
            webRTCManager.OnConnectionClosed -= HandlePeerDisconnected;
        }

        UnregisterUIDocumentListeners();
        uiPointerIdsBlocking.Clear();
        SetUIBlockingState(false);
    }
    public void OnPlaneChanged(ARTrackablesChangedEventArgs<ARPlane> changes)
    {
        if (changes.added != null && changes.added.Count > 0)
        {
            leastOnePlaneDetected = true;
            if (enableDebugLogs) Debug.Log($"[LineDrawing] Plane(s) added: {changes.added.Count}");
            UpdateStatus("Plane detected");

            foreach (var plane in changes.added)
            {
                if (plane != null && !planes.Contains(plane))
                {
                    planes.Add(plane);
                }

                if (plane != null)
                {
                    Pose anchorPose = new Pose(plane.transform.position, plane.transform.rotation);
                    TryAnchorPoseAsync(anchorPose, "plane");
                }
            }
            if (leastOnePlaneDetected && leastOnePointCloudDetected)
            {
                loading.SetActive(false);
                UpdateStatus("Ready to draw: plane and point cloud detected");
            }
        }
        else if (changes.removed != null && changes.removed.Count > 0)
        {
            if (enableDebugLogs) Debug.Log($"[LineDrawing] Plane(s) removed: {changes.removed.Count}");
            UpdateStatus("Plane removed (anchored copies persist)");
        }
    }
    void OnPointCloudsChanged(ARTrackablesChangedEventArgs<ARPointCloud> changes)
    {
        if (changes.added != null && changes.added.Count > 0)
        {
            leastOnePointCloudDetected = true;
            if (enableDebugLogs) Debug.Log($"[LineDrawing] Point cloud(s) added: {changes.added.Count}");
            UpdateStatus("Point cloud detected");

            foreach (var pointCloud in changes.added)
            {
                if (pointCloud != null && !pointClouds.Contains(pointCloud))
                {
                    pointClouds.Add(pointCloud);
                }

                if (pointCloud != null)
                {
                    Pose anchorPose = new Pose(pointCloud.transform.position, pointCloud.transform.rotation);
                    TryAnchorPoseAsync(anchorPose, "point cloud");
                }
            }
            if (leastOnePlaneDetected && leastOnePointCloudDetected)
            {
                loading.SetActive(false);
                Drawable = true;
                UpdateStatus("Ready to draw: plane and point cloud detected");
            }
        }
        else if (changes.removed != null && changes.removed.Count > 0)
        {
            if (enableDebugLogs) Debug.Log($"[LineDrawing] Point cloud(s) removed: {changes.removed.Count}");
            UpdateStatus("Point cloud removed (anchored copies persist)");
        }
    }

    void UpdateStatus(string message, bool persist = true)
    {
        if (enableDebugLogs) Debug.Log($"[LineDrawing] Status: {message}");
        if (statusText != null)
            statusText.text = message;
        if (persist)
            lastPersistentStatus = message;
    }

    void RestorePersistentStatus()
    {
        if (!string.IsNullOrEmpty(lastPersistentStatus))
        {
            UpdateStatus(lastPersistentStatus, false);
        }
    }

    // Call from runtime to log diagnostics (useful on device via adb logcat)
    public void LogDiagnostics()
    {
        Debug.Log($"[LineDrawing] Diagnostics: platform={Application.platform}, isEditor={Application.isEditor}");
        Debug.Log($"[LineDrawing] Camera.main is {(Camera.main == null ? "NULL" : "Present")}");
        Debug.Log($"[LineDrawing] Drawable={Drawable}, enableLegacyInputFallback={enableLegacyInputFallback}");
        Debug.Log($"[LineDrawing] storedLines count={storedLines?.Count}");
    }

    public void SetLineWidth(float newWidth)
    {
        float clampedWidth = Mathf.Max(minLineWidthValue, newWidth);
        if (Mathf.Approximately(lineWidth, clampedWidth))
            return;

        lineWidth = clampedWidth;

        if (presentLine != null)
        {
            presentLine.startWidth = clampedWidth;
            presentLine.endWidth = clampedWidth;
        }

        if (currentLineData != null)
            currentLineData.width = clampedWidth;

        if (enableDebugLogs) Debug.Log($"[LineDrawing] Line width set to {clampedWidth}");
    }

    public void SetLineColor(Color newColor)
    {
        if (lineColor == newColor)
            return;

        lineColor = newColor;

        if (presentLine != null)
            ApplyColorToRenderer(presentLine, lineColor);

        if (currentLineData != null)
            currentLineData.color = lineColor;

        if (enableDebugLogs) Debug.Log($"[LineDrawing] Line color set to {newColor}");
    }


    void RenderNewLine()
    {
        GameObject lineObj = new GameObject("Line");
        presentLine = lineObj.AddComponent<LineRenderer>();
        // Configure renderer appearance
        if (lineMaterial != null)
        {
            presentLine.material = lineMaterial;
        }
        else
        {
            // Fallback to an unlit color shader which tends to be visible across mobile GPUs
            var fallback = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            presentLine.material = new Material(fallback);
        }

        ApplyColorToRenderer(presentLine, lineColor);
        presentLine.startWidth = lineWidth;
        presentLine.endWidth = lineWidth;
        presentLine.useWorldSpace = true;
        presentLine.positionCount = 0;
        presentLine.numCapVertices = 4;
        lines.Add(presentLine);
        currentLineData = new LineData { color = lineColor, width = lineWidth };
        currentLineId = Guid.NewGuid().ToString();
        lineIdToRenderer[currentLineId] = presentLine;
        lineIdToData[currentLineId] = currentLineData;
        lineIdToOwner[currentLineId] = LocalUserId;
        lineDrawOrder.Add(currentLineId);
        if (enableDebugLogs) Debug.Log($"[LineDrawing] Created LineRenderer (shader={presentLine.material.shader.name}, width={lineWidth})");

        // Send Start Event
        if (webRTCManager != null)
        {
            // For the start event, we don't have the first point yet, but we can send color/width.
            // The first point will be sent immediately after via AddPointToCurrentLine -> Point event.
            // Actually, let's wait for the first point to send the Start event? 
            // No, let's send a Start event with zero pos or just rely on the first point?
            // The protocol says Start has pos. Let's handle it in AddPointToCurrentLine if it's the first point.
            // Or better: Send Start here with a dummy pos, or modify protocol.
            // Let's just send Start here with zero, and the receiver will ignore the pos for Start or use it?
            // My helper CreateStart takes a pos.
            // Let's change the flow: RenderNewLine is called, then AddPoint.
            // We can send Start in AddPoint if it's the first point.
        }
    }

    void AddPointToCurrentLine(Vector3 worldPoint)
    {
        if (presentLine == null || currentLineData == null || string.IsNullOrEmpty(currentLineId))
            return;

        if (enableDebugLogs) Debug.Log($"[LineDrawing] AddPoint: {worldPoint}");

        // Avoid adding points that are too close
        if (currentLineData.points.Count > 0)
        {
            Vector3 last = currentLineData.points[currentLineData.points.Count - 1];
            if (Vector3.Distance(last, worldPoint) < minPointDistance)
                return;
        }

        currentLineData.points.Add(worldPoint);
        presentLine.positionCount = currentLineData.points.Count;
        presentLine.SetPosition(currentLineData.points.Count - 1, worldPoint);
        if (enableDebugLogs) Debug.Log($"[LineDrawing] currentLine points={currentLineData.points.Count}");

        // Send drawing events via WebRTC data channel
        if (webRTCManager != null)
        {
            // NOTE: worldPoint for joiner is not actually a world point, it's a screen-space point
            // stored in Vector3 for convenience. The isScreenCoordinate flag tells creator how to interpret it.
            if (currentLineData.points.Count == 1)
            {
                if (enableDebugLogs) Debug.Log($"[LineDrawing] Sending START event for line {currentLineId}");
                webRTCManager.SendDrawingEvent(DrawingEvent.CreateStart(worldPoint, lineColor, lineWidth, currentLineId, LocalUserId));
            }
            else
            {
                webRTCManager.SendDrawingEvent(DrawingEvent.CreatePoint(worldPoint, currentLineId, LocalUserId));
            }
        }
        else if (enableDebugLogs)
        {
            Debug.LogWarning("[LineDrawing] WebRTCManager is null, cannot send drawing events");
        }
    }
    
    // New method specifically for joiner's screen coordinate drawing
    void AddScreenPointToCurrentLine(Vector2 screenPos)
    {
        if (presentLine == null || currentLineData == null || string.IsNullOrEmpty(currentLineId))
            return;

        if (enableDebugLogs) Debug.Log($"[LineDrawing] AddScreenPoint: {screenPos}");

        // Convert screen position to normalized coordinates (0-1 range)
        Vector2 normalizedPos = new Vector2(
            screenPos.x / Screen.width,
            screenPos.y / Screen.height
        );
        
        // For joiner, store screen coords as Vector3 for local rendering
        Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, distanceFromCamera);
        
        // Avoid adding points that are too close
        if (currentLineData.points.Count > 0)
        {
            Vector3 last = currentLineData.points[currentLineData.points.Count - 1];
            if (Vector3.Distance(last, screenPoint) < minPointDistance * 100f) // Scale threshold for screen space
                return;
        }

        currentLineData.points.Add(screenPoint);
        presentLine.positionCount = currentLineData.points.Count;
        presentLine.SetPosition(currentLineData.points.Count - 1, screenPoint);
        if (enableDebugLogs) Debug.Log($"[LineDrawing] currentLine screen points={currentLineData.points.Count}");

        // Send normalized screen coordinates to creator
        if (webRTCManager != null)
        {
            if (currentLineData.points.Count == 1)
            {
                if (enableDebugLogs) Debug.Log($"[LineDrawing] Sending SCREEN START event for line {currentLineId}");
                webRTCManager.SendDrawingEvent(DrawingEvent.CreateStartScreen(normalizedPos, lineColor, lineWidth, currentLineId, LocalUserId));
            }
            else
            {
                webRTCManager.SendDrawingEvent(DrawingEvent.CreatePointScreen(normalizedPos, currentLineId, LocalUserId));
            }
        }
    }

    void FinishCurrentLine()
    {
        if (currentLineData != null && currentLineData.points.Count > 0)
        {
            if (!storedLines.Contains(currentLineData))
                storedLines.Add(currentLineData);
            if (webRTCManager != null && !string.IsNullOrEmpty(currentLineId))
            {
                webRTCManager.SendDrawingEvent(DrawingEvent.CreateTypeOnly("end", currentLineId, LocalUserId));
            }
        }
        else if (!string.IsNullOrEmpty(currentLineId))
        {
            // Remove empty line placeholders
            TryRemoveLine(currentLineId, true, false);
        }

        if (enableDebugLogs) Debug.Log($"[LineDrawing] FinishCurrentLine. storedLines now={storedLines.Count}");
        currentLineData = null;
        presentLine = null;
        currentLineId = null;
    }

    public void ClearAllLines()
    {
        ClearLinesInternal(true);
    }

    public void UndoLastLine()
    {
        string localUserId = LocalUserId;
        string lineIdToUndo = FindLastLineIdOwnedBy(localUserId);
        if (string.IsNullOrEmpty(lineIdToUndo))
        {
            if (enableDebugLogs) Debug.Log("[LineDrawing] No line to undo for current user");
            return;
        }

        if (TryRemoveLine(lineIdToUndo, true, true) && webRTCManager != null)
        {
            webRTCManager.SendDrawingEvent(DrawingEvent.CreateTypeOnly("undo", lineIdToUndo, localUserId));
        }
    }

    void Update()
    {
        DetectUIBlockingFromPointerState();
        
        // Joiner can draw immediately, Creator needs AR tracking
        bool canDrawNow = IsJoiner || (leastOnePlaneDetected && leastOnePointCloudDetected);

        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            if (canDrawNow)
                DrawOnTouch();
            legacyFallbackLogged = false;
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse != null && canDrawNow)
            {
                if (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame)
                    DrawOnMouse();
            }
        }

        // Process any remote drawing events that were posted from non-main threads.
        while (true)
        {
            DrawingEvent remoteEvt = null;
            lock (remoteQueueLock)
            {
                if (remoteEventQueue.Count == 0)
                    break;
                remoteEvt = remoteEventQueue.Dequeue();
            }
            if (remoteEvt != null)
                HandleRemoteEvent(remoteEvt);
        }
    }
    void DrawOnTouch()
    {
        // Prefer EnhancedTouch (new Input System) when available
        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            var touch = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0];
            var phase = touch.phase;
            Vector2 screenPos = touch.screenPosition;

            switch (phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    currentLineStartedOverUI = ShouldBlockDrawingAt(screenPos);
                    if (currentLineStartedOverUI)
                        return;
                    RenderNewLine();
                    if (IsJoiner)
                    {
                        // Joiner: Use screen coordinates directly
                        AddScreenPointToCurrentLine(screenPos);
                    }
                    else
                    {
                        // Creator: Convert to AR world point
                        Vector3 worldPoint = GetTouchWorldPoint(screenPos);
                        AddPointToCurrentLine(worldPoint);
                        AnchorCurrentLineAt(worldPoint);
                    }
                    break;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    if (currentLineStartedOverUI || ShouldBlockDrawingAt(screenPos))
                        return;
                    if (IsJoiner)
                    {
                        AddScreenPointToCurrentLine(screenPos);
                    }
                    else
                    {
                        AddPointToCurrentLine(GetTouchWorldPoint(screenPos));
                    }
                    break;
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                case UnityEngine.InputSystem.TouchPhase.Ended:
                    if (currentLineStartedOverUI)
                    {
                        // Cancel the line if it started over UI
                        if (presentLine != null)
                        {
                            if (!string.IsNullOrEmpty(currentLineId))
                                TryRemoveLine(currentLineId, true, false);
                            presentLine = null;
                            currentLineData = null;
                            currentLineId = null;
                        }
                        currentLineStartedOverUI = false;
                    }
                    else
                    {
                        FinishCurrentLine();
                    }
                    break;
            }
            return;
        }

        // Fallback to legacy Input.touches when enabled (useful on some Android setups)
        if (enableLegacyInputFallback && Input.touchCount > 0)
        {
            if (!legacyFallbackLogged)
            {
                legacyFallbackLogged = true;
                if (enableDebugLogs) Debug.Log("[LineDrawing] Using legacy Input.touches fallback path");
            }
            var lt = Input.GetTouch(0);
            switch (lt.phase)
            {
                case UnityEngine.TouchPhase.Began:
                    currentLineStartedOverUI = ShouldBlockDrawingAt(lt.position);
                    if (currentLineStartedOverUI)
                        return;
                    RenderNewLine();
                    {
                        Vector3 worldPoint = GetTouchWorldPoint(lt.position);
                        AddPointToCurrentLine(worldPoint);
                        AnchorCurrentLineAt(worldPoint);
                    }
                    break;
                case UnityEngine.TouchPhase.Moved:
                case UnityEngine.TouchPhase.Stationary:
                    if (currentLineStartedOverUI || ShouldBlockDrawingAt(lt.position))
                        return;
                    AddPointToCurrentLine(GetTouchWorldPoint(lt.position));
                    break;
                case UnityEngine.TouchPhase.Canceled:
                case UnityEngine.TouchPhase.Ended:
                    if (currentLineStartedOverUI)
                    {
                        // Cancel the line if it started over UI
                        if (presentLine != null)
                        {
                            if (!string.IsNullOrEmpty(currentLineId))
                                TryRemoveLine(currentLineId, true, false);
                            presentLine = null;
                            currentLineData = null;
                            currentLineId = null;
                        }
                        currentLineStartedOverUI = false;
                    }
                    else
                    {
                        FinishCurrentLine();
                    }
                    break;
            }
        }
    }

    Vector3 GetTouchWorldPoint(Vector2 screenPos)
    {
        // If we have an ARRaycastManager, try to raycast against detected planes first
        if (raycastManager != null)
        {
            s_ArRaycastHits.Clear();
            if (raycastManager.Raycast(screenPos, s_ArRaycastHits, TrackableType.Planes) && s_ArRaycastHits.Count > 0)
            {
                return ProjectPointAlongRay(s_ArRaycastHits[0].pose.position);
            }

            s_ArRaycastHits.Clear();
            if (raycastManager.Raycast(screenPos, s_ArRaycastHits, TrackableType.FeaturePoint) && s_ArRaycastHits.Count > 0)
            {
                return ProjectPointAlongRay(s_ArRaycastHits[0].pose.position);
            }
        }

        // Fallback: project a point in front of Camera.main
        if (Camera.main != null)
        {
            Vector3 point = new Vector3(screenPos.x, screenPos.y, distanceFromCamera);
            return Camera.main.ScreenToWorldPoint(point);
        }

        return Vector3.zero;
    }

    Vector3 ProjectPointAlongRay(Vector3 hitPosition)
    {
        if (Camera.main == null)
            return hitPosition;

        Vector3 cameraPos = Camera.main.transform.position;
        Vector3 direction = hitPosition - cameraPos;
        float distanceToPlane = direction.magnitude;
        if (distanceToPlane <= Mathf.Epsilon)
            return hitPosition;

        direction.Normalize();
        float drawDistance = distanceToPlane * cameraToPlaneRatio;
        return cameraPos + direction * drawDistance;
    }

    void EnsureEnhancedTouchSetup()
    {
        if (!EnhancedTouchSupport.enabled)
        {
            EnhancedTouchSupport.Enable();
            if (enableDebugLogs) Debug.Log("[LineDrawing] EnhancedTouchSupport enabled");
        }
#if UNITY_EDITOR
    TouchSimulation.Enable();
#endif
    }

    void ApplyColorToRenderer(LineRenderer renderer, Color color)
    {
        if (renderer == null)
            return;

        try { renderer.material.color = color; } catch { }
        renderer.startColor = color;
        renderer.endColor = color;
    }

    void DrawOnMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null)
            return;

        Vector2 mousePos = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            currentLineStartedOverUI = ShouldBlockDrawingAt(mousePos);
            if (currentLineStartedOverUI)
                return;
            RenderNewLine();
            Vector3 point = new Vector3(mousePos.x, mousePos.y, distanceFromCamera);
            Vector3 worldPoint = Camera.main != null ? Camera.main.ScreenToWorldPoint(point) : Vector3.zero;
            AddPointToCurrentLine(worldPoint);
            AnchorCurrentLineAt(worldPoint);
        }
        else if (mouse.leftButton.isPressed)
        {
            if (currentLineStartedOverUI || ShouldBlockDrawingAt(mousePos))
                return;
            Vector3 movePoint = new Vector3(mousePos.x, mousePos.y, distanceFromCamera);
            Vector3 worldMovePoint = Camera.main != null ? Camera.main.ScreenToWorldPoint(movePoint) : Vector3.zero;
            AddPointToCurrentLine(worldMovePoint);
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (currentLineStartedOverUI)
            {
                // Cancel the line if it started over UI
                if (presentLine != null)
                {
                    if (!string.IsNullOrEmpty(currentLineId))
                        TryRemoveLine(currentLineId, true, false);
                    presentLine = null;
                    currentLineData = null;
                    currentLineId = null;
                }
                currentLineStartedOverUI = false;
            }
            else
            {
                FinishCurrentLine();
            }
        }
    }

    void AnchorCurrentLineAt(Vector3 worldPoint)
    {
        var lineToAnchor = presentLine;
        if (lineToAnchor == null || anchoredLines.Contains(lineToAnchor))
            return;

        Pose anchorPose = new Pose(worldPoint, Quaternion.identity);
        TryAnchorPoseAsync(anchorPose, "line", anchor =>
        {
            if (lineToAnchor == null)
                return;

            lineToAnchor.transform.SetParent(anchor.transform, true);
            anchoredLines.Add(lineToAnchor);
        });
    }

    async void TryAnchorPoseAsync(Pose pose, string debugContext, Action<ARAnchor> onAnchored = null)
    {
        if (anchorManager == null)
        {
            if (enableDebugLogs) Debug.LogWarning("[LineDrawing] Cannot anchor without ARAnchorManager");
            return;
        }

        try
        {
            var result = await anchorManager.TryAddAnchorAsync(pose);
            if (result.status.IsSuccess() && result.value != null)
            {
                anchors.Add(result.value);
                if (enableDebugLogs) Debug.Log($"[LineDrawing] Anchor added for {debugContext}");
                onAnchored?.Invoke(result.value);
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning($"[LineDrawing] Failed to anchor {debugContext}: {result.status}");
            }
        }
        catch (Exception ex)
        {
            if (enableDebugLogs) Debug.LogWarning($"[LineDrawing] Exception while anchoring {debugContext}: {ex.Message}");
        }
    }

    private void OnRemoteDrawingReceived(DrawingEvent evt)
    {
        lock (remoteQueueLock)
        {
            remoteEventQueue.Enqueue(evt);
        }
    }

    private void HandleRemoteEvent(DrawingEvent evt)
    {
        if (evt == null || string.IsNullOrEmpty(evt.type))
            return;

        switch (evt.type)
        {
            case "start":
                HandleRemoteStart(evt);
                break;
            case "point":
                HandleRemotePoint(evt);
                break;
            case "end":
                HandleRemoteEnd(evt);
                break;
            case "undo":
                HandleRemoteUndo(evt);
                break;
            case "clear":
                HandleRemoteClear();
                break;
        }
    }

    private void HandleRemoteStart(DrawingEvent evt)
    {
        string lineId = string.IsNullOrEmpty(evt.lineId) ? Guid.NewGuid().ToString() : evt.lineId;
        string ownerId = string.IsNullOrEmpty(evt.senderId) ? "remote" : evt.senderId;
        
        Vector3 startPos;
        if (evt.isScreenCoordinate)
        {
            // Joiner sent screen coordinates - convert to 3D AR position if we're the creator
            if (IsCreator)
            {
                Vector2 normalizedScreen = new Vector2(evt.screenX, evt.screenY);
                Vector2 screenPixels = new Vector2(
                    normalizedScreen.x * Screen.width,
                    normalizedScreen.y * Screen.height
                );
                startPos = GetTouchWorldPoint(screenPixels);
                if (enableDebugLogs) Debug.Log($"[LineDrawing] Converted joiner screen coords ({evt.screenX}, {evt.screenY}) to AR world pos {startPos}");
            }
            else
            {
                // Joiner receiving from another joiner - use as screen point
                startPos = new Vector3(evt.screenX * Screen.width, evt.screenY * Screen.height, distanceFromCamera);
            }
        }
        else
        {
            // Creator sent 3D world coordinates
            startPos = new Vector3(evt.x, evt.y, evt.z);
        }
        
        Color color = new Color(evt.r, evt.g, evt.b);
        float width = evt.width > 0 ? evt.width : lineWidth;

        TryRemoveLine(lineId, true, true);

        GameObject lineObj = new GameObject($"RemoteLine_{lineId}");
        var renderer = lineObj.AddComponent<LineRenderer>();
        if (lineMaterial != null)
        {
            renderer.material = lineMaterial;
        }
        else
        {
            var fallback = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(fallback);
        }

        ApplyColorToRenderer(renderer, color);
        renderer.startWidth = width;
        renderer.endWidth = width;
        renderer.useWorldSpace = true;
        renderer.positionCount = 1;
        renderer.SetPosition(0, startPos);
        renderer.numCapVertices = 4;

        lines.Add(renderer);

        var data = new LineData { color = color, width = width };
        data.points.Add(startPos);
        lineIdToRenderer[lineId] = renderer;
        lineIdToData[lineId] = data;
        lineIdToOwner[lineId] = ownerId;
        lineDrawOrder.Add(lineId);
    }

    private void HandleRemotePoint(DrawingEvent evt)
    {
        if (!TryGetLineState(evt.lineId, out var renderer, out var data))
            return;

        Vector3 pos;
        if (evt.isScreenCoordinate)
        {
            // Joiner sent screen coordinates - convert to 3D AR position if we're the creator
            if (IsCreator)
            {
                Vector2 normalizedScreen = new Vector2(evt.screenX, evt.screenY);
                Vector2 screenPixels = new Vector2(
                    normalizedScreen.x * Screen.width,
                    normalizedScreen.y * Screen.height
                );
                pos = GetTouchWorldPoint(screenPixels);
            }
            else
            {
                // Joiner receiving from another joiner - use as screen point
                pos = new Vector3(evt.screenX * Screen.width, evt.screenY * Screen.height, distanceFromCamera);
            }
        }
        else
        {
            // Creator sent 3D world coordinates
            pos = new Vector3(evt.x, evt.y, evt.z);
        }
        
        if (data.points.Count > 0)
        {
            Vector3 last = data.points[data.points.Count - 1];
            float threshold = evt.isScreenCoordinate && !IsCreator ? minPointDistance * 100f : minPointDistance;
            if (Vector3.Distance(last, pos) < threshold)
                return;
        }

        data.points.Add(pos);
        renderer.positionCount = data.points.Count;
        renderer.SetPosition(data.points.Count - 1, pos);
    }

    private void HandleRemoteEnd(DrawingEvent evt)
    {
        if (!TryGetLineState(evt.lineId, out _, out var data))
            return;

        if (data.points.Count > 0 && !storedLines.Contains(data))
        {
            storedLines.Add(data);
        }
    }

    private void HandleRemoteUndo(DrawingEvent evt)
    {
        string targetLineId = evt.lineId;
        if (string.IsNullOrEmpty(targetLineId) && !string.IsNullOrEmpty(evt.senderId))
        {
            targetLineId = FindLastLineIdOwnedBy(evt.senderId);
        }

        if (!string.IsNullOrEmpty(targetLineId))
        {
            TryRemoveLine(targetLineId, true, true);
        }
    }

    private void HandleRemoteClear()
    {
        ClearLinesInternal(false);
    }

    private void DetectUIBlockingFromPointerState()
    {
        RegisterUIDocumentListeners();
        Vector2 screenPos = Vector2.zero;
        bool pointerActive = false;

        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            screenPos = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0].screenPosition;
            pointerActive = true;
        }
        else if (Input.touchCount > 0)
        {
            screenPos = Input.GetTouch(0).position;
            pointerActive = true;
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame))
            {
                screenPos = mouse.position.ReadValue();
                pointerActive = true;
            }
        }

        if (pointerActive)
        {
            ShouldBlockDrawingAt(screenPos);
        }
        else if (pointerBlockedByUI)
        {
            SetUIBlockingState(false);
        }
    }

    private void RegisterUIDocumentListeners()
    {
        if (uiDocumentsToBlockInput == null || uiDocumentsToBlockInput.Length == 0)
            return;

        foreach (var doc in uiDocumentsToBlockInput)
        {
            if (doc == null || registeredUIDocuments.Contains(doc))
                continue;

            var root = doc.rootVisualElement;
            if (root == null)
                continue;

            var blocker = ResolveBlockingElement(root);
            if (blocker == null)
                continue;

            blocker.pickingMode = PickingMode.Position;

            blocker.RegisterCallback<PointerDownEvent>(OnUIPointerDown, TrickleDown.TrickleDown);
            blocker.RegisterCallback<PointerUpEvent>(OnUIPointerUp, TrickleDown.TrickleDown);
            blocker.RegisterCallback<PointerMoveEvent>(OnUIPointerMove, TrickleDown.TrickleDown);
            blocker.RegisterCallback<PointerCancelEvent>(OnUIPointerCancel, TrickleDown.TrickleDown);
            blocker.RegisterCallback<PointerCaptureOutEvent>(OnUIPointerCaptureOut, TrickleDown.TrickleDown);
            registeredUIDocuments.Add(doc);
            uiBlockingElements[doc] = blocker;
        }
    }

    private void UnregisterUIDocumentListeners()
    {
        if (registeredUIDocuments.Count == 0)
            return;

        foreach (var doc in registeredUIDocuments)
        {
            if (doc == null)
                continue;

            VisualElement blocker = null;
            if (!uiBlockingElements.TryGetValue(doc, out blocker) || blocker == null)
                continue;

            blocker.UnregisterCallback<PointerDownEvent>(OnUIPointerDown, TrickleDown.TrickleDown);
            blocker.UnregisterCallback<PointerUpEvent>(OnUIPointerUp, TrickleDown.TrickleDown);
            blocker.UnregisterCallback<PointerMoveEvent>(OnUIPointerMove, TrickleDown.TrickleDown);
            blocker.UnregisterCallback<PointerCancelEvent>(OnUIPointerCancel, TrickleDown.TrickleDown);
            blocker.UnregisterCallback<PointerCaptureOutEvent>(OnUIPointerCaptureOut, TrickleDown.TrickleDown);
        }

        registeredUIDocuments.Clear();
        uiBlockingElements.Clear();
    }

    private VisualElement ResolveBlockingElement(VisualElement root)
    {
        if (root == null)
            return null;

        if (string.IsNullOrEmpty(uiBlockingElementName))
            return root;

        return root.Q<VisualElement>(uiBlockingElementName) ?? root;
    }

    private void OnUIPointerDown(PointerDownEvent evt)
    {
        MarkPointerBlocked(evt.pointerId);
        evt.StopPropagation();
    }
    
    private void OnUIPointerMove(PointerMoveEvent evt)
    {
        if (uiPointerIdsBlocking.Contains(evt.pointerId))
        {
            evt.StopPropagation();
        }
    }
    
    private void OnUIPointerUp(PointerUpEvent evt)
    {
        ReleasePointerFromUI(evt.pointerId);
        evt.StopPropagation();
    }
    
    private void OnUIPointerCancel(PointerCancelEvent evt)
    {
        ReleasePointerFromUI(evt.pointerId);
        evt.StopPropagation();
    }
    
    private void OnUIPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        ReleasePointerFromUI(evt.pointerId);
        evt.StopPropagation();
    }

    private void MarkPointerBlocked(int pointerId)
    {
        if (pointerId == PointerId.invalidPointerId)
            return;

        if (uiPointerIdsBlocking.Add(pointerId))
            SetUIBlockingState(true);
    }

    private void ReleasePointerFromUI(int pointerId)
    {
        if (pointerId == PointerId.invalidPointerId)
            return;

        if (!uiPointerIdsBlocking.Remove(pointerId))
            return;

        if (uiPointerIdsBlocking.Count == 0)
            SetUIBlockingState(false);
    }

    private bool ShouldBlockDrawingAt(Vector2 screenPosition)
    {
        bool blocked = IsPointerOverUI(screenPosition);
        if (blocked && enableDebugLogs)
        {
            Debug.Log($"[LineDrawing] Drawing blocked at screen position {screenPosition} - pointer is over UI");
        }
        SetUIBlockingState(blocked);
        return blocked;
    }

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        return IsPointerOverUIDocuments(screenPosition);
    }

    private bool IsPointerOverUIDocuments(Vector2 screenPosition)
    {
        bool withinElement = IsPointerWithinBlockingElement(screenPosition);
        if (withinElement)
        {
            if (enableDebugLogs)
                Debug.Log($"[LineDrawing] Pointer at {screenPosition} is within blocking element bounds");
            return true;
        }

        if (uiPointerIdsBlocking.Count == 0)
            return false;

        int pointerId = ResolveActivePointerId();
        if (pointerId == PointerId.invalidPointerId)
            return false;

        bool isBlocked = uiPointerIdsBlocking.Contains(pointerId);
        if (isBlocked && enableDebugLogs)
            Debug.Log($"[LineDrawing] Pointer ID {pointerId} is tracked as blocking");
        return isBlocked;
    }

    private bool IsPointerWithinBlockingElement(Vector2 screenPosition)
    {
        if (uiBlockingElements.Count == 0)
        {
            if (enableDebugLogs)
            {
                // Debug.LogWarning("[LineDrawing] No UI blocking elements registered!");
            }
            return false;
        }

        foreach (var kvp in uiBlockingElements)
        {
            var doc = kvp.Key;
            var blocker = kvp.Value;
            if (doc == null || blocker == null)
                continue;

            var panel = doc.rootVisualElement?.panel;
            if (panel == null)
                continue;

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            if (blocker.ContainsPoint(panelPosition))
                return true;
        }

        return false;
    }

    private int ResolveActivePointerId()
    {
        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            return PointerId.touchPointerIdBase + UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0].touchId;
        }

        if (Input.touchCount > 0)
        {
            return PointerId.touchPointerIdBase + Input.GetTouch(0).fingerId;
        }

        var mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame))
        {
            return PointerId.mousePointerId;
        }

        return PointerId.invalidPointerId;
    }


    private void SetUIBlockingState(bool blocked)
    {
        if (pointerBlockedByUI == blocked)
            return;

        pointerBlockedByUI = blocked;
        if (blocked)
        {
            UpdateStatus(uiBlockingStatusMessage, false);
        }
        else
        {
            RestorePersistentStatus();
        }
    }

    private bool TryGetLineState(string lineId, out LineRenderer renderer, out LineData data)
    {
        renderer = null;
        data = null;
        if (string.IsNullOrEmpty(lineId))
            return false;

        lineIdToRenderer.TryGetValue(lineId, out renderer);
        lineIdToData.TryGetValue(lineId, out data);
        return renderer != null && data != null;
    }

    private bool TryRemoveLine(string lineId, bool destroyRenderer, bool removeFromStored)
    {
        if (string.IsNullOrEmpty(lineId))
            return false;

        if (lineIdToRenderer.TryGetValue(lineId, out var renderer))
        {
            if (destroyRenderer && renderer != null)
                Destroy(renderer.gameObject);
            lines.Remove(renderer);
            anchoredLines.Remove(renderer);
            lineIdToRenderer.Remove(lineId);
        }

        if (lineIdToData.TryGetValue(lineId, out var data))
        {
            if (removeFromStored)
                storedLines.Remove(data);
            lineIdToData.Remove(lineId);
        }

        lineIdToOwner.Remove(lineId);
        lineDrawOrder.Remove(lineId);

        if (currentLineId == lineId)
        {
            currentLineId = null;
            currentLineData = null;
            presentLine = null;
        }

        return true;
    }

    private string FindLastLineIdOwnedBy(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId))
            return null;

        for (int i = lineDrawOrder.Count - 1; i >= 0; i--)
        {
            var lineId = lineDrawOrder[i];
            if (lineIdToOwner.TryGetValue(lineId, out var owner) && owner == ownerId)
                return lineId;
        }

        return null;
    }

    private void ClearLinesInternal(bool broadcastEvent)
    {
        foreach (var lr in lines)
        {
            if (lr != null)
                Destroy(lr.gameObject);
        }

        lines.Clear();
        anchoredLines.Clear();
        storedLines.Clear();
        currentLineData = null;
        presentLine = null;
        currentLineId = null;

        lineIdToRenderer.Clear();
        lineIdToData.Clear();
        lineIdToOwner.Clear();
        lineDrawOrder.Clear();

        if (broadcastEvent && webRTCManager != null)
        {
            webRTCManager.SendDrawingEvent(DrawingEvent.CreateTypeOnly("clear", null, LocalUserId));
        }
    }

    private void HandlePeerConnected()
    {
        if (enableDebugLogs) Debug.Log("[LineDrawing] Peer connection established");
        UpdateStatus("Connected to peer. Waiting for data channel...", false);
        
        if (snapshotSentThisSession)
            return;

        // Start coroutine to wait for data channel to be ready
        StartCoroutine(WaitForDataChannelAndSendSnapshot());
    }

    private System.Collections.IEnumerator WaitForDataChannelAndSendSnapshot()
    {
        if (webRTCManager == null)
            yield break;

        // Wait up to 10 seconds for data channel to open
        float timeout = 10f;
        float elapsed = 0f;
        
        while (elapsed < timeout)
        {
            if (webRTCManager.IsDataChannelReady())
            {
                if (enableDebugLogs) Debug.Log("[LineDrawing] Data channel is ready. Sending stored lines to peer.");
                UpdateStatus("Data channel ready. Syncing drawings...", false);
                SendStoredLinesToPeer();
                snapshotSentThisSession = true;
                UpdateStatus("Drawing sync complete", false);
                yield break;
            }
            
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
        }
        
        Debug.LogWarning("[LineDrawing] Timeout waiting for data channel to open");
        UpdateStatus("Warning: Data channel not ready", false);
    }

    private void HandlePeerDisconnected()
    {
        snapshotSentThisSession = false;
    }

    private void SendStoredLinesToPeer()
    {
        if (webRTCManager == null || storedLines.Count == 0 || lineDrawOrder.Count == 0)
            return;

        webRTCManager.SendDrawingEvent(DrawingEvent.CreateTypeOnly("clear", null, LocalUserId));

        foreach (var lineId in lineDrawOrder)
        {
            if (!lineIdToData.TryGetValue(lineId, out var data) || data.points.Count == 0)
                continue;

            string ownerId = lineIdToOwner.TryGetValue(lineId, out var owner) ? owner : LocalUserId;
            Vector3 firstPoint = data.points[0];
            webRTCManager.SendDrawingEvent(DrawingEvent.CreateStart(firstPoint, data.color, data.width, lineId, ownerId));
            for (int i = 1; i < data.points.Count; i++)
            {
                webRTCManager.SendDrawingEvent(DrawingEvent.CreatePoint(data.points[i], lineId, ownerId));
            }
            webRTCManager.SendDrawingEvent(DrawingEvent.CreateTypeOnly("end", lineId, ownerId));
        }
    }
}
