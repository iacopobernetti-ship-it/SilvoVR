using LCCCore;
using UnityEngine;

public class MultiRenderer : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath01;
    public Transform m_transform01;
    
    public string m_FilePath02;
    public Transform m_transform02;

    private LCCCore.Renderer m_renderer01;
    private LCCCore.Renderer m_renderer02;

    private int m_currentFileIndex = 0;

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        // Set the render mode to MultiRender to allow multiple renderers to be active simultaneously
        m_manager.SwitchRenderPass(ActiveRenderMode.MultiRender);

        Debug.Log("Started ");
        m_renderer01 = m_manager.GetRender(m_transform01);
        m_renderer02 = m_manager.GetRender(m_transform02);        
        m_currentFileIndex = 0;
        m_renderer01.Load(m_FilePath01,onProgress,onComplete);
        m_currentFileIndex = 1;
        m_renderer02.Load(m_FilePath02, onProgress, onComplete);
    }


    private void onProgress(float _progress)
    {
        Debug.Log($"[LCCRenderer] file {m_currentFileIndex} load progress: {_progress:P1}");
    }
    private void onComplete()
    {
        Debug.Log($"[LCCRenderer] file {m_currentFileIndex} loaded");
    }
}
