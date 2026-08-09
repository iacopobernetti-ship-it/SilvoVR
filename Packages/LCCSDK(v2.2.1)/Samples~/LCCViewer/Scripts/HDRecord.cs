using System.IO;
using System.Collections;
using LCCCore;
using UnityEngine;

public class HDRecord : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    public Camera m_camera;

    private LCCCore.Renderer m_renderer;

    private const int HD_WIDTH = 3840;
    private const int HD_HEIGHT = 2160;
    private const string OUTPUT_DIR = @"G:\output";

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        m_manager.SwitchRenderPass(ActiveRenderMode.SingleRender);

        if (m_camera == null)
            m_camera = Camera.main;

        m_manager.SetMainCamera(m_camera);

        Debug.Log("[HDRecord] Started, loading data...");
        m_renderer = m_manager.GetRender(this.transform);
        m_renderer.Load(m_FilePath, onProgress, onComplete);
    }

    private void onProgress(float _progress)
    {
        Debug.Log($"[HDRecord] Load progress: {_progress:P1}");
    }

    private void onComplete()
    {
        Debug.Log("[HDRecord] Load complete, enabling 4K record mode...");

        float verticalFov = m_camera.fieldOfView;
        m_manager.SetRecordMode(true, new Vector2(HD_WIDTH, HD_HEIGHT), verticalFov);
        StartCoroutine(CaptureFrame());
    }

    private IEnumerator CaptureFrame()
    {
        // Wait for a few frames to ensure the camera has rendered the scene in 4K
        yield return new WaitForSeconds(10);

        RenderTexture tempRT = RenderTexture.GetTemporary(HD_WIDTH, HD_HEIGHT, 24);
        RenderTexture preTarget = m_camera.targetTexture;
        Rect preRect = m_camera.rect;

        m_camera.targetTexture = tempRT;
        m_camera.rect = new Rect(0, 0, 1, 1);

        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        RenderTexture.active = tempRT;
        Texture2D tex = new Texture2D(HD_WIDTH, HD_HEIGHT, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, HD_WIDTH, HD_HEIGHT), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        m_camera.rect = preRect;
        m_camera.targetTexture = preTarget;
        RenderTexture.ReleaseTemporary(tempRT);

        byte[] jpgData = tex.EncodeToJPG(95);
        Destroy(tex);

        if (!Directory.Exists(OUTPUT_DIR))
            Directory.CreateDirectory(OUTPUT_DIR);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(OUTPUT_DIR, $"HD4K_{timestamp}.jpg");
        File.WriteAllBytes(filePath, jpgData);

        Debug.Log($"[HDRecord] 4K image saved: {filePath} ({jpgData.Length / 1024}KB)");

        m_manager.SetRecordMode(false, new Vector2(Screen.width, Screen.height), m_camera.fieldOfView);
        Debug.Log("[HDRecord] Record mode disabled, restored normal rendering.");
    }
}
