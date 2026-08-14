using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

namespace Artemis.Vr
{
    /// <summary>
    /// La CORNICE della HUD VR: pannello world-space lazy-follow (mai head-locked rigido),
    /// schede registrabili con CreateTab, factory di widget con lo stile centralizzato qui.
    ///
    /// ARCHITETTURA rev.2 — NIENTE PERSISTENZA: vive dentro il prefab VrApp, istanziato in
    /// OGNI scena, e viene ricostruita a ogni cambio area. Nessun DontDestroyOnLoad: e' la
    /// scelta che ha eliminato in blocco i bug da oggetto orfano (EventSystem e XR Interaction
    /// Manager di scena che morivano lasciando il resto agganciato al nulla).
    ///
    /// REVISIONE dopo il primo collaudo:
    ///  - contrasto rifatto (pannello scuro, pulsanti chiari CON BORDO, testo bold): la prima
    ///    versione era dark-on-dark e in visore sembrava un quadro nero;
    ///  - EnsureEventSystem garantisce UN SOLO EventSystem dotato di XRUIInputModule: adotta
    ///    quello creato da terzi (simulatore XRI, building block Meta), ne crea uno se manca,
    ///    e disattiva gli eventuali doppioni. Senza modulo di input, ray e poke non
    ///    raggiungono la UI e i pulsanti restano visibili ma inerti;
    ///  - all'avvio verifica esplicitamente i TMP Essentials: senza, i testi runtime non si
    ///    disegnano e la HUD diventa un rettangolo muto — meglio un errore chiaro in Console.
    /// </summary>
    public class VrHud : MonoBehaviour
    {
        [Header("Posizionamento (lazy follow)")]
        [Tooltip("Distanza dalla testa (m). 0.7 = a portata di POKE; il RAY funziona comunque.")]
        [SerializeField] private float distance = 0.7f;
        [Tooltip("Quota rispetto all'orizzonte dello sguardo (m). Positivo = piu' in alto.")]
        [SerializeField] private float heightOffset = -0.25f;
        [Tooltip("Angolo AZIMUTALE del pannello attorno alla verticale, in gradi: 0 = davanti, " +
                 "negativo = a sinistra, positivo = a destra. Serve a spostare la HUD fuori " +
                 "dall'asse di lavoro (per esempio a lato, mentre si punta un fusto davanti a se').")]
        [SerializeField] private float azimuthDegrees = 0f;

        [Header("Quali rotazioni della TESTA inseguire")]
        [Tooltip("IMBARDATA (girare la testa a destra/sinistra). SPENTO = il pannello si orienta " +
                 "sul CORPO del giocatore (l'XR Origin): guardandoti intorno resta dov'e', e ti " +
                 "segue solo quando ruoti col joystick o cammini. E' la modalita' piu' riposante: " +
                 "la HUD diventa un oggetto fermo nello spazio del giocatore invece di qualcosa " +
                 "che insegue lo sguardo e va 'combattuto'.")]
        [SerializeField] private bool trackHeadYaw = false;

        [Tooltip("BECCHEGGIO (alzare/abbassare lo sguardo). ACCESO = il pannello sale e scende " +
                 "con lo sguardo, restando sempre in campo visivo. Attenzione: e' proprio il " +
                 "comportamento che stanca di piu' in sessioni lunghe.")]
        [SerializeField] private bool trackHeadPitch = false;

        [Tooltip("ROLLIO (inclinare la testa di lato). ACCESO = il pannello si inclina con te, " +
                 "restando parallelo agli occhi. SPENTO = resta orizzontale rispetto al mondo, " +
                 "che e' quasi sempre preferibile perche' da' un riferimento stabile.")]
        [SerializeField] private bool trackHeadRoll = false;
        [Tooltip("Zona morta angolare: il pannello parte solo quando lo scarto supera questo (gradi).")]
        [SerializeField] private float deadZoneDegrees = 35f;
        [Tooltip("Una volta PARTITO, il pannello continua finche' lo scarto non scende sotto " +
                 "questo valore (gradi). Deve essere MOLTO piu' piccolo della zona morta: e' " +
                 "l'isteresi che elimina il parti-fermati — senza, il pannello si arresta a " +
                 "meta' strada appena rientra in zona morta e riparte al movimento successivo, " +
                 "che in visore si legge come una HUD 'ballerina'.")]
        [SerializeField] private float reAlignDegrees = 6f;
        [Tooltip("Velocita' di rotazione del pannello verso la testa. Smorzata a parte dalla " +
                 "posizione: ruotare di scatto mentre si trasla e' meta' del ballo.")]
        [SerializeField] private float turnSpeed = 6f;
        [SerializeField] private float followSpeed = 4f;
        [Tooltip("Oltre questa distanza (m) il pannello rientra di scatto (teleport, cambio scena).")]
        [SerializeField] private float snapBeyond = 3f;
        [Tooltip("Scarto di POSIZIONE (m) oltre il quale il pannello rientra. Senza questo " +
                 "controllo la zona morta guarda solo l'ANGOLO: camminando in avanti il " +
                 "pannello resta indietro nel mondo e ci resta — piccolo, illeggibile e " +
                 "spesso dietro un tronco. Era la causa vera della 'visibilita' variabile'.")]
        [SerializeField] private float positionDeadZone = 0.25f;
        [Tooltip("Congela il pannello quando una mano e' piu' vicina di cosi' (m): senza, " +
                 "sporgendosi per il POKE la testa avanza e il pannello scappa in avanti " +
                 "inseguito dal dito. 0 = mai congelare.")]
        [SerializeField] private float freezeWhenHandWithin = 0.45f;
        [Tooltip("Nomi dei controller nel rig, per trovare le mani.")]
        [SerializeField] private string leftControllerName = "Left Controller";
        [SerializeField] private string rightControllerName = "Right Controller";

        [Header("Schede")]
        [Tooltip("Titolo della scheda da aprire all'avvio. Senza questo, si apre la PRIMA che si " +
                 "registra — cioe' l'ordine dipende da quello dei componenti sull'oggetto App, " +
                 "che cambia ogni volta che se ne aggiunge uno. Vuoto = prima registrata.")]
        [SerializeField] private string defaultTab = "Areas";

        [Header("Dimensioni pannello (px a scala 0.001 = mm)")]
        [SerializeField] private Vector2 panelSize = new Vector2(440, 520);

        [Header("Sempre in primo piano")]
        [Tooltip("Forza i materiali della UI a ZTest Always e coda di rendering in fondo: la HUD " +
                 "si disegna sopra la geometria invece di essere occlusa. NON tocca camere, " +
                 "layer o EventSystem — quindi non puo' rompere l'input, a differenza della " +
                 "camera overlay. Se il pass degli splat disegna comunque dopo la coda " +
                 "trasparente, questo da' solo un miglioramento parziale: si misura, non si " +
                 "assume.")]
        [SerializeField] private bool alwaysOnTop = true;
        [Tooltip("Coda di rendering per la UI. 4000 = Overlay, l'ultima delle code standard.")]
        [SerializeField] private int uiRenderQueue = 4000;

        [Header("Stile")]
        [SerializeField] private Color panelColor     = new Color(0.09f, 0.10f, 0.12f, 0.94f);
        [SerializeField] private Color panelBorder    = new Color(0.55f, 0.58f, 0.62f, 1f);
        [SerializeField] private Color buttonColor    = new Color(0.30f, 0.33f, 0.38f, 1f);
        [SerializeField] private Color buttonBorder   = new Color(0.05f, 0.05f, 0.06f, 1f);
        [SerializeField] private Color activeColor    = new Color(0.16f, 0.62f, 0.30f, 1f);
        [SerializeField] private Color tabIdleColor   = new Color(0.16f, 0.17f, 0.20f, 1f);
        [SerializeField] private Color tabActiveColor = new Color(0.36f, 0.40f, 0.46f, 1f);

        public Color ButtonColor => buttonColor;
        public Color ActiveColor => activeColor;

        public static VrHud Instance { get; private set; }

        private Canvas canvas;
        private RectTransform tabBar;
        private RectTransform commandBar;
        private RectTransform pageArea;
        private EventSystem mine;
        private TMP_Text diagnostics;
        private float nextDiag;
        private Transform head;
        private Transform body;          // XR Origin: il "corpo" del giocatore
        private float nextBodySearch;
        private Transform handLeft, handRight;
        private float nextHandSearch;
        private bool snapNext;
        private bool following;      // isteresi: sto rientrando davanti alla testa?

        private class Tab { public Button button; public Image image; public RectTransform page; }
        private readonly Dictionary<string, Tab> tabs = new Dictionary<string, Tab>();

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            // Il piu' recente PRENDE il posto, non si suicida. Il pattern precedente
            // (if Instance != null -> Destroy(this)) presuppone che Instance sia sempre valido,
            // ma in architettura rev.2 i componenti si ricostruiscono a ogni scena: se Instance
            // punta ancora a quello morto della scena precedente, il nuovo si distruggeva da solo
            // e la classe restava senza istanza VIVA — silenziosamente, per il resto della
            // sessione. E su Quest uscire alla home SOSPENDE l'app: le statiche sopravvivono,
            // quindi nemmeno "riaprire" rimetteva le cose a posto.
            Instance = this;

            CheckTmpEssentials();
            EnsureEventSystem();
            BuildFrame();
        }

        private void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
        private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            snapNext = true;        // il player e' stato riposato: il pannello si ripresenta davanti
            EnsureEventSystem();    // l'EventSystem della scena precedente puo' essere morto con lei
        }

        private void Update()
        {
            Follow();

            // L'EventSystem va sorvegliato di continuo, non solo a sceneLoaded: gli intrusi
            // possono nascere qualche frame DOPO il caricamento della scena.
            if (Time.time >= nextDiag)
            {
                nextDiag = Time.time + 0.5f;
                EnsureEventSystem();
                EnsureEventCamera();
                if (alwaysOnTop) ApplyAlwaysOnTop();
                UpdateDiagnostics();
            }
        }

        /// ZTest Always + coda Overlay su tutti i grafici della HUD: la UI si disegna sopra
        /// la geometria 3D invece di essere occlusa da essa. Applicato periodicamente perche'
        /// i widget nascono a runtime (schede, pulsanti delle fasi future) e nascono con i
        /// materiali di default.
        ///
        /// Perche' questa strada e non la camera overlay: qui non si toccano camere, layer
        /// ne' EventSystem, quindi l'input UI non puo' rompersi. E' la lezione pagata cara.
        private void ApplyAlwaysOnTop()
        {
            foreach (var g in GetComponentsInChildren<MaskableGraphic>(true))
            {
                if (g == null) continue;

                if (g is TMP_Text tmp)
                {
                    // TMP ha i suoi shader: la proprieta' si chiama _ZTestMode (8 = Always).
                    var fm = tmp.fontMaterial;
                    if (fm != null && fm.HasProperty(ZTestModeId) &&
                        !Mathf.Approximately(fm.GetFloat(ZTestModeId), 8f))
                    {
                        fm.SetFloat(ZTestModeId, 8f);
                        fm.renderQueue = uiRenderQueue + 1;   // il testo sopra il suo sfondo
                    }
                }
                else if (g.material == null || g.material == uiTopMaterial || !g.material.HasProperty(GuiZTestId))
                {
                    if (uiTopMaterial == null) BuildTopMaterial();
                    if (uiTopMaterial != null && g.material != uiTopMaterial) g.material = uiTopMaterial;
                }
            }
        }

        private static readonly int ZTestModeId = Shader.PropertyToID("_ZTestMode");
        private static readonly int GuiZTestId  = Shader.PropertyToID("unity_GUIZTestMode");
        private Material uiTopMaterial;

        private void BuildTopMaterial()
        {
            var sh = Shader.Find("UI/Default");
            if (sh == null)
            {
                Debug.LogWarning("[VrHud] shader 'UI/Default' non trovato: niente always-on-top " +
                                 "per gli sfondi (il testo puo' funzionare comunque).");
                return;
            }
            uiTopMaterial = new Material(sh) { name = "VrHud_AlwaysOnTop" };
            uiTopMaterial.SetInt(GuiZTestId, (int)UnityEngine.Rendering.CompareFunction.Always);
            uiTopMaterial.renderQueue = uiRenderQueue;
        }

        /// L'EVENT CAMERA del canvas world-space: e' la camera con cui il raycaster UI
        /// interpreta i puntamenti. Se la HUD viene spostata su un layer dedicato che la
        /// Camera.main NON renderizza piu' (caso overlay), lasciare worldCamera a null
        /// significa ripiegare su Camera.main — che quel layer non lo vede, quindi nessun
        /// hit viene prodotto: pannello visibile e completamente sordo.
        ///
        /// Sta QUI e non nel componente overlay di proposito: cosi' la correttezza non
        /// dipende da quale versione di quel componente e' in progetto. Regola generale,
        /// valida anche senza overlay: event camera = una camera che VEDE il layer del
        /// canvas. Se Camera.main lo vede, va benissimo lei (worldCamera resta null).
        private void EnsureEventCamera()
        {
            if (canvas == null) return;
            int layer = canvas.gameObject.layer;
            var main = Camera.main;

            bool mainSeesIt = main != null && (main.cullingMask & (1 << layer)) != 0;
            if (mainSeesIt)
            {
                if (canvas.worldCamera != null && canvas.worldCamera != main)
                {
                    canvas.worldCamera = null;      // Camera.main basta e avanza
                    Debug.Log("[VrHud] event camera riportata a Camera.main (vede il layer della HUD).");
                }
                return;
            }

            // Camera.main non vede piu' il layer: serve quella che lo vede davvero.
            if (canvas.worldCamera != null && (canvas.worldCamera.cullingMask & (1 << layer)) != 0) return;

            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null || c == main) continue;
                if ((c.cullingMask & (1 << layer)) == 0) continue;
                canvas.worldCamera = c;
                Debug.Log($"[VrHud] event camera impostata su '{c.name}': e' l'unica che vede il " +
                          $"layer '{LayerMask.LayerToName(layer)}' della HUD.");
                return;
            }

            Debug.LogWarning($"[VrHud] NESSUNA camera vede il layer '{LayerMask.LayerToName(layer)}' " +
                             "della HUD: i pulsanti non potranno ricevere eventi. Riporta la HUD " +
                             "su un layer che la Camera.main renderizza.");
        }

        /// Striscia di stato SEMPRE visibile in fondo al pannello: in visore non ci sono log,
        /// e un pulsante diagnostico e' inutile proprio quando serve (cioe' quando i pulsanti
        /// non rispondono). Questa si aggiorna da sola e non richiede alcuna interazione.
        private void UpdateDiagnostics()
        {
            if (diagnostics == null) return;

            int esCount = 0;
            foreach (var es in FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (es != null) esCount++;

            bool hasModule = mine != null && mine.GetComponent<XRUIInputModule>() != null;
            var cam = Camera.main;
            var canvas = GetComponentInChildren<Canvas>();
            string eventCam = canvas == null ? "-" : (canvas.worldCamera == null ? "Camera.main" : canvas.worldCamera.name);
            string layer = canvas == null ? "-" : LayerMask.LayerToName(canvas.gameObject.layer);

            // Nomi delle schede registrate: dice a colpo d'occhio se un pannello ha davvero
            // chiamato CreateTab o se non e' mai arrivato a costruirsi.
            var names = new System.Text.StringBuilder();
            foreach (var kv in tabs) { if (names.Length > 0) names.Append(','); names.Append(kv.Key); }

            diagnostics.text =
                $"scene {SceneManager.GetActiveScene().name} · ES {esCount} · module {(hasModule ? "ok" : "MISSING")} · " +
                $"cam {(cam == null ? "NULL" : cam.name)} · eventCam {eventCam} · layer {layer}\n" +
                $"tabs [{names}]";
            diagnostics.color = (esCount == 1 && hasModule)
                ? new Color(0.6f, 1f, 0.6f, 0.9f)      // verde: configurazione sana
                : new Color(1f, 0.55f, 0.45f, 0.95f);  // rosso: ecco perche' non risponde
        }


        // ------------------------------------------------------------------ API per i pannelli

        /// <summary>
        /// La barra dei comandi SEMPRE VISIBILI, sotto le pagine e sopra la diagnostica: quello
        /// che ci metti resta a schermo qualunque scheda sia aperta.
        ///
        /// Serve per i comandi da cui dipende la possibilita' di proseguire — il ritorno all'area
        /// dalla simulazione, per esempio. Metterli dentro una scheda significa che chi ne apre
        /// un'altra si trova senza via d'uscita, e non e' una svista da correggere caso per caso:
        /// e' una categoria di comandi che non appartiene a nessuna scheda.
        /// </summary>
        public RectTransform CommandBar()
        {
            if (commandBar != null) return commandBar;

            var go = new GameObject("CommandBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(canvas.transform, false);
            commandBar = go.GetComponent<RectTransform>();
            commandBar.anchorMin = new Vector2(0, 0); commandBar.anchorMax = new Vector2(1, 0);
            commandBar.pivot = new Vector2(0.5f, 0);
            commandBar.offsetMin = new Vector2(8, 46); commandBar.offsetMax = new Vector2(-8, 110);

            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            // Le pagine si accorciano per far posto: la barra non deve coprirle.
            pageArea.offsetMin = new Vector2(0, 114);
            return commandBar;
        }

        /// <summary>Registra una scheda e restituisce la colonna dove il pannello costruisce i
        /// suoi controlli. Idempotente. La prima scheda registrata diventa quella attiva.</summary>
        public RectTransform CreateTab(string title)
        {
            if (tabs.TryGetValue(title, out var existing)) return existing.page;

            var btnGo = new GameObject($"Tab_{title}", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(tabBar, false);
            var img = btnGo.GetComponent<Image>();
            img.color = tabIdleColor;
            AddBorder(btnGo, buttonBorder);
            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredHeight = 56; le.flexibleWidth = 1;

            AddText(btnGo.transform, title, 22, FontStyles.Bold);

            var pageGo = new GameObject($"Page_{title}", typeof(RectTransform), typeof(VerticalLayoutGroup));
            pageGo.transform.SetParent(pageArea, false);
            var pageRt = pageGo.GetComponent<RectTransform>();
            pageRt.anchorMin = Vector2.zero; pageRt.anchorMax = Vector2.one;
            pageRt.offsetMin = new Vector2(16, 16); pageRt.offsetMax = new Vector2(-16, -16);
            var vlg = pageGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter; vlg.spacing = 12f;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            pageGo.SetActive(false);

            var tab = new Tab { button = btnGo.GetComponent<Button>(), image = img, page = pageRt };
            tab.button.targetGraphic = img;
            string key = title;
            tab.button.onClick.AddListener(() => SelectTab(key));
            tabs[title] = tab;

            // La scheda di partenza e' quella dichiarata, non la prima arrivata. Se non e'
            // ancora stata registrata si tiene aperta la prima, e si passa a quella dichiarata
            // appena compare: cosi' l'ordine dei componenti non conta piu'.
            if (tabs.Count == 1) SelectTab(title);
            else if (!string.IsNullOrWhiteSpace(defaultTab) && title == defaultTab) SelectTab(title);

            return pageRt;
        }

        public void SelectTab(string title)
        {
            if (!tabs.ContainsKey(title)) return;
            foreach (var kv in tabs)
            {
                bool on = kv.Key == title;
                kv.Value.page.gameObject.SetActive(on);
                kv.Value.image.color = on ? tabActiveColor : tabIdleColor;
            }
        }

        /// <summary>Pulsante standard. Ritorna (Button, Image): l'Image serve al chiamante per
        /// evidenziare lo stato (es. area attiva in verde).</summary>
        public (Button button, Image image) MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = buttonColor;
            AddBorder(go, buttonBorder);
            var le = go.AddComponent<LayoutElement>();
            // 64 px = 6.4 cm: comodo da centrare col dito. Piu' basso frustra il poke.
            // Dentro una MakeRow e' la riga a decidere l'altezza (childForceExpandHeight).
            le.preferredHeight = 64; le.flexibleWidth = 1; le.flexibleHeight = 1;

            AddText(go.transform, label, 24, FontStyles.Bold);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            // Feedback visivo del hover/press (il tint di default e' quasi invisibile).
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);
            return (btn, img);
        }

        /// <summary>
        /// Un contenitore ORIZZONTALE dentro una pagina: i widget che ci metti dentro si
        /// dividono la larghezza in parti uguali. Serve a evitare la colonna unica di pulsanti
        /// lunghi e stretti, che in visore sono difficili da centrare col ray — due o tre per
        /// riga vengono piu' quadrati e quindi piu' facili da colpire.
        /// </summary>
        public RectTransform MakeRow(Transform parent, float height = 96f)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);

            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childControlWidth = true;  hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.flexibleWidth = 1; le.flexibleHeight = 0;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Dimensioni del pannello in px di canvas — serve a chi costruisce viste
        /// che devono stare dentro la sua larghezza (per esempio una tabella).</summary>
        public Vector2 PanelSize => panelSize;

        /// <summary>Colori della cornice, per chi costruisce widget propri e vuole restare coerente.</summary>
        public Color PanelColor => panelColor;
        public Color PanelBorder => panelBorder;

        /// <summary>Etichetta standard. Ritorna il TMP_Text per aggiornamenti successivi.</summary>
        public TMP_Text MakeLabel(Transform parent, string text, float size = 20, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = size + 14; le.flexibleWidth = 1;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = align;
            t.color = new Color(1f, 1f, 1f, 0.9f);
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// Il ray sta puntando il pannello? Serve agli strumenti 3D (rilievo) per NON agire
        /// quando l'utente sta semplicemente premendo un pulsante: senza questo controllo, un
        /// colpo di grilletto sulla HUD misurerebbe anche l'albero che le sta dietro.
        ///
        /// Intersezione raggio-piano del canvas, poi verifica che il punto cada dentro il
        /// rettangolo. Nessun collider coinvolto: aggiungerne uno al pannello disturberebbe
        /// il poke e i raycast di gioco.
        /// </summary>
        public bool RayHitsPanel(Ray ray, out float distance)
        {
            distance = 0f;
            if (canvas == null || !canvas.gameObject.activeInHierarchy) return false;

            var rt = canvas.GetComponent<RectTransform>();
            if (rt == null) return false;

            var plane = new Plane(-canvas.transform.forward, canvas.transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 hit = ray.GetPoint(enter);
            Vector3 local = rt.InverseTransformPoint(hit);
            var r = rt.rect;
            if (local.x < r.xMin || local.x > r.xMax || local.y < r.yMin || local.y > r.yMax) return false;

            distance = enter;
            return true;
        }

        // ------------------------------------------------------------------ lazy follow

        private void Follow()
        {
            if (canvas == null) return;
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                head = cam.transform;
            }

            // Mano vicina = l'utente sta POKANDO: il pannello resta immobile, altrimenti
            // insegue la testa che si sporge e scappa davanti al dito.
            if (freezeWhenHandWithin > 0.01f && !snapNext && HandNearPanel()) { following = false; return; }

            // La direzione di riferimento viene dalla TESTA o dal CORPO a seconda del flag:
            // ancorarla al corpo e' cio' che rende la HUD un oggetto fermo nello spazio del
            // giocatore, che non insegue lo sguardo.
            Transform reference = trackHeadYaw ? head : (Body() != null ? Body() : head);

            Vector3 fwd = reference.forward;
            // Il beccheggio si include solo se richiesto: altrimenti si proietta sul piano
            // orizzontale e il pannello resta alla sua quota qualunque cosa guardi l'utente.
            if (!trackHeadPitch) fwd = Vector3.ProjectOnPlane(fwd, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) return;
            fwd.Normalize();

            // Direzione DESIDERATA = riferimento ruotato dell'azimut attorno alla verticale.
            // Tutto il resto (posizione voluta e isteresi) si misura rispetto a questa, mai
            // rispetto al riferimento grezzo: con un azimut di 40 gradi lo scarto varrebbe
            // sempre 40, il pannello si crederebbe eternamente disallineato e inseguirebbe
            // senza fermarsi mai.
            Vector3 desiredDir = azimuthDegrees != 0f
                ? Quaternion.AngleAxis(azimuthDegrees, Vector3.up) * fwd
                : fwd;

            Vector3 fwdFlat = Vector3.ProjectOnPlane(desiredDir, Vector3.up).normalized;
            if (fwdFlat.sqrMagnitude < 0.01f) fwdFlat = desiredDir;

            // La POSIZIONE segue sempre la testa: cosi' il pannello accompagna il giocatore sia
            // quando si sposta col joystick sia quando cammina davvero nel play space. Sono le
            // ROTAZIONI a essere filtrate dai flag, non le traslazioni.
            Vector3 target = head.position + desiredDir * distance + Vector3.up * heightOffset;

            float dist = Vector3.Distance(canvas.transform.position, head.position);
            if (snapNext || dist > snapBeyond)
            {
                canvas.transform.position = target;
                canvas.transform.rotation = FaceHead();
                snapNext = false;
                following = false;
            }
            else
            {
                Vector3 toPanel = Vector3.ProjectOnPlane(canvas.transform.position - head.position, Vector3.up);
                float angle = toPanel.sqrMagnitude < 0.02f ? 999f : Vector3.Angle(desiredDir, toPanel);
                float posError = Vector3.Distance(canvas.transform.position, target);

                // ISTERESI su ANGOLO **e** POSIZIONE: si parte se ci si e' girati troppo O se
                // ci si e' allontanati troppo (camminando), e ci si ferma solo quando ENTRAMBI
                // sono rientrati. Con il solo angolo il pannello restava indietro a metri di
                // distanza senza che nulla lo richiamasse.
                if (!following && (angle > deadZoneDegrees || posError > positionDeadZone)) following = true;
                if (following)
                {
                    canvas.transform.position = Vector3.Lerp(canvas.transform.position, target,
                                                             Time.deltaTime * followSpeed);
                    if (angle < reAlignDegrees && posError < 0.05f) following = false;
                }

                // La rotazione insegue sempre, ma con la sua costante di tempo: separarla
                // dalla posizione toglie l'oscillazione che si vedeva durante il rientro.
                canvas.transform.rotation = Quaternion.Slerp(canvas.transform.rotation, FaceHead(),
                                                             Time.deltaTime * turnSpeed);
            }
        }

        /// Il pannello guarda sempre il giocatore. Il ROLLIO invece e' opzionale: seguirlo
        /// tiene il pannello parallelo agli occhi, ignorarlo lo tiene orizzontale rispetto al
        /// mondo — che di solito e' meglio, perche' offre un riferimento stabile.
        private Quaternion FaceHead()
        {
            Vector3 up = trackHeadRoll && head != null ? head.up : Vector3.up;
            return Quaternion.LookRotation(canvas.transform.position - head.position, up);
        }

        /// Il "corpo" del giocatore: l'XR Origin. Cercato con pazienza, perche' in architettura
        /// rev.2 il rig si ricostruisce a ogni cambio scena.
        private Transform Body()
        {
            if (body != null) return body;
            if (Time.time < nextBodySearch) return null;
            nextBodySearch = Time.time + 0.5f;
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) body = origin.transform;
            return body;
        }

        private bool HandNearPanel()
        {
            if ((handLeft == null || handRight == null) && Time.time >= nextHandSearch)
            {
                nextHandSearch = Time.time + 1f;
                var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (origin != null)
                    foreach (var t in origin.GetComponentsInChildren<Transform>(true))
                    {
                        if (handLeft == null && t.name == leftControllerName) handLeft = t;
                        else if (handRight == null && t.name == rightControllerName) handRight = t;
                    }
            }
            Vector3 p = canvas.transform.position;
            if (handLeft  != null && Vector3.Distance(handLeft.position,  p) < freezeWhenHandWithin) return true;
            if (handRight != null && Vector3.Distance(handRight.position, p) < freezeWhenHandWithin) return true;
            return false;
        }

        // ------------------------------------------------------------------ infrastruttura

        /// TMP costruito a runtime senza gli Essentials = testo invisibile e HUD "muta".
        /// Meglio dirlo forte e subito che scoprirlo in visore.
        private void CheckTmpEssentials()
        {
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings == null || TMP_Settings.defaultFontAsset == null)
                Debug.LogError("[VrHud] TMP ESSENTIALS MANCANTI: i testi della HUD non verranno " +
                               "disegnati. Window -> TextMeshPro -> Import TMP Essential Resources, " +
                               "poi riavvia il Play.");
        }

        /// UN SOLO EventSystem, per tutta la vita dell'applicazione.
        ///
        /// Il problema reale osservato: l'EventSystem viene creato da TERZI (il simulatore
        /// XRI, i building block Meta) dentro la SCENA — quindi muore al primo cambio, e
        /// nessuno lo ricrea: nelle scene-area non ne resta nessuno e l'input UI e' morto
        /// fin dal primo istante. Stessa fragilita' dell'XR Interaction Manager per-scena.
        ///
        /// Strategia: ADOTTARE invece di competere. Se ne esiste uno, VrHud se lo prende in
        /// casa (riparentato sotto di se', cosi' ha lo stesso ciclo di vita della HUD) e gli
        /// garantisce l'XRUIInputModule. Ne crea uno solo se non ce n'e' nessuno. Eventuali
        /// doppioni vengono disattivati: due EventSystem attivi = input imprevedibile.
        private void EnsureEventSystem()
        {
            if (mine == null)
            {
                // 1) c'e' gia' qualcuno? Adottalo.
                var found = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Exclude);
                if (found != null)
                {
                    mine = found;
                    if (mine.transform.root != transform.root)
                    {
                        mine.transform.SetParent(transform, true);
                        Debug.Log($"[VrHud] EventSystem '{mine.name}' adottato (ora condivide " +
                                  "il ciclo di vita della HUD).");
                    }
                }
                else
                {
                    var go = new GameObject("EventSystem (VrHud)", typeof(EventSystem));
                    go.transform.SetParent(transform, false);
                    mine = go.GetComponent<EventSystem>();
                    Debug.Log("[VrHud] nessun EventSystem in scena: creato il nostro.");
                }
            }

            // 2) il modulo di input XR: senza, ray e poke non raggiungono la UI.
            if (mine.GetComponent<XRUIInputModule>() == null)
            {
                var legacy = mine.GetComponent<StandaloneInputModule>();
                if (legacy != null) legacy.enabled = false;
                mine.gameObject.AddComponent<XRUIInputModule>();
                Debug.Log("[VrHud] XRUIInputModule aggiunto all'EventSystem.");
            }

            if (!mine.gameObject.activeSelf) mine.gameObject.SetActive(true);
            if (!mine.enabled) mine.enabled = true;

            // 3) doppioni tardivi: disattivati (due EventSystem attivi = input imprevedibile).
            foreach (var es in FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (es == null || es == mine) continue;
                Debug.LogWarning($"[VrHud] EventSystem in piu' '{es.name}' (scena '{es.gameObject.scene.name}') " +
                                 "disattivato: ne deve restare uno solo.");
                es.gameObject.SetActive(false);
            }
        }

        private void BuildFrame()
        {
            var go = new GameObject("VrHudCanvas", typeof(RectTransform), typeof(Canvas),
                                    typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = panelSize;
            rt.localScale = Vector3.one * 0.001f;   // 1000 px = 1 m

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = panelColor;
            bgImg.raycastTarget = true;             // sfondo per i raggi: niente click-through
            AddBorder(bg, panelBorder);


            var barGo = new GameObject("TabBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(go.transform, false);
            tabBar = barGo.GetComponent<RectTransform>();
            tabBar.anchorMin = new Vector2(0, 1); tabBar.anchorMax = new Vector2(1, 1);
            tabBar.pivot = new Vector2(0.5f, 1);
            tabBar.offsetMin = new Vector2(8, -64); tabBar.offsetMax = new Vector2(-8, -8);
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8; hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            var pageGo = new GameObject("Pages", typeof(RectTransform));
            pageGo.transform.SetParent(go.transform, false);
            pageArea = pageGo.GetComponent<RectTransform>();
            pageArea.anchorMin = new Vector2(0, 0); pageArea.anchorMax = new Vector2(1, 1);
            pageArea.offsetMin = new Vector2(0, 44); pageArea.offsetMax = new Vector2(0, -72);

            // striscia diagnostica ancorata in fondo, fuori dalle pagine: sempre visibile,
            // qualunque scheda sia aperta.
            var diagGo = new GameObject("Diagnostics", typeof(RectTransform), typeof(TextMeshProUGUI));
            diagGo.transform.SetParent(go.transform, false);
            var diagRt = diagGo.GetComponent<RectTransform>();
            diagRt.anchorMin = new Vector2(0, 0); diagRt.anchorMax = new Vector2(1, 0);
            diagRt.pivot = new Vector2(0.5f, 0);
            diagRt.offsetMin = new Vector2(8, 4); diagRt.offsetMax = new Vector2(-8, 42);
            diagnostics = diagGo.GetComponent<TextMeshProUGUI>();
            diagnostics.fontSize = 13;
            diagnostics.alignment = TextAlignmentOptions.Center;
            diagnostics.raycastTarget = false;
            diagnostics.text = "";
        }

        /// Bordo via Outline: 2 px bastano a staccare un rettangolo dall'altro — senza,
        /// pulsanti e pannello si fondono in un unico blocco al primo colpo d'occhio.
        private static void AddBorder(GameObject go, Color c)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(2, -2);
            o.useGraphicAlpha = false;
        }

        private void AddText(Transform parent, string text, float size, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = style; t.color = Color.white;

            // Auto-dimensionamento: con i pulsanti affiancati la larghezza disponibile si
            // dimezza, e un corpo fisso sborda. Cosi' il testo si restringe quanto basta e
            // va a capo invece di uscire dal pulsante.
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = Mathf.Max(9f, size * 0.45f);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Truncate;

            t.raycastTarget = false;                // il click lo prende il pulsante, non il testo
            var r = t.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(8, 4); r.offsetMax = new Vector2(-8, -4);
        }
    }
}
