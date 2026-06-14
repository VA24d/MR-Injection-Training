using System;
using System.Collections;
using System.IO;
using UnityEngine.UI;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Captures an in-app screenshot and writes it to the device's persistent storage
    /// (<see cref="Application.persistentDataPath"/>/Screenshots). On Quest this lives in the app's
    /// sandbox (Android/data/&lt;package&gt;/files/Screenshots) and is pullable over adb / a file manager.
    ///
    /// Note: a rendered screenshot captures the app's framebuffer only. The OS composites MR passthrough
    /// separately, so passthrough may appear black/transparent behind the virtual content. Use the
    /// headset's system capture (Meta button shortcut) when the real-world background must be included.
    /// </summary>
    public class ScreenshotCapture : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional UI button; its onClick triggers a capture.")]
        Button m_CaptureButton;

        [SerializeField, Tooltip("Subfolder under persistentDataPath where screenshots are written.")]
        string m_Subfolder = "Screenshots";

        [SerializeField, Tooltip("File name prefix for each screenshot.")]
        string m_FileNamePrefix = "injection";

        [SerializeField, Min(1), Tooltip("Resolution multiplier (supersize) for the captured image.")]
        int m_SuperSize = 1;

        [SerializeField, Tooltip("Briefly flashed when a capture succeeds (optional).")]
        GameObject m_ConfirmationFlash;

        [SerializeField, Min(0.1f)]
        float m_ConfirmationSeconds = 0.8f;

        bool m_Capturing;

        /// <summary>Absolute path of the most recently written screenshot (empty until one is taken).</summary>
        public string lastSavedPath { get; private set; } = string.Empty;

        void Awake()
        {
            if (m_CaptureButton != null)
                m_CaptureButton.onClick.AddListener(Capture);
        }

        void OnDestroy()
        {
            if (m_CaptureButton != null)
                m_CaptureButton.onClick.RemoveListener(Capture);
        }

        /// <summary>Public entry point — wire to a UI button, poke interactable, or call directly.</summary>
        public void Capture()
        {
            if (!m_Capturing)
                StartCoroutine(CaptureRoutine());
        }

        IEnumerator CaptureRoutine()
        {
            m_Capturing = true;

            // Capture must happen after the frame has finished rendering.
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture(Mathf.Max(1, m_SuperSize));
                var png = shot.EncodeToPNG();

                var dir = Path.Combine(Application.persistentDataPath, m_Subfolder);
                Directory.CreateDirectory(dir);

                var fileName = $"{m_FileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(dir, fileName);
                File.WriteAllBytes(path, png);

                lastSavedPath = path;
                Debug.Log($"[ScreenshotCapture] Saved screenshot to {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScreenshotCapture] Failed to save screenshot: {e.Message}");
            }
            finally
            {
                if (shot != null)
                    Destroy(shot);
            }

            if (m_ConfirmationFlash != null)
            {
                m_ConfirmationFlash.SetActive(true);
                yield return new WaitForSeconds(m_ConfirmationSeconds);
                m_ConfirmationFlash.SetActive(false);
            }

            m_Capturing = false;
        }
    }
}
