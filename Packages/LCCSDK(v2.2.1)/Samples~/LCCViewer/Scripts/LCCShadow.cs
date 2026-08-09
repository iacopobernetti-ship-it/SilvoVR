using LCCCore;
using UnityEngine;

public class LCCShadow : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    private LCCCore.Renderer m_renderer;

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        m_manager.SwitchRenderPass(ActiveRenderMode.SingleRender);

        Debug.Log("Started ");
        m_renderer = m_manager.GetRender(this.transform);
        m_renderer.Load(m_FilePath, onProgress, onComplete);
    }


    private void onProgress(float _progress)
    {
        Debug.Log($"[LCCRenderer] file load progress: {_progress:P1}");
    }
    private void onComplete()
    {
        Debug.Log($"[LCCRenderer] file  loaded");
        m_manager.SetShadowReceive(true);
        //m_manager.SetShadowColor(new Color(0, 0, 0, 1.0f));
        //m_manager.SetShadowStrength(0.5f);
    }
}
