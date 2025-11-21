using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace Videocall.WebRTC
{
    public class RtcChatPeer : MonoBehaviour
    {
        public event Action<string> OnChatMessageReceived;
        public event Action<string> OnStatusMessage;
        public event Action OnChannelOpen;
        public event Action OnChannelClosed;
        public event Action<string> OnSignalReady;
        public event Action<Texture> OnLocalVideoTexture;
        public event Action<Texture> OnRemoteVideoTexture;

        [Header("Media Capture Settings")]
        [SerializeField] private bool autoStartCamera = true;
        [SerializeField] private bool autoStartMicrophone = true;
        [SerializeField] private int webcamWidth = 1280;
        [SerializeField] private int webcamHeight = 720;
        [SerializeField] private int webcamFramerate = 24;
        [SerializeField] private int microphoneSampleRate = 48000;
        [SerializeField] private string preferredMicrophone = string.Empty;

        [Header("Remote Audio Output")]
        [SerializeField] private AudioSource remoteAudioOutput;

        public bool HasOpenChannel => dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open;
        public bool IsCameraMuted => localVideoTrack != null && !localVideoTrack.Enabled;
        public bool IsMicrophoneMuted => localAudioTrack != null && !localAudioTrack.Enabled;

        private const int RemoteAudioBufferSeconds = 2;

        private RTCPeerConnection peerConnection;
        private readonly List<RTCRtpSender> activeSenders = new();
        private RTCDataChannel dataChannel;
        private RTCIceServer[] iceServers;
        private bool isInitiator;
        private bool remoteReady;
        private bool negotiationRunning;

        private WebCamTexture webcamTexture;
        private VideoStreamTrack localVideoTrack;
        private AudioSource microphoneSource;
        private AudioStreamTrack localAudioTrack;
        private Coroutine microphoneRoutine;
        private string activeMicrophoneDevice;

        private VideoStreamTrack remoteVideoTrack;
        private AudioStreamTrack remoteAudioTrack;
        private readonly Queue<float> remoteAudioSamples = new();
        private AudioClip remoteAudioClip;
        private int remoteAudioChannels = 1;
        private int remoteAudioSampleRate = 48000;
        private readonly object remoteAudioLock = new();
        private GameObject remoteAudioOutputObject;

        private Coroutine webRtcUpdateRoutine;

        public void Configure(RTCIceServer[] servers, bool initiator)
        {
            DisposePeer();
            DisposeRemoteMedia();
            iceServers = servers;
            isInitiator = initiator;
            SetupPeer();
        }

        public void SendChatMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !HasOpenChannel)
            {
                return;
            }

            dataChannel.Send(message);
            OnChatMessageReceived?.Invoke($"Me: {message}");
        }

        public void HandleRemoteSignal(SignalPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            switch (payload.kind)
            {
                case SignalPayloadKinds.Ready:
                    remoteReady = true;
                    TryCreateOffer();
                    break;
                case SignalPayloadKinds.Sdp:
                    HandleRemoteSdp(payload);
                    break;
                case SignalPayloadKinds.Ice:
                    HandleRemoteIce(payload);
                    break;
            }
        }

        public void SetMicrophoneMuted(bool mute)
        {
            if (localAudioTrack != null)
            {
                localAudioTrack.Enabled = !mute;
            }
            if (microphoneSource != null)
            {
                microphoneSource.mute = mute;
            }
        }

        public void SetCameraMuted(bool mute)
        {
            if (localVideoTrack != null)
            {
                localVideoTrack.Enabled = !mute;
            }
            if (webcamTexture != null)
            {
                if (mute && webcamTexture.isPlaying)
                {
                    webcamTexture.Pause();
                }
                else if (!mute && !webcamTexture.isPlaying)
                {
                    webcamTexture.Play();
                }
            }
        }

        public void HangUp()
        {
            DisposePeer();
            DisposeLocalMedia();
            DisposeRemoteMedia();
        }

        public void DisposePeer()
        {
            if (dataChannel != null)
            {
                dataChannel.OnOpen = null;
                dataChannel.OnClose = null;
                dataChannel.OnMessage = null;
                dataChannel.Close();
                dataChannel.Dispose();
                dataChannel = null;
            }

            if (peerConnection != null)
            {
                peerConnection.OnIceCandidate = null;
                peerConnection.OnDataChannel = null;
                peerConnection.OnTrack = null;
                peerConnection.OnConnectionStateChange = null;
                foreach (var sender in activeSenders)
                {
                    sender?.Dispose();
                }
                activeSenders.Clear();
                peerConnection.Close();
                peerConnection.Dispose();
                peerConnection = null;
            }

            negotiationRunning = false;
            remoteReady = false;
        }

        private void SetupPeer()
        {
            if (webRtcUpdateRoutine == null)
            {
                webRtcUpdateRoutine = StartCoroutine(Unity.WebRTC.WebRTC.Update());
            }

            var config = default(RTCConfiguration);
            config.iceServers = iceServers;
            peerConnection = new RTCPeerConnection(ref config);

            peerConnection.OnIceCandidate = candidate =>
            {
                if (candidate == null)
                {
                    return;
                }

                var payload = SignalPayload.FromIceCandidate(candidate);
                OnSignalReady?.Invoke(JsonUtility.ToJson(payload));
            };

            peerConnection.OnDataChannel = channel => AttachDataChannel(channel);
            peerConnection.OnTrack = HandleRemoteTrack;
            peerConnection.OnConnectionStateChange = state =>
            {
                OnStatusMessage?.Invoke($"Peer connection state: {state}");
                if (state == RTCPeerConnectionState.Disconnected || state == RTCPeerConnectionState.Failed)
                {
                    OnChannelClosed?.Invoke();
                }
            };

            AttachExistingLocalTracks();

            if (isInitiator)
            {
                AttachDataChannel(peerConnection.CreateDataChannel("chat"));
                TryCreateOffer();
            }
        }

        private void AttachExistingLocalTracks()
        {
            if (localVideoTrack == null && autoStartCamera)
            {
                StartLocalCamera();
            }
            if (localAudioTrack == null && autoStartMicrophone && microphoneRoutine == null)
            {
                microphoneRoutine = StartCoroutine(StartLocalMicrophoneRoutine());
            }

            if (localVideoTrack != null)
            {
                AddTrackToPeer(localVideoTrack);
            }
            if (localAudioTrack != null)
            {
                AddTrackToPeer(localAudioTrack);
            }
        }

        private void StartLocalCamera()
        {
            if (webcamTexture != null)
            {
                return;
            }

            string deviceName = WebCamTexture.devices.Length > 0 ? WebCamTexture.devices[0].name : null;
            webcamTexture = string.IsNullOrEmpty(deviceName)
                ? new WebCamTexture(webcamWidth, webcamHeight, webcamFramerate)
                : new WebCamTexture(deviceName, webcamWidth, webcamHeight, webcamFramerate);
            webcamTexture.Play();
            localVideoTrack = new VideoStreamTrack(webcamTexture);
            OnLocalVideoTexture?.Invoke(webcamTexture);
        }

        private IEnumerator StartLocalMicrophoneRoutine()
        {
            try
            {
                if (Microphone.devices.Length == 0)
                {
                    OnStatusMessage?.Invoke("No microphone detected on this device.");
                    yield break;
                }

                if (microphoneSource == null)
                {
                    microphoneSource = gameObject.AddComponent<AudioSource>();
                    microphoneSource.playOnAwake = false;
                    microphoneSource.loop = true;
                    microphoneSource.mute = true; // never echo local mic
                }

                activeMicrophoneDevice = ResolveMicrophoneDevice();
                microphoneSource.clip = Microphone.Start(activeMicrophoneDevice, true, 1, microphoneSampleRate);
                while (Microphone.GetPosition(activeMicrophoneDevice) <= 0)
                {
                    yield return null;
                }

                microphoneSource.Play();
                localAudioTrack = new AudioStreamTrack(microphoneSource);
                localAudioTrack.Loopback = false;
                AddTrackToPeer(localAudioTrack);
            }
            finally
            {
                microphoneRoutine = null;
            }
        }

        private string ResolveMicrophoneDevice()
        {
            if (!string.IsNullOrEmpty(preferredMicrophone))
            {
                foreach (var device in Microphone.devices)
                {
                    if (string.Equals(device, preferredMicrophone, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }
            }

            return Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
        }

        private void AddTrackToPeer(MediaStreamTrack track)
        {
            if (peerConnection == null || track == null)
            {
                return;
            }

            var sender = peerConnection.AddTrack(track);
            if (sender != null)
            {
                activeSenders.Add(sender);
            }
        }

        private void AttachDataChannel(RTCDataChannel channel)
        {
            dataChannel = channel;
            dataChannel.OnOpen = () =>
            {
                OnStatusMessage?.Invoke("Data channel opened.");
                OnChannelOpen?.Invoke();
            };
            dataChannel.OnClose = () =>
            {
                OnStatusMessage?.Invoke("Data channel closed.");
                OnChannelClosed?.Invoke();
            };
            dataChannel.OnMessage = bytes =>
            {
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                OnChatMessageReceived?.Invoke($"Peer: {text}");
            };
        }

        private void HandleRemoteTrack(RTCTrackEvent trackEvent)
        {
            if (trackEvent.Track is VideoStreamTrack videoTrack)
            {
                AttachRemoteVideo(videoTrack);
            }
            else if (trackEvent.Track is AudioStreamTrack audioTrack)
            {
                AttachRemoteAudio(audioTrack);
            }
        }

        private void AttachRemoteVideo(VideoStreamTrack track)
        {
            if (remoteVideoTrack != null)
            {
                remoteVideoTrack.OnVideoReceived -= HandleRemoteVideoReceived;
                remoteVideoTrack.Dispose();
            }

            remoteVideoTrack = track;
            remoteVideoTrack.OnVideoReceived += HandleRemoteVideoReceived;
        }

        private void HandleRemoteVideoReceived(Texture texture)
        {
            OnRemoteVideoTexture?.Invoke(texture);
        }

        private void AttachRemoteAudio(AudioStreamTrack track)
        {
            if (remoteAudioTrack != null)
            {
                remoteAudioTrack.onReceived -= HandleRemoteAudioSamples;
                remoteAudioTrack.Dispose();
            }

            remoteAudioTrack = track;
            remoteAudioTrack.onReceived += HandleRemoteAudioSamples;
            PrepareRemoteAudioOutput();
        }

        private void HandleRemoteAudioSamples(float[] data, int channels, int sampleRate)
        {
            bool requiresRecreate = false;
            lock (remoteAudioLock)
            {
                if (channels != remoteAudioChannels || sampleRate != remoteAudioSampleRate)
                {
                    remoteAudioChannels = Mathf.Max(1, channels);
                    remoteAudioSampleRate = Mathf.Max(8000, sampleRate);
                    requiresRecreate = true;
                }

                foreach (var sample in data)
                {
                    remoteAudioSamples.Enqueue(sample);
                    var maxSamples = remoteAudioSampleRate * remoteAudioChannels * RemoteAudioBufferSeconds;
                    if (remoteAudioSamples.Count > maxSamples)
                    {
                        remoteAudioSamples.Dequeue();
                    }
                }
            }

            if (requiresRecreate)
            {
                PrepareRemoteAudioOutput();
            }
        }

        private void PrepareRemoteAudioOutput()
        {
            EnsureRemoteAudioOutput();

            int clipSamples = Mathf.Max(remoteAudioSampleRate * RemoteAudioBufferSeconds, remoteAudioSampleRate);
            remoteAudioClip = AudioClip.Create("RemotePeer", clipSamples, remoteAudioChannels, remoteAudioSampleRate, true, OnRemoteAudioRead, OnRemoteAudioSetPosition);
            remoteAudioOutput.loop = true;
            remoteAudioOutput.clip = remoteAudioClip;
            remoteAudioOutput.Play();
        }

        private void EnsureRemoteAudioOutput()
        {
            if (remoteAudioOutput != null)
            {
                remoteAudioOutput.playOnAwake = false;
                remoteAudioOutput.spatialBlend = 0f;
                return;
            }

            if (remoteAudioOutputObject == null)
            {
                remoteAudioOutputObject = new GameObject("RemotePeerAudioOutput");
                remoteAudioOutputObject.transform.SetParent(transform, false);
            }

            remoteAudioOutput = remoteAudioOutputObject.GetComponent<AudioSource>();
            if (remoteAudioOutput == null)
            {
                remoteAudioOutput = remoteAudioOutputObject.AddComponent<AudioSource>();
            }

            remoteAudioOutput.playOnAwake = false;
            remoteAudioOutput.loop = false;
            remoteAudioOutput.spatialBlend = 0f;
        }

        private void OnRemoteAudioRead(float[] data)
        {
            lock (remoteAudioLock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = remoteAudioSamples.Count > 0 ? remoteAudioSamples.Dequeue() : 0f;
                }
            }
        }

        private void OnRemoteAudioSetPosition(int position)
        {
            // no-op but required for streaming clips
        }

        private void TryCreateOffer()
        {
            if (!isInitiator || !remoteReady || negotiationRunning || peerConnection == null)
            {
                return;
            }

            negotiationRunning = true;
            StartCoroutine(CreateOfferRoutine());
        }

        private IEnumerator CreateOfferRoutine()
        {
            var offerOp = peerConnection.CreateOffer();
            yield return offerOp;
            if (offerOp.IsError)
            {
                OnStatusMessage?.Invoke($"CreateOffer error: {offerOp.Error.message}");
                negotiationRunning = false;
                yield break;
            }

            var offerDesc = offerOp.Desc;
            var setLocalOp = peerConnection.SetLocalDescription(ref offerDesc);
            yield return setLocalOp;
            if (setLocalOp.IsError)
            {
                OnStatusMessage?.Invoke($"SetLocalDescription error: {setLocalOp.Error.message}");
                negotiationRunning = false;
                yield break;
            }

            var payload = SignalPayload.FromSdp(offerDesc);
            OnSignalReady?.Invoke(JsonUtility.ToJson(payload));
            OnStatusMessage?.Invoke("Local offer sent.");
            negotiationRunning = false;
        }

        private void HandleRemoteSdp(SignalPayload payload)
        {
            if (peerConnection == null)
            {
                return;
            }

            if (!Enum.TryParse(payload.sdpType, true, out RTCSdpType remoteType))
            {
                OnStatusMessage?.Invoke($"Unknown SDP type: {payload.sdpType}");
                return;
            }

            var desc = new RTCSessionDescription { type = remoteType, sdp = payload.sdp };
            StartCoroutine(ApplyRemoteDescriptionRoutine(desc));
        }

        private IEnumerator ApplyRemoteDescriptionRoutine(RTCSessionDescription desc)
        {
            var setRemoteOp = peerConnection.SetRemoteDescription(ref desc);
            yield return setRemoteOp;
            if (setRemoteOp.IsError)
            {
                OnStatusMessage?.Invoke($"SetRemoteDescription error: {setRemoteOp.Error.message}");
                yield break;
            }

            if (desc.type == RTCSdpType.Offer)
            {
                var answerOp = peerConnection.CreateAnswer();
                yield return answerOp;
                if (answerOp.IsError)
                {
                    OnStatusMessage?.Invoke($"CreateAnswer error: {answerOp.Error.message}");
                    yield break;
                }

                var answerDesc = answerOp.Desc;
                var setLocalOp = peerConnection.SetLocalDescription(ref answerDesc);
                yield return setLocalOp;
                if (setLocalOp.IsError)
                {
                    OnStatusMessage?.Invoke($"SetLocalDescription error: {setLocalOp.Error.message}");
                    yield break;
                }

                var payload = SignalPayload.FromSdp(answerDesc);
                OnSignalReady?.Invoke(JsonUtility.ToJson(payload));
                OnStatusMessage?.Invoke("Answer sent.");
            }
        }

        private void HandleRemoteIce(SignalPayload payload)
        {
            if (peerConnection == null || string.IsNullOrEmpty(payload.candidate))
            {
                return;
            }

            var candidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = payload.candidate,
                sdpMid = payload.sdpMid,
                sdpMLineIndex = payload.sdpMLineIndex
            });

            peerConnection.AddIceCandidate(candidate);
        }

        private void DisposeLocalMedia()
        {
            if (webcamTexture != null)
            {
                if (webcamTexture.isPlaying)
                {
                    webcamTexture.Stop();
                }
                Destroy(webcamTexture);
                webcamTexture = null;
            }

            if (localVideoTrack != null)
            {
                localVideoTrack.Dispose();
                localVideoTrack = null;
            }

            if (microphoneRoutine != null)
            {
                StopCoroutine(microphoneRoutine);
                microphoneRoutine = null;
            }

            if (microphoneSource != null && microphoneSource.clip != null)
            {
                if (!string.IsNullOrEmpty(activeMicrophoneDevice) && Microphone.IsRecording(activeMicrophoneDevice))
                {
                    Microphone.End(activeMicrophoneDevice);
                }
                else if (string.IsNullOrEmpty(activeMicrophoneDevice))
                {
                    Microphone.End(null);
                }

                microphoneSource.Stop();
                activeMicrophoneDevice = null;
            }

            if (localAudioTrack != null)
            {
                localAudioTrack.Dispose();
                localAudioTrack = null;
            }
        }

        private void DisposeRemoteMedia()
        {
            if (remoteVideoTrack != null)
            {
                remoteVideoTrack.OnVideoReceived -= HandleRemoteVideoReceived;
                remoteVideoTrack.Dispose();
                remoteVideoTrack = null;
            }

            if (remoteAudioTrack != null)
            {
                remoteAudioTrack.onReceived -= HandleRemoteAudioSamples;
                remoteAudioTrack.Dispose();
                remoteAudioTrack = null;
            }

            if (remoteAudioClip != null)
            {
                if (remoteAudioOutput != null && remoteAudioOutput.isPlaying)
                {
                    remoteAudioOutput.Stop();
                    remoteAudioOutput.clip = null;
                }
                Destroy(remoteAudioClip);
                remoteAudioClip = null;
            }

            if (remoteAudioOutputObject != null && remoteAudioOutput == null)
            {
                Destroy(remoteAudioOutputObject);
                remoteAudioOutputObject = null;
            }

            lock (remoteAudioLock)
            {
                remoteAudioSamples.Clear();
            }
        }

        private void OnDestroy()
        {
            HangUp();
            if (webRtcUpdateRoutine != null)
            {
                StopCoroutine(webRtcUpdateRoutine);
                webRtcUpdateRoutine = null;
            }

            if (remoteAudioOutputObject != null)
            {
                Destroy(remoteAudioOutputObject);
                remoteAudioOutputObject = null;
            }
        }
    }

    [Serializable]
    public class SignalPayload
    {
        public string kind;
        public string sdpType;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;

        public static SignalPayload Ready() => new SignalPayload { kind = SignalPayloadKinds.Ready };

        public static SignalPayload FromSdp(RTCSessionDescription desc)
        {
            return new SignalPayload
            {
                kind = SignalPayloadKinds.Sdp,
                sdpType = desc.type.ToString(),
                sdp = desc.sdp
            };
        }

        public static SignalPayload FromIceCandidate(RTCIceCandidate candidate)
        {
            return new SignalPayload
            {
                kind = SignalPayloadKinds.Ice,
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex ?? 0
            };
        }
    }

    public static class SignalPayloadKinds
    {
        public const string Ready = "ready";
        public const string Sdp = "sdp";
        public const string Ice = "ice";
    }
}
