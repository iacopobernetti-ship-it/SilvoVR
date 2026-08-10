using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Artemis.Vr;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Martellata in VR: si punta un albero del soprassuolo ricostruito col ray e si preme il
    /// grilletto per segnarlo o togliergli il segno. L'abbattimento NON avviene qui — si accumula
    /// una selezione e la si esegue dalla scheda "Felling" della HUD, come nella pratica reale
    /// dove si percorre il popolamento segnando, e solo dopo si taglia.
    ///
    /// Erede di PointerRay, ma molto piu' magro: cadono i ruoli (docente/studenti), le candidature
    /// e le scorciatoie da tastiera — tutto materiale di Fase 4 o del mondo desktop.
    ///
    /// Il segno NON e' sull'albero ma a TERRA: si accende il POLIGONO DI VORONOI della pianta.
    /// E' la scelta piu' istruttiva delle due — quello che conta in una martellata non e' il
    /// singolo fusto ma lo SPAZIO che si libera, e il poligono e' esattamente la porzione di
    /// bosco che l'albero occupa e che passera' alla rinnovazione. Segnando due piante vicine si
    /// vede subito le due celle accendersi contigue, cioe' la buca unica che si sta per aprire.
    /// </summary>
    public class SimMarkTool : MonoBehaviour
    {
        [Header("Puntamento")]
        [SerializeField] private Transform rayOrigin;
        [SerializeField] private string rayOriginName = "Right Controller";
        [Tooltip("Layer dei collider di selezione degli alberi ricostruiti.")]
        [SerializeField] private LayerMask treeLayer = ~0;
        [SerializeField] private float maxRayDistance = 120f;

        [Header("Input")]
        [Tooltip("Legge il grilletto direttamente dal dispositivo XR invece che dai binding " +
                 "dell'Input System: su Quest e' l'unico modo affidabile, i percorsi di binding " +
                 "dipendono dal layout riconosciuto dal runtime.")]
        [SerializeField] private bool useDeviceTrigger = true;
        [Range(0.1f, 0.9f)]
        [SerializeField] private float triggerPressPoint = 0.6f;

        [SerializeField] private string[] triggerBindings =
        {
            "<XRController>{RightHand}/trigger",
            "<XRController>{RightHand}/triggerPressed",
            "<XRController>{RightHand}/triggerButton"
        };

        [Header("Poligoni di Voronoi")]
        [Tooltip("Colore del poligono di una pianta NON segnata.")]
        [SerializeField] private Color cellColor = new Color(0.75f, 0.78f, 0.82f, 1f);
        [Tooltip("Colore del poligono di una pianta SEGNATA per l'abbattimento.")]
        [SerializeField] private Color markedCellColor = new Color(0.95f, 0.20f, 0.12f, 1f);
        [SerializeField] private float lineWidth = 0.10f;
        [Tooltip("Spessore del poligono segnato: oltre al colore cambia anche il tratto, cosi' " +
                 "resta leggibile anche di sbieco e da lontano.")]
        [SerializeField] private float markedLineWidth = 0.22f;
        [Tooltip("Quanto sollevare il tratto dal suolo, per non farlo sparire dentro il piano.")]
        [SerializeField] private float lift = 0.04f;

        public static SimMarkTool Instance { get; private set; }

        [SerializeField] private StandBuilder builder;

        private readonly HashSet<int> marked = new HashSet<int>();
        private readonly Dictionary<int, LineRenderer> cellLines = new Dictionary<int, LineRenderer>();
        private InputAction triggerAction;
        private float nextOriginSearch;
        private bool prevTrigger;
        private Material cellMat, markedMat;
        private Transform cellRoot;

        public IReadOnlyCollection<int> Marked => marked;
        public int MarkedCount => marked.Count;
        public string Status { get; private set; } = "";
        public event Action OnMarkingChanged;

        // ---- ciclo di vita ---------------------------------------------------------------------

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

            triggerAction = new InputAction("SimMarkTrigger", InputActionType.Button);
            foreach (var p in triggerBindings)
                if (!string.IsNullOrWhiteSpace(p)) triggerAction.AddBinding(p);
        }

        private void OnEnable() => triggerAction?.Enable();
        private void OnDisable() { triggerAction?.Disable(); ClearMarkVisuals(); }
        private void OnDestroy()
        {
            triggerAction?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (builder == null) builder = FindFirstObjectByType<StandBuilder>();
            cellMat   = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(cellColor, "M_Cell");
            markedMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(markedCellColor, "M_CellMarked");
            if (builder != null) builder.OnRebuilt += OnStandRebuilt;
            BuildCells();
        }

        /// Un nuovo soprassuolo (o un abbattimento) invalida i segni — gli id non esistono piu' —
        /// e ridisegna le celle, che dopo un taglio restano ma cambiano di significato.
        private void OnStandRebuilt()
        {
            marked.Clear();
            BuildCells();
            OnMarkingChanged?.Invoke();
        }

        // ---- input -----------------------------------------------------------------------------

        private void Update()
        {
            EnsureRayOrigin();
            bool edge = useDeviceTrigger ? TriggerEdge() 
                                         : (triggerAction != null && triggerAction.WasPressedThisFrame());
            if (!edge) return;
            if (rayOrigin == null) { SetStatus("Right controller not found"); return; }

            var ray = new Ray(rayOrigin.position, rayOrigin.forward);
            if (VrHud.Instance != null && VrHud.Instance.RayHitsPanel(ray, out _)) return;

            if (!Physics.Raycast(ray, out var hit, maxRayDistance, treeLayer))
            { SetStatus("No tree under the pointer"); return; }

            var tree = hit.collider.GetComponentInParent<StandTree>();
            if (tree == null) { SetStatus("No tree under the pointer"); return; }

            Toggle(tree.StemId);
        }

        /// Fronte di salita del grilletto letto dal dispositivo (bottone digitale, o asse
        /// analogico con soglia se il bottone non c'e').
        private bool TriggerEdge()
        {
            bool now = false;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (dev.isValid && !dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out now))
            {
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float v);
                now = v >= triggerPressPoint;
            }
            bool edge = now && !prevTrigger;
            prevTrigger = now;
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

        // ---- marcatura ---------------------------------------------------------------------------

        public void Toggle(int stemId)
        {
            if (!cellLines.ContainsKey(stemId))
            {
                SetStatus($"Tree #{stemId} has no Voronoi cell");
                return;
            }

            if (marked.Remove(stemId)) SetStatus($"Tree #{stemId} unmarked  ·  {marked.Count} marked");
            else { marked.Add(stemId); SetStatus($"Tree #{stemId} marked  ·  {marked.Count} marked"); }

            ApplyCellColors();
            OnMarkingChanged?.Invoke();
        }

        public void ClearMarks()
        {
            marked.Clear();
            ApplyCellColors();
            OnMarkingChanged?.Invoke();
        }

        /// <summary>Abbatte gli alberi segnati: e' il pulsante della scheda Felling.</summary>
        public void FellMarked()
        {
            if (builder == null || marked.Count == 0) return;
            int n = marked.Count;
            var ids = new List<int>(marked);
            marked.Clear();                     // i segni cadono con gli alberi; le celle le
            builder.FellMany(ids);              // ridisegna OnRebuilt a fine abbattimento
            SetStatus($"{n} trees felled — regeneration computed");
        }

        // ---- poligoni di Voronoi ---------------------------------------------------------------

        /// Disegna il poligono di ogni pianta ancora in piedi. Un LineRenderer per cella,
        /// riutilizzato: cambiare colore a una martellata di venti alberi non deve ricostruire
        /// nulla, solo riassegnare materiale e spessore.
        private void BuildCells()
        {
            if (cellRoot != null) Destroy(cellRoot.gameObject);
            cellLines.Clear();
            if (builder == null) return;

            var rootGo = new GameObject("VoronoiCells");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cellRoot = rootGo.transform;

            float y = builder.GroundY + lift;

            foreach (var rec in builder.ResidualStems)
            {
                if (!builder.TryGetCell(rec.StemId, out var cell)) continue;
                if (cell == null || cell.Count < 3) continue;

                var go = new GameObject($"Cell_{rec.StemId}");
                go.transform.SetParent(cellRoot, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.material = cellMat;
                lr.loop = true; lr.useWorldSpace = true;
                lr.widthMultiplier = lineWidth;
                lr.positionCount = cell.Count;
                for (int i = 0; i < cell.Count; i++)
                    lr.SetPosition(i, new Vector3(cell[i].x, y, cell[i].y));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                cellLines[rec.StemId] = lr;
            }

            ApplyCellColors();
        }

        private void ApplyCellColors()
        {
            foreach (var kv in cellLines)
            {
                var lr = kv.Value;
                if (lr == null) continue;
                bool isMarked = marked.Contains(kv.Key);
                lr.material = isMarked ? markedMat : cellMat;
                lr.startColor = lr.endColor = isMarked ? markedCellColor : cellColor;
                lr.widthMultiplier = isMarked ? markedLineWidth : lineWidth;
            }
        }

        private void ClearMarkVisuals()
        {
            if (cellRoot != null) Destroy(cellRoot.gameObject);
            cellRoot = null;
            cellLines.Clear();
        }

        private void SetStatus(string s) { Status = s; OnMarkingChanged?.Invoke(); }
    }
}
