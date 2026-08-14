using LCCCore;
using UnityEngine;

/// <summary>
/// LCCRendererVR originale XGRIDS + tre aggiunte:
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
/// 3) PARAMETRI DI BUDGET esposti in Inspector (guida XGRIDS "SDK Rendering Performance
///    Parameter Configuration"):
///     - SetMaxBufferSplat  = capienza del Buffer GPU, ALLOCAZIONE UNA TANTUM. Su VR:
///       default 100 (= 1 M splat), tetto 200 (= 2 M). DEVE precedere GetRender — e qui lo
///       precede sempre, perche' con "un'area = una scena" ogni ingresso e' un teardown
///       completo e non esiste il caso "cambiare il buffer a renderer vivi" (che
///       richiederebbe il Dispose di tutti).
///     - SetMaxRenderSplats = splat A SCHERMO nel rendering chunked (l'unico percorso su
///       Quest). Su VR: default 60 (= 600 k), minimo 10, massimo = il buffer. Va chiamato
///       DOPO SetMaxBufferSplat. E' il pomello dal miglior rapporto qualita'/prezzo: alzare
///       gli splat visibili DENTRO il buffer non tocca l'allocazione GPU; alzare il buffer si'.
///
///    UNITA' DELL'SDK: decine di migliaia (万). 100 = 1.000.000 di splat. La guida usa la
///    stessa unita' ovunque (es. SetMaxBufferSplat(3000) = 30 M su PC).
///
///    0 = NON chiamare l'API, restano i default di piattaforma. E' il valore di fabbrica di
///    proposito: questo componente vive in QUATTRO scene (Silvo01–04) e i valori sono
///    SERIALIZZATI in ciascuna — la trappola nota: cambiare i default nel codice non tocca i
///    componenti gia' nelle scene. Con 0 il comportamento resta quello attuale finche' non si
///    impostano i numeri, scena per scena, TENENDOLI UGUALI IN TUTTE E QUATTRO: il buffer e'
///    un'allocazione globale e valori diversi fra scene sono solo un modo per confondersi.
///    Il log dichiara cosa e' stato applicato, cosi' una scena rimasta indietro si vede dal
///    logcat (filtro: adb logcat -s Unity | grep LCCRendererVR).
///
/// NIENTE try/catch attorno alle chiamate SDK, di proposito: la volta scorsa la prudenza aveva
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

    [Header("Budget splat (unita' SDK: decine di migliaia — 100 = 1 M)")]
    [Tooltip("Capienza del Buffer GPU (allocazione una tantum, prima di GetRender). " +
             "VR: default 100 (= 1 M splat), tetto 200 (= 2 M). 0 = non chiamare, default SDK. " +
             "STESSO valore in tutte e quattro le scene-area: e' un'allocazione globale.")]
    [Min(0)] public int maxBufferSplat10k = 0;

    [Tooltip("Splat contemporanei A SCHERMO nel rendering chunked (il percorso Quest). " +
             "VR: default 60 (= 600 k), minimo 10, massimo = il buffer. 0 = non chiamare. " +
             "E' il primo pomello da provare per la qualita': non tocca l'allocazione GPU.")]
    [Min(0)] public int maxRenderSplats10k = 0;

    [Header("Dettaglio (i budget senza questi non cambiano nulla di visibile)")]
    [Tooltip("LOD di partenza (0 = il piu' FINE, 10 = il piu' grosso). Tabella dei tier della " +
             "guida XGRIDS: 2 = Very Low, 1 = Low/Medium, 0 = High/Very High. Era cablato a 2 " +
             "dall'esempio originale: e' il motivo per cui alzare i budget non cambiava nulla — " +
             "partendo dal livello 2, i livelli piu' fini non vengono mai richiesti, per quanto " +
             "buffer si conceda. Si applica dopo il Load, come prescrive l'API.")]
    [Range(0, 10)] public int startLod = 2;

    [Tooltip("SetDetailLevel [1-100]: quanto in profondita' raffinare la gerarchia. 1 = blocca " +
             "il raffinamento anche da vicino, 100 = raffina fino al livello piu' basso. " +
             "0 = non chiamare (default SDK). Governa il dettaglio PERCEPITO insieme a StartLod; " +
             "i budget governano solo quanto materiale puo' fluire.")]
    [Range(0, 100)] public int detailLevel = 0;

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
        ApplySplatBudget();
        m_renderer = m_manager.GetRender(this.gameObject.transform);
        m_renderer.Load(m_FilePath, onProgress, onLoaded);
#else
        m_manager.SetPlatformType(PlatformType.Quest);
        ApplySplatBudget();
        m_renderer = m_manager.GetRender(this.gameObject.transform);
        m_renderer.Load(m_FilePath, onProgress, onLoaded);
#endif
    }

    /// <summary>
    /// Applica i budget PRIMA di GetRender, nell'ordine prescritto dalla guida
    /// (SetMaxBufferSplat, poi SetMaxRenderSplats). Con 0 non chiama nulla e lo dice,
    /// cosi' dal logcat si legge scena per scena quale configurazione era attiva —
    /// indispensabile quando i valori vivono serializzati in quattro file di scena.
    /// </summary>
    private void ApplySplatBudget()
    {
        if (maxBufferSplat10k > 0)
        {
            m_manager.SetMaxBufferSplat(maxBufferSplat10k);
            Debug.Log($"[LCCRendererVR] '{gameObject.scene.name}': SetMaxBufferSplat({maxBufferSplat10k}) " +
                      $"= {maxBufferSplat10k * 10000:N0} splat nel buffer GPU.");
        }
        else
        {
            Debug.Log($"[LCCRendererVR] '{gameObject.scene.name}': buffer splat al default SDK " +
                      "(VR: 1 M).");
        }

        if (maxRenderSplats10k > 0)
        {
            m_manager.SetMaxRenderSplats(maxRenderSplats10k);
            Debug.Log($"[LCCRendererVR] '{gameObject.scene.name}': SetMaxRenderSplats({maxRenderSplats10k}) " +
                      $"= {maxRenderSplats10k * 10000:N0} splat a schermo.");
        }
        else
        {
            Debug.Log($"[LCCRendererVR] '{gameObject.scene.name}': splat a schermo al default SDK " +
                      "(VR: 600 k).");
        }
    }

    private void onProgress(float v)
    {
        Debug.Log("progress:" + v);
    }

    private void onLoaded()
    {
        // StartLod dall'Inspector (era cablato a 2 = tier Very Low). Va dopo il Load,
        // come prescrive l'API. 0 = LOD piu' fine.
        m_manager.SetStartLod(startLod);
        Debug.Log($"[LCCRendererVR] '{gameObject.scene.name}': SetStartLod({startLod}) " +
                  $"({(startLod == 0 ? "il piu' fine" : startLod >= 2 ? "tier Very Low" : "tier Low/Medium")}).");

        if (detailLevel > 0)
        {
            m_manager.SetDetailLevel(detailLevel);
            Debug.Log($"[LCCRendererVR] '{gameObject.scene.name}': SetDetailLevel({detailLevel}).");
        }

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