using LCCCore;
using UnityEngine;
using UnityEngine.UI;

public class MipSwitch : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    public Button m_BtnMipSwitch;
    private LCCCore.Renderer m_renderer;
    private MipMode m_mipMode;

    void Start()
    {
        m_manager.SetPlatformType(PlatformType.PC);
        Debug.Log("Started ");
        m_manager.SetEditorMode(true);
        m_mipMode = MipMode.Mip;
        m_BtnMipSwitch.onClick.AddListener(onMipSwitch);
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
    }

    private void onMipSwitch()
    {
        if (m_mipMode == MipMode.Mip)
        {
            m_mipMode = MipMode.Non_Mip;
        }
        else
        {
            m_mipMode = MipMode.Mip;
        }
        m_manager.SetMipMode(m_mipMode);
    }
}
