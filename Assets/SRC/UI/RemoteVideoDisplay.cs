using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple helper that keeps a UI RawImage or Renderer updated with the latest remote video texture.
/// Attach this to any GameObject in the scene and assign the WebRTCManager plus the surface to paint.
/// </summary>
public class RemoteVideoDisplay : MonoBehaviour
{
    [SerializeField] private WebRTCManager webRTCManager;
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private float refreshIntervalSeconds = 0.1f;

    private Coroutine updateRoutine;

    void OnEnable()
    {
        if (refreshIntervalSeconds <= 0f)
            refreshIntervalSeconds = 0.1f;

        updateRoutine = StartCoroutine(UpdateRemoteTextureRoutine());
    }

    void OnDisable()
    {
        if (updateRoutine != null)
        {
            StopCoroutine(updateRoutine);
            updateRoutine = null;
        }

        if (targetRawImage != null)
            targetRawImage.texture = null;
        if (targetRenderer != null && targetRenderer.material != null)
            targetRenderer.material.mainTexture = null;
    }

    private IEnumerator UpdateRemoteTextureRoutine()
    {
        var wait = new WaitForSeconds(refreshIntervalSeconds);
        while (enabled)
        {
            if (webRTCManager != null)
            {
                var texture = webRTCManager.GetRemoteVideoTexture();
                if (texture != null)
                {
                    if (targetRawImage != null)
                    {
                        targetRawImage.texture = texture;
                    }

                    if (targetRenderer != null && targetRenderer.material != null)
                    {
                        targetRenderer.material.mainTexture = texture;
                    }
                }
            }

            yield return wait;
        }
    }
}
