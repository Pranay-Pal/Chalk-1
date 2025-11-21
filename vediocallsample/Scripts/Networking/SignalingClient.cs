using System;
using NativeWebSocket;
using UnityEngine;

namespace Videocall.Networking
{
    public class SignalingClient
    {
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;
        public event Action<string, string> OnRoomCreated; // roomId, clientId
        public event Action<string, string> OnRoomJoined;  // roomId, clientId
        public event Action<SignalEnvelope> OnSignal;      // generic signal payload

        public string RoomId => roomId;
        public string ClientId => clientId;
        public bool IsConnected => websocket != null && websocket.State == WebSocketState.Open;

        private readonly string serverUrl;
        private WebSocket websocket;
        private string roomId;
        private string clientId;

        public SignalingClient(string url)
        {
            serverUrl = url;
        }

        public void Connect()
        {
            if (websocket != null)
            {
                websocket.Close();
                websocket = null;
            }

            websocket = new WebSocket(serverUrl);
            websocket.OnOpen += () => OnConnected?.Invoke();
            websocket.OnError += error => OnError?.Invoke(error);
            websocket.OnClose += code => OnDisconnected?.Invoke();
            websocket.OnMessage += bytes => HandleServerMessage(System.Text.Encoding.UTF8.GetString(bytes));
            websocket.Connect();
        }

        public void Disconnect()
        {
            websocket?.Close();
        }

        public void Tick()
        {
            websocket?.DispatchMessageQueue();
        }

        public void RequestRoomCreation()
        {
            if (!IsConnected)
            {
                OnError?.Invoke("WebSocket is not connected.");
                return;
            }
            websocket.SendText("{ \"type\": \"create\" }");
        }

        public void RequestRoomJoin(string targetRoomId)
        {
            if (!IsConnected)
            {
                OnError?.Invoke("WebSocket is not connected.");
                return;
            }

            if (string.IsNullOrWhiteSpace(targetRoomId))
            {
                OnError?.Invoke("Room ID cannot be empty.");
                return;
            }

            string payload = $"{{ \"type\": \"join\", \"roomId\": \"{targetRoomId}\" }}";
            websocket.SendText(payload);
        }

        public void SendSignal(string payloadJson)
        {
            if (!IsConnected)
            {
                OnError?.Invoke("WebSocket is not connected.");
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                OnError?.Invoke("You need to be inside a room before sending signals.");
                return;
            }

            var envelope = new OutgoingSignal { type = "signal", payload = payloadJson };
            websocket.SendText(JsonUtility.ToJson(envelope));
        }

        private void HandleServerMessage(string rawMessage)
        {
            if (string.IsNullOrEmpty(rawMessage))
            {
                return;
            }

            try
            {
                var normalized = NormalizePayloadField(rawMessage);
                var message = JsonUtility.FromJson<ServerMessage>(normalized);
                if (message == null || string.IsNullOrEmpty(message.type))
                {
                    OnError?.Invoke($"Unrecognized message: {rawMessage}");
                    return;
                }

                switch (message.type)
                {
                    case "room_created":
                        roomId = message.roomId;
                        clientId = message.myId;
                        OnRoomCreated?.Invoke(roomId, clientId);
                        break;
                    case "room_joined":
                        roomId = message.roomId;
                        clientId = message.myId;
                        OnRoomJoined?.Invoke(roomId, clientId);
                        break;
                    case "signal":
                        if (string.IsNullOrEmpty(message.payload))
                        {
                            Debug.LogWarning($"[SignalingClient] Received signal without payload: {rawMessage}");
                            return;
                        }

                        var signal = new SignalEnvelope
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
            catch (Exception exception)
            {
                OnError?.Invoke($"Failed to parse server message: {exception.Message}");
            }
        }

        private static string NormalizePayloadField(string rawMessage)
        {
            if (string.IsNullOrEmpty(rawMessage))
            {
                return rawMessage;
            }

            int payloadNameIndex = rawMessage.IndexOf("\"payload\"", StringComparison.Ordinal);
            if (payloadNameIndex < 0)
            {
                return rawMessage;
            }

            int colonIndex = rawMessage.IndexOf(':', payloadNameIndex);
            if (colonIndex < 0)
            {
                return rawMessage;
            }

            int valueStart = SkipWhitespace(rawMessage, colonIndex + 1);
            if (valueStart >= rawMessage.Length)
            {
                return rawMessage;
            }

            char leadingChar = rawMessage[valueStart];
            if (leadingChar == '"')
            {
                return rawMessage; // already a string payload
            }

            if (leadingChar != '{' && leadingChar != '[')
            {
                return rawMessage;
            }

            int valueEnd = FindPayloadBlockEnd(rawMessage, valueStart);
            if (valueEnd < 0)
            {
                return rawMessage;
            }

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
            {
                return -1;
            }

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
                {
                    continue;
                }

                if (current == open)
                {
                    depth++;
                }
                else if (current == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
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
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
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
}
