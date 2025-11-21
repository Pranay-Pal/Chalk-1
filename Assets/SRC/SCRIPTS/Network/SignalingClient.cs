using System;
using System.Threading;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

/// <summary>
/// Wraps the WebSocket-based signaling protocol shared between Chalk and the sample project.
/// Responsible for connecting, creating/joining rooms, and forwarding opaque signal payloads.
/// </summary>
public class SignalingClient : IDisposable
{
    public event Action OnConnected;
    public event Action<WebSocketCloseCode> OnDisconnected;
    public event Action<string> OnError;
    public event Action<string, string> OnRoomCreated; // roomId, clientId
    public event Action<string, string> OnRoomJoined;  // roomId, clientId
    public event Action<SignalEnvelope> OnSignal;      // senderId, payload
    public event Action<int, float> OnReconnectScheduled;
    public event Action OnReconnectCancelled;
    public event Action OnReconnectFailed;

    public string RoomId { get; private set; }
    public string ClientId { get; private set; }
    public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;
    public bool AutoReconnect { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 5;
    public float ReconnectBaseDelaySeconds { get; set; } = 1.5f;
    public float ReconnectMaxDelaySeconds { get; set; } = 15f;

    private readonly string serverUrl;
    private WebSocket webSocket;
    private CancellationTokenSource reconnectCts;
    private bool manualDisconnect;
    private bool isConnecting;
    private int reconnectAttempts;

    public SignalingClient(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("Server URL cannot be null or empty", nameof(serverUrl));

        this.serverUrl = serverUrl;
    }

    public async void Connect()
    {
        if (isConnecting)
        {
            if (Application.isEditor)
                Debug.Log("[SignalingClient] Connect called while another connection attempt is in progress.");
            return;
        }

        if (webSocket != null)
        {
            try
            {
                await webSocket.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SignalingClient] Failed to close previous socket: {ex.Message}");
            }
            webSocket = null;
        }

        CancelPendingReconnects();
        manualDisconnect = false;
        isConnecting = true;
        webSocket = new WebSocket(serverUrl);
        webSocket.OnOpen += () =>
        {
            reconnectAttempts = 0;
            isConnecting = false;
            manualDisconnect = false;
            OnConnected?.Invoke();
        };
        webSocket.OnError += error =>
        {
            OnError?.Invoke(error);
        };
        webSocket.OnClose += code => HandleSocketClosed(code);
        webSocket.OnMessage += bytes => HandleServerMessage(System.Text.Encoding.UTF8.GetString(bytes));

        try
        {
            await webSocket.Connect();
        }
        catch (Exception ex)
        {
            isConnecting = false;
            OnError?.Invoke($"Failed to connect to signaling server: {ex.Message}");
            ScheduleReconnect();
        }
    }

    public void Disconnect()
    {
        manualDisconnect = true;
        CancelPendingReconnects();
        webSocket?.Close();
        webSocket = null;
        RoomId = null;
        ClientId = null;
        reconnectAttempts = 0;
        isConnecting = false;
    }

    public void Tick()
    {
        webSocket?.DispatchMessageQueue();
    }

    public void CreateRoom()
    {
        if (!IsConnected)
        {
            OnError?.Invoke("WebSocket is not connected.");
            return;
        }
        webSocket.SendText("{ \"type\": \"create\" }");
    }

    public void JoinRoom(string roomId)
    {
        if (!IsConnected)
        {
            OnError?.Invoke("WebSocket is not connected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            OnError?.Invoke("Room ID cannot be empty.");
            return;
        }

        string payload = $"{{ \"type\": \"join\", \"roomId\": \"{roomId}\" }}";
        webSocket.SendText(payload);
    }

    public void SendSignal(string payloadJson)
    {
        if (!IsConnected)
        {
            OnError?.Invoke("WebSocket is not connected.");
            return;
        }

        if (string.IsNullOrEmpty(RoomId))
        {
            OnError?.Invoke("You must join or create a room before sending signals.");
            return;
        }

        var envelope = new OutgoingSignal
        {
            type = "signal",
            payload = payloadJson
        };
        webSocket.SendText(JsonUtility.ToJson(envelope));
    }

    private void HandleServerMessage(string rawMessage)
    {
        if (string.IsNullOrEmpty(rawMessage))
            return;

        string normalized = NormalizePayloadField(rawMessage);
        if (!JsonValidationUtility.TryParse(normalized, out ServerMessage message, out string parseError) || message == null)
        {
            OnError?.Invoke(parseError ?? $"Failed to parse server message: {rawMessage}");
            return;
        }

        if (string.IsNullOrEmpty(message.type))
        {
            OnError?.Invoke($"Server message missing type: {rawMessage}");
            return;
        }

        switch (message.type)
        {
            case "room_created":
                RoomId = message.roomId;
                ClientId = message.myId;
                OnRoomCreated?.Invoke(RoomId, ClientId);
                break;
            case "room_joined":
                RoomId = message.roomId;
                ClientId = message.myId;
                OnRoomJoined?.Invoke(RoomId, ClientId);
                break;
            case "signal":
                if (string.IsNullOrEmpty(message.payload))
                {
                    Debug.LogWarning($"[SignalingClient] Received signal without payload: {rawMessage}");
                    return;
                }
                SignalEnvelope signal = new SignalEnvelope
                {
                    senderId = message.senderId,
                    payload = message.payload
                };
                OnSignal?.Invoke(signal);
                break;
            case "error":
                OnError?.Invoke(message.message ?? "Unknown server error");
                break;
            default:
                Debug.LogWarning($"[SignalingClient] Unhandled message: {rawMessage}");
                break;
        }
    }

    private void HandleSocketClosed(WebSocketCloseCode code)
    {
        isConnecting = false;
        webSocket = null;
        OnDisconnected?.Invoke(code);

        if (manualDisconnect || !AutoReconnect)
        {
            CancelPendingReconnects();
            return;
        }

        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (manualDisconnect || isConnecting || !AutoReconnect)
            return;

        if (MaxReconnectAttempts > 0 && reconnectAttempts >= MaxReconnectAttempts)
        {
            OnReconnectFailed?.Invoke();
            return;
        }

        reconnectAttempts++;
        float delaySeconds = (float)Math.Min(ReconnectMaxDelaySeconds,
            ReconnectBaseDelaySeconds * Math.Pow(2, Math.Max(0, reconnectAttempts - 1)));

        CancelPendingReconnects();
        reconnectCts = new CancellationTokenSource();
        OnReconnectScheduled?.Invoke(reconnectAttempts, delaySeconds);
        _ = AttemptReconnectAfterDelay(delaySeconds, reconnectCts.Token);
    }

    private async Task AttemptReconnectAfterDelay(float delaySeconds, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || manualDisconnect)
            return;

        Connect();
    }

    private void CancelPendingReconnects()
    {
        if (reconnectCts != null)
        {
            reconnectCts.Cancel();
            reconnectCts.Dispose();
            reconnectCts = null;
            OnReconnectCancelled?.Invoke();
        }
    }

    private static string NormalizePayloadField(string rawMessage)
    {
        if (string.IsNullOrEmpty(rawMessage))
            return rawMessage;

        int payloadNameIndex = rawMessage.IndexOf("\"payload\"", StringComparison.Ordinal);
        if (payloadNameIndex < 0)
            return rawMessage;

        int colonIndex = rawMessage.IndexOf(':', payloadNameIndex);
        if (colonIndex < 0)
            return rawMessage;

        int valueStart = SkipWhitespace(rawMessage, colonIndex + 1);
        if (valueStart >= rawMessage.Length)
            return rawMessage;

        char leadingChar = rawMessage[valueStart];
        if (leadingChar == '"')
            return rawMessage; // already a string payload

        if (leadingChar != '{' && leadingChar != '[')
            return rawMessage;

        int valueEnd = FindPayloadBlockEnd(rawMessage, valueStart);
        if (valueEnd < 0)
            return rawMessage;

        string payloadJson = rawMessage.Substring(valueStart, valueEnd - valueStart + 1);
        string escapedPayload = EscapeJsonString(payloadJson);
        return rawMessage.Substring(0, valueStart) + "\"" + escapedPayload + "\"" + rawMessage.Substring(valueEnd + 1);
    }

    private static int SkipWhitespace(string text, int startIndex)
    {
        int index = startIndex;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
        return index;
    }

    private static int FindPayloadBlockEnd(string text, int startIndex)
    {
        if (startIndex >= text.Length)
            return -1;

        char open = text[startIndex];
        char close = open == '{' ? '}' : ']';
        int depth = 0;
        bool inString = false;

        for (int i = startIndex; i < text.Length; i++)
        {
            char current = text[i];

            if (current == '"' && !IsEscaped(text, i))
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (current == open)
            {
                depth++;
            }
            else if (current == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static bool IsEscaped(string text, int index)
    {
        int backslashCount = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
        {
            backslashCount++;
        }
        return (backslashCount & 1) == 1;
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }
        return builder.ToString();
    }

    public void Dispose()
    {
        Disconnect();
    }

    [Serializable]
    private class ServerMessage
    {
        public string type;
        public string roomId;
        public string myId;
        public string senderId;
        public string payload;
        public string message;
    }

    [Serializable]
    private class OutgoingSignal
    {
        public string type;
        public string payload;
    }
}

[Serializable]
public class SignalEnvelope
{
    public string senderId;
    public string payload;
}
