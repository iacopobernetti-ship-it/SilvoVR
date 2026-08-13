using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Artemis.Vr;
using Artemis.Session;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Martellata in VR: si punta un albero del soprassuolo ricostruito col ray e si preme il
    /// grilletto per segnarlo o togliergli il segno. L'abbattimento NON avviene qui — si accumula
    /// una selezione e la si esegue dalla scheda "Felling" della HUD, come nella pratica reale
    /// dove si percorre il popolamento segnando, e solo dopo si taglia.
    ///
    /// IN SESSIONE i ruoli si separano: lo STUDENTE propone (una pianta alla volta, segnalata da
    /// una bandierina del suo colore, visibile a tutti), il DOCENTE martella davvero e solo lui
    /// puo' abbattere. Le proposte passano da un RPC al server; i segni del docente li scrive lui,
    /// che il server lo e'.
    ///
    /// I colori: la BANDIERINA porta il colore di chi propone — serve a sapere chi ha proposto
    /// cosa. Il POLIGONO invece resta uguale per tutti (giallo = proposto, rosso = martellato):
    /// il poligono dice qualcosa sul BOSCO, non su chi l'ha detto, e colorarlo per proponente
    /// darebbe l'idea sbagliata che quella porzione di bosco "appartenga" a qualcuno.
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

        [SerializeField]
        private string[] triggerBindings =
        {
            "<XRController>{RightHand}/trigger",
            "<XRController>{RightHand}/triggerPressed",
            "<XRController>{RightHand}/triggerButton"
        };

        [Header("Poligoni di Voronoi")]
        [Tooltip("Colore del poligono di una pianta NON segnata.")]
        [SerializeField] private Color cellColor = new Color(0.75f, 0.78f, 0.82f, 1f);
        [Tooltip("Colore del poligono di una pianta MARTELLATA dal docente.")]
        [SerializeField] private Color markedCellColor = new Color(0.95f, 0.20f, 0.12f, 1f);
        [Tooltip("Colore del poligono di una pianta PROPOSTA da uno o piu' studenti. Uguale per " +
                 "tutti i proponenti: il colore di chi propone sta sulla bandierina.")]
        [SerializeField] private Color proposedCellColor = new Color(0.98f, 0.82f, 0.20f, 1f);

        [Header("Bandierine delle proposte")]
        [SerializeField] private float flagPoleHeight = 1.7f;
        [SerializeField] private float flagRadius = 0.65f;
        [Tooltip("Quante bandierine attorno allo stesso albero prima di sovrapporle.")]
        [SerializeField] private int flagSlots = 4;
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
        private Material cellMat, markedMat, proposedMat;
        private Transform cellRoot, flagRoot;
        private string lastNetSignature = "";

        public IReadOnlyCollection<int> Marked => marked;
        public int MarkedCount => marked.Count;
        public int ProposedCount => proposed.Count;
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
            cellMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(cellColor, "M_Cell");
            markedMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(markedCellColor, "M_CellMarked");
            proposedMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(proposedCellColor, "M_CellProposed");
            if (builder != null) builder.OnRebuilt += OnStandRebuilt;
            BuildCells();
        }

        /// Un nuovo soprassuolo (o un abbattimento) invalida i segni — gli id non esistono piu' —
        /// e ridisegna le celle, che dopo un taglio restano ma cambiano di significato.
        private void OnStandRebuilt()
        {
            marked.Clear();
            proposed.Clear();
            lastNetSignature = "";
            BuildCells();
            OnMarkingChanged?.Invoke();
        }

        // ---- input -----------------------------------------------------------------------------

        private void Update()
        {
            EnsureRayOrigin();
            SyncFromSession();
            bool edge = useDeviceTrigger ? TriggerEdge()
                                         : (triggerAction != null && triggerAction.WasPressedThisFrame());
            if (!edge) return;
            if (rayOrigin == null) { SetStatus("Right controller not found"); return; }

            var ray = new Ray(rayOrigin.position, rayOrigin.forward);
            if (VrHud.Instance != null && VrHud.Instance.RayHitsPanel(ray, out _)) return;

            Act(ray);
        }

        /// <summary>
        /// Ingresso alternativo per il grilletto, con raggio fornito da fuori: lo usa il
        /// puntatore a mouse dell'Editor, dove la catena XRI non si accende. In build non viene
        /// mai percorso.
        /// </summary>
        public void ExternalTrigger(Ray ray) => Act(ray);

        private void Act(Ray ray)
        {
            if (!Physics.Raycast(ray, out var hit, maxRayDistance, treeLayer))
            { SetStatus("No tree under the pointer"); return; }

            var tree = hit.collider.GetComponentInParent<StandTree>();
            if (tree == null) { SetStatus("No tree under the pointer"); return; }

            var st = SessionState.Instance;
            if (st != null && st.IsSpawned)
            {
                if (VrSession.IsTeacher) st.ToggleTeacherMark(tree.StemId);
                else st.RequestCandidacyRpc(tree.StemId, PlayerPalette.IndexFor(
                        Unity.Netcode.NetworkManager.Singleton.LocalClientId));
                return;                       // la vista si aggiorna quando lo stato replica
            }

            Toggle(tree.StemId);              // fuori sessione: tutto locale
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
            var st = SessionState.Instance;
            if (st != null && st.IsSpawned)
            {
                // Il turno lo registra la sessione, con il suo seme: ogni visore riapplica lo
                // stesso abbattimento e ottiene la stessa rinnovazione, compreso chi si e'
                // collegato dopo. Le proposte degli studenti cadono insieme agli alberi.
                if (!VrSession.IsTeacher) { SetStatus("only the teacher can fell"); return; }
                st.CommitMarking();
                return;
            }

            if (builder == null || marked.Count == 0) return;
            int n = marked.Count;
            var ids = new List<int>(marked);
            marked.Clear();                     // i segni cadono con gli alberi; le celle le
            builder.FellMany(ids);              // ridisegna OnRebuilt a fine abbattimento
            SetStatus($"{n} trees felled — regeneration computed");
        }

        // ---- stato condiviso ---------------------------------------------------------------------

        /// Ricalca sulla vista locale cio' che dice la sessione. Una firma a buon mercato evita
        /// di ricostruire bandierine e colori a ogni frame: si rifa' solo quando qualcosa cambia.
        private void SyncFromSession()
        {
            var st = SessionState.Instance;
            if (st == null || !st.IsSpawned) return;

            var sb = new System.Text.StringBuilder();
            foreach (var c in st.Candidacies) sb.Append(c.ClientId).Append(':').Append(c.StemId).Append(',');
            sb.Append('|');
            foreach (var m in st.TeacherMarks) sb.Append(m).Append(',');
            string sig = sb.ToString();
            if (sig == lastNetSignature) return;
            lastNetSignature = sig;

            marked.Clear();
            foreach (var m in st.TeacherMarks) marked.Add(m);

            proposed.Clear();
            foreach (var c in st.Candidacies) proposed.Add(c.StemId);

            ApplyCellColors();
            RebuildFlags(st);
            OnMarkingChanged?.Invoke();
        }

        private readonly HashSet<int> proposed = new HashSet<int>();

        /// Una bandierina per proposta, del colore del proponente, disposta attorno al fusto in
        /// slot fissi: piu' studenti possono proporre lo stesso albero e restano distinguibili.
        private void RebuildFlags(SessionState st)
        {
            if (flagRoot != null) Destroy(flagRoot.gameObject);
            if (builder == null) return;

            var rootGo = new GameObject("ProposalFlags");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            flagRoot = rootGo.transform;

            var perStem = new Dictionary<int, int>();
            foreach (var c in st.Candidacies)
            {
                if (!builder.TryGetBase(c.StemId, out var basePos)) continue;
                perStem.TryGetValue(c.StemId, out int k);
                perStem[c.StemId] = k + 1;

                float a = Mathf.PI * 2f * (k % Mathf.Max(1, flagSlots)) / Mathf.Max(1, flagSlots);
                var pos = basePos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * flagRadius;
                CreateFlag(pos, PlayerPalette.Color(c.ColorIndex));
            }
        }

        private void CreateFlag(Vector3 basePos, Color color)
        {
            var mat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(color, "M_Flag");

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var pc = pole.GetComponent<Collider>(); if (pc != null) Destroy(pc);
            pole.transform.SetParent(flagRoot, false);
            pole.transform.position = basePos + Vector3.up * (flagPoleHeight * 0.5f);
            pole.transform.localScale = new Vector3(0.05f, flagPoleHeight * 0.5f, 0.05f);
            var pr = pole.GetComponent<Renderer>(); if (pr != null) pr.sharedMaterial = mat;

            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var fc = flag.GetComponent<Collider>(); if (fc != null) Destroy(fc);
            flag.transform.SetParent(flagRoot, false);
            flag.transform.position = basePos + Vector3.up * (flagPoleHeight - 0.14f) + Vector3.right * 0.17f;
            flag.transform.localScale = new Vector3(0.34f, 0.20f, 0.03f);
            var fr = flag.GetComponent<Renderer>(); if (fr != null) fr.sharedMaterial = mat;
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
                // Tre stati, in ordine di precedenza: martellato dal docente, proposto da
                // qualcuno, normale.
                bool isMarked = marked.Contains(kv.Key);
                bool isProposed = !isMarked && proposed.Contains(kv.Key);

                lr.material = isMarked ? markedMat : (isProposed ? proposedMat : cellMat);
                lr.startColor = lr.endColor = isMarked ? markedCellColor
                                            : (isProposed ? proposedCellColor : cellColor);
                lr.widthMultiplier = (isMarked || isProposed) ? markedLineWidth : lineWidth;
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