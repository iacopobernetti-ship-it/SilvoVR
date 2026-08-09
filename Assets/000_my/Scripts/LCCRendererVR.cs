using LCCCore;
using UnityEngine;

/// <summary>
/// LCCRendererVR originale XGRIDS + due sole aggiunte:
///
/// 1) SetZDepth sul MANAGER (non sul Renderer — l'errore che avevamo fatto prima, per cui
///    la chiamata non ha mai avuto effetto). Documentazione API: "开关Z深度写入，同时调整渲染队列"
///    = attiva la scrittura Z e contestualmente aggiusta la coda di rendering. E' esattamente
///    cio' che serve perche' gli splat smettano di coprire la UI world-space: scrivendo
///    profondita' ed entrando nella coda giusta, un pannello a 0.7 m davanti al viso non viene
///    piu' sovrascritto da un tronco a 5 m.
///    Nota: e' una delle poche API che NON riporta la limitazione "solo PC/Mac", presente
///    invece su SetMainCamera/AddCamera/RemoveCamera — ed e' per quel vincolo che la strada
///    della camera overlay era condannata in partenza.
///
/// 2) skipInEditor: in Play Mode l'SDK forza PlatformType.PC e in visore via Link si vede in
///    diplopia. Limite noto e strutturale: gli splat si collaudano SOLO in build standalone.
///    Con questo attivo, in Editor lo splat non si carica e si puo' lavorare via Link su tutto
///    il resto (HUD, cambio scena, interazioni) con controller veri e Console.
///
/// NIENTE try/catch attorno a SetZDepth, di proposito: la volta scorsa la prudenza aveva
/// ingoiato in silenzio proprio l'errore che avrebbe rivelato il metodo sbagliato. Se fallisce,
/// deve fallire rumorosamente.
/// </summary>
public class LCCRendererVR : MonoBehaviour
{
    public LCCManager m_manager;
    public string m_FilePath;

    [Tooltip("Attiva la scrittura Z degli splat: senza, il pass LCC copre la UI world-space. " +
             "Se dovesse costare in prestazioni, si spegne da qui e si misura.")]
    public bool splatDepthWrite = true;

    [Tooltip("In Editor non caricare lo splat: evita la diplopia da Play Mode. Nessun effetto in build.")]
    public bool skipInEditor = true;

    private LCCCore.Renderer m_renderer;

    void Start()
    {
#if UNITY_EDITOR
        if (skipInEditor)
        {
            Debug.Log("[LCCRendererVR] Editor + skipInEditor: splat NON caricato (evita la diplopia).");
            return;
        }
        m_manager.SetPlatformType(PlatformType.PC);
        m_renderer = m_manager.GetRender(this.gameObject.transform);
        m_renderer.Load(m_FilePath, onProgress, onLoaded);
#else
        m_manager.SetPlatformType(PlatformType.Quest);
        m_renderer = m_manager.GetRender(this.gameObject.transform);
        m_renderer.Load(m_FilePath, onProgress, onLoaded);
#endif
    }

    private void onProgress(float v)
    {
        Debug.Log("progress:" + v);
    }

    private void onLoaded()
    {
        m_manager.SetStartLod(2);
        m_renderer.SetEnvironment(false);

        if (splatDepthWrite)
        {
            m_manager.SetZDepth(true);
            Debug.Log("[LCCRendererVR] SetZDepth(true) sul MANAGER: gli splat scrivono profondita' " +
                      "e la coda di rendering viene riaggiustata — la HUD dovrebbe restare davanti.");
        }

        Debug.Log("[LCCRendererVR] Data loaded");
    }
}
