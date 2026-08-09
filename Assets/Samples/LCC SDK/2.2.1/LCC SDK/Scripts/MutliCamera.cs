using LCCCore;
using UnityEngine;

public class MutliCamera : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath01;
    public Camera m_camera01; 
    public Camera m_camera02;

    public Transform m_transform01;
    private LCCCore.Renderer m_renderer01;

    private int m_currentFileIndex = 0;

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        m_manager.SwitchRenderPass(ActiveRenderMode.SingleRender);

        Debug.Log("Started ");
        m_renderer01 = m_manager.GetRender(m_transform01);
     
        m_currentFileIndex = 0;
        m_renderer01.Load(m_FilePath01,onProgress,onComplete);

    }


    private void onProgress(float _progress)
    {
        Debug.Log($"[LCCRenderer] file {m_currentFileIndex} load progress: {_progress:P1}");
    }
    private void onComplete()
    {
        Debug.Log($"[LCCRenderer] file {m_currentFileIndex} loaded");

        m_manager.AddCamera(m_camera01);
        m_manager.AddCamera(m_camera02);
    }

    private void OnDestroy()
    {
        m_manager.RemoveCamera(m_camera01);
        m_manager.RemoveCamera(m_camera02) ;
    }
}
