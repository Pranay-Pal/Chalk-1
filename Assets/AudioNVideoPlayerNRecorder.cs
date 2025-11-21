using System;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Timeline;

public class AudioNVideoPlayerNRecorder : MonoBehaviour
{
    [SerializeField] private GameObject WebRTC;
    [SerializeField] private WebRTCManager webRTCManager;
    [SerializeField] private AudioSource inputAudioSource;
    [SerializeField] private AudioSource outputAudioSource;
    private AudioStreamTrack audioTrackInput;
    private VideoStreamTrack videoTrackInput;
    private AudioStreamTrack audioTrackOutput;
    private VideoStreamTrack videoTrackOutput;
    [SerializeField] private MediaStream localStream;
    [SerializeField] private Microphone microphone;

    void OnEnable()
    {
        webRTCManager = WebRTC.GetComponent<WebRTCManager>();
        audioTrackInput = new AudioStreamTrack(inputAudioSource);
        localStream.AddTrack(audioTrackInput);
    }
    void Start()
    {
        microphone = new Microphone();
    }

    void Update()
    {
        
    }
}
