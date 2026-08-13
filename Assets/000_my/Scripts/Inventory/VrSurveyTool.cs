using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Artemis.Vr;

namespace Artemis.Inventory
{
    /// <summary>
    /// Rilievo dendrometrico in VR. Erede immersivo di InventoryTool: stessa procedura, stessa
    /// matematica (TrunkSampler + CircleFit + Hypsometry, riusati verbatim), input diverso.
    ///
    /// Procedura: si punta la BASE DEL FUSTO col ray del controller e si preme il grilletto ->
    /// TrunkSampler spara un anello di raggi a 1.30 m e adatta un cerchio -> diametro; l'altezza
    /// viene dalla curva ipsometrica; compare un marker giallo di anteprima con i valori sulla
    /// HUD -> si conferma (tasto A o pulsante HUD) o si annulla (tasto B o pulsante HUD).
    ///
    /// Modi: Misura / Marca / Rimuovi, come nel desktop (li' M/K/X, qui pulsanti sulla HUD).
    ///
    /// Note di progetto:
    ///  - i binding di input sono STRINGHE esposte in Inspector: se il layout del controller non
    ///    corrisponde si correggono senza ricompilare (e senza che io debba indovinare il nome
    ///    esatto del pulsante sul tuo runtime);
    ///  - un colpo di grilletto che sta puntando la HUD non misura nulla: il pannello ha la
    ///    precedenza, altrimenti premere un pulsante misurerebbe anche l'albero dietro di esso;
    ///  - richiede Physics.queriesHitBackfaces = true (PhysicsBootstrap): la mesh dell'area e'
    ///    specchiata (scala -1) e senza quel flag i raggi la attraversano senza colpirla.
    /// </summary>
    public class VrSurveyTool : MonoBehaviour
    {
        public enum ToolMode { Measure, Remove }

        [Header("Puntamento")]
        [Tooltip("Transform da cui parte il ray. Vuoto = si cerca 'Right Controller' nell'XR Origin.")]
        [SerializeField] private Transform rayOrigin;
        [SerializeField] private string rayOriginName = "Right Controller";
        [Tooltip("Layer del collider dell'area (terreno + fusti).")]
        [SerializeField] private LayerMask meshLayer = ~0;
        [SerializeField] private float maxRayDistance = 40f;

        [Header("Input (binding modificabili senza ricompilare)")]
        [Tooltip("PIU' percorsi per la stessa azione: il primo che il runtime riconosce vince. " +
                 "I layout dei controller variano fra OpenXR, Meta e versioni dell'Input System, " +
                 "e indovinare il nome esatto a priori non e' possibile — meglio provarne diversi.")]
        [SerializeField]
        private string[] triggerBindings =
        {
            "<XRController>{RightHand}/trigger",
            "<XRController>{RightHand}/triggerPressed",
            "<XRController>{RightHand}/triggerButton"
        };
        [Tooltip("Legge i grip DIRETTAMENTE dai dispositivi XR (UnityEngine.XR.InputDevices) " +
                 "invece che dai binding dell'Input System. Su Quest e' molto piu' affidabile: " +
                 "i percorsi di binding dipendono dal layout riconosciuto dal runtime e dalla " +
                 "versione dei pacchetti, mentre CommonUsages.gripButton e' sempre lo stesso. " +
                 "Spegnilo solo per tornare ai binding qui sotto.")]
        [SerializeField] private bool useDeviceGrips = true;

        [Tooltip("Legge anche il GRILLETTO direttamente dal dispositivo XR, come i grip. La riga " +
                 "diagnostica mostrava 'trigger binds 0', cioe' nessun percorso di binding " +
                 "riconosciuto dal runtime: invece di continuare a indovinare nomi di layout, si " +
                 "usa CommonUsages.triggerButton, che e' sempre lo stesso.")]
        [SerializeField] private bool useDeviceTrigger = true;
        [Tooltip("Soglia sopra la quale il grilletto analogico conta come premuto, usata solo se " +
                 "il dispositivo non espone il bottone digitale.")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float triggerPressPoint = 0.6f;

        [Tooltip("CONFERMA = grip DESTRO ('tengo'). Non si usa piu' il tasto A: su Quest e' " +
                 "prenotato dal menu di sistema e la pressione non arriva all'applicazione. " +
                 "I grip sono liberi perche' non ci sono oggetti afferrabili in scena.")]
        [SerializeField]
        private string[] confirmBindings =
        {
            "<XRController>{RightHand}/gripPressed",
            "<XRController>{RightHand}/grip",
            "<XRController>{RightHand}/gripButton"
        };
        [Tooltip("ANNULLA = grip SINISTRO ('scarto'). Mano diversa dalla conferma: e' molto " +
                 "difficile scartare per sbaglio una misura appena presa.")]
        [SerializeField]
        private string[] cancelBindings =
        {
            "<XRController>{LeftHand}/gripPressed",
            "<XRController>{LeftHand}/grip",
            "<XRController>{LeftHand}/gripButton"
        };

        [Header("Fit del diametro a 1.30 m")]
        [SerializeField] private float breastHeight = 1.30f;
        [SerializeField] private int rayCount = 24;
        [SerializeField] private float ringRadius = 0.75f;

        [Header("Regole di rilievo")]
        [Tooltip("Rifiuta un albero la cui base disti in pianta meno di questo (m) da uno gia' " +
                 "rilevato: impedisce di misurare due volte lo stesso fusto.")]
        [SerializeField] private float minStemSpacing = 0.5f;

        [Header("Anteprima")]
        [Tooltip("Spessore della fascia di anteprima (m). Leggermente maggiore di quella " +
                 "definitiva, cosi' si distingue a colpo d'occhio da una misura gia' confermata.")]
        [SerializeField] private float previewThickness = 0.08f;
        [SerializeField] private float previewOversize = 1.05f;
        [SerializeField] private Color previewColor = new Color(0.95f, 0.85f, 0.15f);

        public static VrSurveyTool Instance { get; private set; }

        private ToolMode mode = ToolMode.Measure;
        public ToolMode Mode => mode;

        public bool HasPending { get; private set; }
        public float PendingDbh { get; private set; }   // metri
        public float PendingHeight { get; private set; }   // metri
        public string Status { get; private set; } = "";
        public event Action OnStateChanged;

        private Vector3 pendingBase;
        private Vector3 pendingAxis;
        private GameObject previewGo;

        private InputAction triggerAction, confirmAction, cancelAction;
        private float nextOriginSearch;
        private bool prevRightGrip, prevLeftGrip;
        private bool rightGripNow, leftGripNow;
        private bool prevTrigger;
        private float triggerValueNow;

        public string StepHint => mode switch
        {
            ToolMode.Measure => HasPending ? "RIGHT grip to confirm  ·  LEFT grip to discard"
                                           : "MEASURE — aim at the stem base and pull the trigger",
            ToolMode.Remove => "REMOVE — aim at a surveyed tree to delete it",
            _ => ""
        };

        // ---- ciclo di vita -------------------------------------------------------------------

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

            triggerAction = BuildAction("SurveyTrigger", triggerBindings);
            confirmAction = BuildAction("SurveyConfirm", confirmBindings);
            cancelAction = BuildAction("SurveyCancel", cancelBindings);
        }

        /// Un'azione con PIU' binding: l'Input System ignora silenziosamente i percorsi che non
        /// corrispondono a nulla e usa quelli che esistono. Quanti ne abbia risolti davvero si
        /// legge da action.controls.Count — ed e' il primo numero da guardare quando "il tasto
        /// non funziona": zero significa binding sbagliato, non hardware muto.
        private static InputAction BuildAction(string name, string[] paths)
        {
            var a = new InputAction(name, InputActionType.Button);
            if (paths != null)
                foreach (var p in paths)
                    if (!string.IsNullOrWhiteSpace(p)) a.AddBinding(p);
            return a;
        }

        /// <summary>
        /// Riga diagnostica leggibile IN VISORE (la mostra SurveyPanel): dice se i binding sono
        /// stati risolti, se il ray origin e' stato trovato e quanti colpi di grilletto sono
        /// arrivati. Serve a distinguere due guasti che da fuori sembrano identici — "il
        /// grilletto non arriva" e "il grilletto arriva ma non c'e' nulla da misurare".
        /// </summary>
        public string Diagnostics { get; private set; } = "";

        private int triggerCount, measureAttempts, rayHits;

        private void RefreshDiagnostics()
        {
            int tc = triggerAction != null ? triggerAction.controls.Count : 0;
            string grips = useDeviceGrips
                ? $"grips dev R{(rightGripNow ? "1" : "0")}/L{(leftGripNow ? "1" : "0")}"
                : $"grips bind R{(confirmAction != null ? confirmAction.controls.Count : 0)}" +
                  $"/L{(cancelAction != null ? cancelAction.controls.Count : 0)}";

            string trig = useDeviceTrigger ? $"trigger dev {triggerValueNow:F2}" : $"trigger binds {tc}";

            Diagnostics =
                $"{trig} · {grips} · " +
                $"origin {(rayOrigin != null ? rayOrigin.name : "MISSING")} · " +
                $"pulls {triggerCount} · aims {measureAttempts} · hits {rayHits}";
        }

        private void OnEnable()
        {
            triggerAction?.Enable(); confirmAction?.Enable(); cancelAction?.Enable();
        }

        private void OnDisable()
        {
            triggerAction?.Disable(); confirmAction?.Disable(); cancelAction?.Disable();
            ClearPreview();
        }

        private void OnDestroy()
        {
            triggerAction?.Dispose(); confirmAction?.Dispose(); cancelAction?.Dispose();
            if (Instance == this) Instance = null;
        }

        // ---- input ---------------------------------------------------------------------------

        private void Update()
        {
            EnsureRayOrigin();

            bool confirmEdge, cancelEdge;
            if (useDeviceGrips)
            {
                confirmEdge = GripEdge(UnityEngine.XR.XRNode.RightHand, ref prevRightGrip, out rightGripNow);
                cancelEdge = GripEdge(UnityEngine.XR.XRNode.LeftHand, ref prevLeftGrip, out leftGripNow);
            }
            else
            {
                confirmEdge = confirmAction != null && confirmAction.WasPressedThisFrame();
                cancelEdge = cancelAction != null && cancelAction.WasPressedThisFrame();
            }

            if (HasPending)
            {
                if (confirmEdge) { ConfirmPending(); return; }
                if (cancelEdge) { CancelPending(); return; }
            }

            RefreshDiagnostics();

            bool triggerEdge = useDeviceTrigger
                ? TriggerEdge(UnityEngine.XR.XRNode.RightHand, ref prevTrigger, out triggerValueNow)
                : (triggerAction != null && triggerAction.WasPressedThisFrame());

            if (!triggerEdge) return;
            triggerCount++;                 // il grilletto E' arrivato: da qui in poi e' altro
            if (rayOrigin == null) { SetStatus("Right controller not found"); return; }

            // Il pannello ha la precedenza: un grilletto premuto mentre si punta la HUD e' un
            // click su un pulsante, non una misura dell'albero che gli sta dietro.
            var ray = new Ray(rayOrigin.position, rayOrigin.forward);
            if (VrHud.Instance != null && VrHud.Instance.RayHitsPanel(ray, out _)) return;

            Act(ray);
        }

        /// <summary>
        /// Ingresso alternativo per il grilletto, con un raggio fornito da fuori. Serve al
        /// puntatore a mouse dell'Editor: in Editor senza visore la catena XRI non si accende
        /// e il grilletto non arriva mai, quindi la HUD e gli strumenti sarebbero incollaudabili.
        /// In build questa strada non viene mai percorsa.
        /// </summary>
        public void ExternalTrigger(Ray ray)
        {
            triggerCount++;
            Act(ray);
        }

        private void Act(Ray ray)
        {
            switch (mode)
            {
                case ToolMode.Measure: OnMeasure(ray); break;
                case ToolMode.Remove: RemoveAimed(ray); break;
            }
        }

        /// Fronte di salita del grip letto dal dispositivo. Rilevare il FRONTE e non lo stato
        /// e' essenziale: tenendo premuto, uno stato "true" confermerebbe di continuo.
        private static bool GripEdge(UnityEngine.XR.XRNode node, ref bool prev, out bool now)
        {
            // Tipi qualificati per intero: UnityEngine.XR e UnityEngine.InputSystem dichiarano
            // ENTRAMBI una classe CommonUsages, e un 'using' di entrambi rende il nome ambiguo.
            now = false;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid) dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out now);
            bool edge = now && !prev;
            prev = now;
            return edge;
        }

        /// Grilletto letto dal dispositivo: prima il bottone digitale, e se il dispositivo non lo
        /// espone si ripiega sull'asse analogico con una soglia. Come per i grip, si rileva il
        /// FRONTE: tenendolo premuto non si misura in continuazione.
        private bool TriggerEdge(UnityEngine.XR.XRNode node, ref bool prev, out float value)
        {
            value = 0f;
            bool now = false;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid)
            {
                if (!dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out now))
                {
                    dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out value);
                    now = value >= triggerPressPoint;
                }
                else dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out value);
            }
            bool edge = now && !prev;
            prev = now;
            return edge;
        }

        private void EnsureRayOrigin()
        {
            if (rayOrigin != null || Time.time < nextOriginSearch) return;
            nextOriginSearch = Time.time + 0.5f;

            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
                if (t.name == rayOriginName) { rayOrigin = t; return; }
        }

        // ---- misura ---------------------------------------------------------------------------

        private void OnMeasure(Ray ray)
        {
            if (HasPending) return;

            measureAttempts++;
            if (!Physics.Raycast(ray, out var hit, maxRayDistance, meshLayer))
            {
                SetStatus("No surface hit — check Mesh Layer (PlotTerrain) and that " +
                          "PhysicsBootstrap is in the scene");
                return;
            }
            rayHits++;

            var m = TrunkSampler.MeasureDbh(hit.point, meshLayer, breastHeight, rayCount, ringRadius);
            if (!m.Ok)
            { SetStatus("Diameter fit failed — aim at a cleaner stem base"); return; }

            pendingBase = hit.point;
            // TrunkSampler restituisce il centro del cerchio adattato a quota di petto: e'
            // l'ASSE del fusto. Riportato a quota base, e' il punto su cui centrare il segno —
            // il punto cliccato invece sta sulla corteccia e darebbe un anello sbilenco.
            pendingAxis = new Vector3(m.Center.x, hit.point.y, m.Center.z);
            PendingDbh = m.Dbh;
            PendingHeight = Hypsometry.Height(m.Dbh);
            HasPending = true;
            Status = "";
            ShowPreview();
            Raise();
        }

        public void ConfirmPending()
        {
            if (!HasPending) return;
            var inv = StemInventory.Instance;
            if (inv == null) { SetStatus("No active inventory"); return; }

            // Doppione: troppo vicino in pianta a un albero gia' rilevato.
            var plan = new Vector2(pendingBase.x, pendingBase.z);
            foreach (var s in inv.Stems)
                if (Vector2.Distance(plan, s.PlanXY) < minStemSpacing)
                {
                    SetStatus($"Already surveyed nearby (#{s.StemId}) — not added");
                    ClearPending(); return;
                }

            int id = inv.AddStem(pendingBase, pendingAxis, PendingDbh, PendingHeight);
            SetStatus($"Saved #{id}  ·  {inv.Count} trees");
            ClearPending();
        }

        public void CancelPending()
        {
            if (!HasPending) return;
            ClearPending();
            SetStatus("Discarded");
        }

        // ---- marca / rimuovi --------------------------------------------------------------------

        /// Rimuove un albero GIA' SALVATO puntando il suo segno di misura. Il volume da colpire
        /// e' la capsula invisibile attorno al fusto, non la fascia sottile: si punta l'albero,
        /// non il tratto di vernice.
        private void RemoveAimed(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, maxRayDistance, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                var marker = h.collider.GetComponentInParent<StemMarker>();
                if (marker == null) continue;

                var inv = StemInventory.Instance;
                if (inv == null) return;
                inv.RemoveStem(marker.StemId);
                SetStatus($"Tree #{marker.StemId} deleted  ·  {inv.Count} left");
                return;
            }
            SetStatus("No surveyed tree under the pointer");
        }

        // ---- modo e anteprima --------------------------------------------------------------------

        public void SetMode(ToolMode m)
        {
            if (m != ToolMode.Measure) CancelPending();
            mode = m;
            Status = "";
            Raise();
        }

        /// L'anteprima ha la STESSA forma del segno definitivo (una fascia a quota di petto),
        /// solo in giallo: quello che confermi e' esattamente quello che hai visto — se fosse
        /// un'altra forma, il confronto fra anteprima e risultato non direbbe nulla.
        private void ShowPreview()
        {
            ClearPreview();
            previewGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            previewGo.name = "DbhPreview";
            var col = previewGo.GetComponent<Collider>(); if (col != null) Destroy(col);

            float d = Mathf.Max(PendingDbh, 0.02f) * previewOversize;
            previewGo.transform.SetPositionAndRotation(pendingAxis + Vector3.up * breastHeight,
                                                       Quaternion.identity);
            previewGo.transform.localScale = new Vector3(d, previewThickness * 0.5f, d);

            var r = previewGo.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = StemMarkerSpawner.MakeUnlit(previewColor, "M_DbhPreview");
        }

        private void ClearPreview() { if (previewGo != null) Destroy(previewGo); previewGo = null; }

        private void ClearPending()
        {
            HasPending = false;
            ClearPreview();
            Raise();
        }

        private void SetStatus(string s) { Status = s; Raise(); }
        private void Raise() => OnStateChanged?.Invoke();
    }
}