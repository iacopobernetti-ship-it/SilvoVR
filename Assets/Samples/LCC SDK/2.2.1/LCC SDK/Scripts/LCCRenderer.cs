using LCCCore;
using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
[DefaultExecutionOrder(100)]
public class LCCRenderer : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;
    private LCCCore.Renderer m_renderer;
    private Camera m_mainCamera;
    private bool m_isLoaded = false;
    void Start()
    {
        m_mainCamera = Camera.main;

        switch (Application.platform)
        {
            case RuntimePlatform.IPhonePlayer:
                m_manager.SetPlatformType(PlatformType.iOS);
                //on iOS, only support URL to load the file from the server.
                //m_FilePath = $"https://192.168.1.15/LccData/meta.lcc2";
                break;
            case RuntimePlatform.Android:
                if (SystemInfo.deviceModel.ToLower().Contains("quest"))
                    m_manager.SetPlatformType(PlatformType.Quest);
                else if (SystemInfo.deviceModel.ToLower().Contains("pico"))
                    m_manager.SetPlatformType(PlatformType.Pico);
                else
                    m_manager.SetPlatformType(PlatformType.Android);
                // on Android, we need to set the file path to the persistent data path, because the assets are not accessible directly.
                // or you can use URL to load the file from the server.
                //m_FilePath = $"{Application.persistentDataPath}/LccData/meta.lcc2";
                break;
            case RuntimePlatform.OSXPlayer:
                m_manager.SetPlatformType(PlatformType.Mac);
                break;
            case RuntimePlatform.WindowsPlayer:
                m_manager.SetPlatformType(PlatformType.PC);
                break;
            case RuntimePlatform.OSXEditor:
                m_manager.SetPlatformType(PlatformType.Mac);
                break;
            case RuntimePlatform.WindowsEditor:
                m_manager.SetPlatformType(PlatformType.PC);
                break;
            // AVP is not supported in this version, so we comment it out for now.
            //case RuntimePlatform.VisionOS:
            //    m_manager.SetPlatformType(PlatformType.AVP);
            //    break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        //Render Pass is SingleRender by default, you can switch to MultiRender if you want render multi lcc2 file at the same time.
        //m_manager.SwitchRenderPass(ActiveRenderMode.SingleRender);        

        m_manager.SetMainCamera(m_mainCamera);
        m_renderer = m_manager.GetRender(this.transform);  
        if(m_renderer == null )
            Debug.LogError("GetRender failed, check if the manager is set correctly and the file path is valid.");
        m_renderer.Load(m_FilePath,onLoadProgress, onLoadCallback);
    }

    private void onLoadProgress(float value)
    {
        Debug.Log($"loading progress: {value}");
    }

    private void onLoadCallback()
    { 
        Debug.Log("data loaded !!!");
        m_isLoaded = true;        
    }

    [ContextMenu("Render (need a little time to load data)")]
    public void Render()
    {    
        if(m_renderer == null)
            m_renderer = m_manager.GetRender(this.transform);
        m_renderer.Load(m_FilePath,onLoadProgress, onLoadCallback);
    }

    [ContextMenu("unRender")]
    public void unRender()
    {
        m_renderer?.Dispose();
    }
}
