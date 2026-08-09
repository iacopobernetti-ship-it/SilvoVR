using LCCCore;
using UnityEngine;

public class Snap : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    private LCCCore.Renderer m_renderer;
    private bool m_loaded = false;
    private Camera m_camera;

    void Start()
    {
        m_camera = Camera.main;
        m_manager.SetPlatformType(PlatformType.PC);
        Debug.Log("Started ");
        m_renderer = m_manager.GetRender(this.transform);
        m_renderer.Load(m_FilePath,onProgress,onComplete);
    }

    private void onProgress(float _progress)
    {
        Debug.Log($"[LCCRenderer] file load progress: {_progress:P1}");
    }
    private void onComplete()
    {
        Debug.Log($"[LCCRenderer] file loaded");
        m_loaded = true;
        // Enable snapping and snap preview
        m_manager.SetSnapEnabled(true);
        m_manager.SetSnapPreviewEnabled(true);
    }

    private void Update()
    {
        if (!m_loaded) return;
        // Update the snap preview based on the current mouse position
        m_manager.UpdateSnapPreview(Input.mousePosition, m_camera);
        if (Input.GetMouseButtonDown(1))
        {
            m_manager.RaycastWithSnap(Input.mousePosition, m_camera, out HitResult result);
            if (result.isHit)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.position = result.hitPos;
                sphere.GetComponent<UnityEngine.Renderer>().material.color = Color.red;
                sphere.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            }
        }
    }


}
