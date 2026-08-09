using LCCCore;
using UnityEngine;

public class VFX : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    private LCCCore.Renderer m_renderer;

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        m_manager.SwitchRenderPass(ActiveRenderMode.SingleRender);

        Debug.Log(" Started, loading data...");
        m_renderer = m_manager.GetRender(this.transform);
        m_renderer.Load(m_FilePath, onProgress, onComplete);
    }

    private void onProgress(float _progress)
    {
        Debug.Log($"Load progress: {_progress:P1}");
    }

    private void onComplete()
    {
        Debug.Log(" Load complete, enabling VFX ...");
        Vector3 pos = Camera.main.transform.position + Camera.main.transform.forward * 2.0f;
        m_manager.TriggerVFX(pos);
    }
}
