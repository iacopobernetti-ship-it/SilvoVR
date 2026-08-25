using System.Collections.Generic;
using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Ripropone la martellata dell'area CORRENTE sopra il Gaussian Splatting reale: un anello e
    /// una X su ogni albero abbattuto, e il perimetro della cella di Voronoi disegnato a terra.
    ///
    /// Le quote arrivano dal terreno vero, campionato col raycast: la simulazione lavora su un
    /// suolo piatto, il bosco reale e' un versante, ma le coordinate IN PIANTA sono le stesse —
    /// ed e' per questo che la martellata salvata non contiene alcuna Y.
    ///
    /// Niente piantine di rinnovazione, per scelta: qui si torna a vedere DOVE si e' tagliato e
    /// quanto spazio si e' aperto, mentre il risultato della rinnovazione si guarda dentro la
    /// simulazione, dove e' calcolato.
    ///
    /// Erede diretto di MartellataViewer, alleggerito: niente catalogo, niente semina, niente
    /// gestione di nomi — una sola martellata per area, l'ultima salvata.
    /// </summary>
    public class MartellataViewerVR : MonoBehaviour
    {
        [Header("Terreno reale")]
        [Tooltip("Layer del collider del terreno (PlotTerrain).")]
        [SerializeField] private LayerMask terrainLayer;
        [SerializeField] private float rayLength = 200f;

        [Header("Alberi abbattuti")]
        [SerializeField] private Color cutColor = new Color(1f, 0.25f, 0.12f, 1f);
        [SerializeField] private float breastHeight = 1.30f;
        [Tooltip("Raggio dell'anello = (diametro/2) x questo fattore.")]
        [SerializeField] private float ringWidthFactor = 1.35f;
        [SerializeField] private float ringMinRadius = 0.22f;
        [SerializeField] private float ringThickness = 0.06f;
        [SerializeField] private float blazeSize = 0.5f;

        [Header("Buche di rinnovazione")]
        [Tooltip("Disegna anche il perimetro delle celle di Voronoi. SPENTO di default: sopra il " +
                 "Gaussian Splatting quei tratti litigano con la nuvola — le linee non scrivono " +
                 "profondita' e finiscono davanti a tutto, dando una lettura spaziale falsa. " +
                 "I poligoni si guardano dove sono calcolati, cioe' in Simulation.")]
        [SerializeField] private bool showGapOutlines = false;
        [SerializeField] private Color gapColor = new Color(0.25f, 0.9f, 0.35f, 1f);
        [SerializeField] private float outlineWidth = 0.14f;
        [Tooltip("Quanto sollevare il perimetro dal suolo, per non farlo sparire dentro il terreno.")]
        [SerializeField] private float outlineLift = 0.08f;
        [Tooltip("Punti per lato del perimetro: il poligono viene infittito perche' su un versante " +
                 "un segmento fra due soli vertici taglierebbe dentro la collina.")]
        [SerializeField] private int outlineSubdivisions = 6;

        public static MartellataViewerVR Instance { get; private set; }

        private Transform root;
        private Material cutMat, gapMat;

        public bool IsShowing => root != null;
        public int ShownTrees { get; private set; }
        public int ShownGaps { get; private set; }
        public string Status { get; private set; } = "";
        public event System.Action OnStateChanged;

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
        }

        private void Start()
        {
            cutMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(cutColor, "M_Cut");
            gapMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(gapColor, "M_Gap");
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ---- comandi -------------------------------------------------------------------------

        /// <summary>Mostra la martellata dell'area corrente; se e' gia' visibile, la nasconde.</summary>
        public void Toggle(string plotId)
        {
            if (IsShowing) { Hide(); return; }
            Show(plotId);
        }

        public void Show(string plotId)
        {
            Hide();

            var data = MartellataStore.Load(plotId);
            if (data == null)
            {
                SetStatus($"no marking saved for {plotId} yet");
                return;
            }

            var go = new GameObject($"Martellata_{plotId}");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            root = go.transform;

            foreach (var t in data.felled) BuildFellingMarkers(t);
            if (showGapOutlines) foreach (var g in data.gaps) BuildGapOutline(g.polygon);

            ShownTrees = data.felled.Count;
            ShownGaps = data.gaps.Count;
            SetStatus($"{ShownTrees} felled  ·  {ShownGaps} gaps  ·  " +
                      $"{data.scenario} {data.startYear}-{data.endYear}  ·  suitability {data.suitability:F2}");
        }

        public void Hide()
        {
            if (root != null) Destroy(root.gameObject);
            root = null;
            ShownTrees = ShownGaps = 0;
            SetStatus("");
        }

        // ---- alberi abbattuti: anello + X ------------------------------------------------------

        private void BuildFellingMarkers(MartellataData.FelledTree t)
        {
            float radius = Mathf.Max(t.dbh * 0.5f * ringWidthFactor, ringMinRadius);

            // Il suolo si campiona ATTORNO al fusto, non attraverso.
            //
            // Da quando la martellata salva l'ASSE del fusto invece del punto cliccato sulla
            // corteccia, la verticale in (x, z) passa dentro il tronco: il "punto piu' basso"
            // lungo quella retta puo' essere l'interno del guscio, la base del tronco, o niente
            // affatto — e in quel caso si ripiegava su y = 0, cioe' sottoterra, dove il marker
            // esiste ma non lo vede nessuno. Campionando su una corona di raggio maggiore del
            // fusto si tocca il terreno vero, che e' quello che serve.
            float groundY = SampleGroundAround(t.x, t.z, Mathf.Max(t.dbh, 0.4f));
            var breast = new Vector3(t.x, groundY + breastHeight, t.z);

            BuildRing(breast, radius);
            BuildBlaze(breast, radius);
        }

        private void BuildRing(Vector3 center, float radius)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(root, false);
            go.transform.position = center;

            var lr = go.AddComponent<LineRenderer>();
            lr.material = cutMat;
            lr.startColor = lr.endColor = cutColor;
            lr.widthMultiplier = ringThickness;
            lr.loop = true; lr.useWorldSpace = false;
            const int seg = 40;
            lr.positionCount = seg;
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// Due barre incrociate che guardano sempre il giocatore: una X piatta sarebbe illeggibile
        /// da tre quarti, ed e' proprio da li' che di solito la si guarda camminando.
        private void BuildBlaze(Vector3 center, float radius)
        {
            var go = new GameObject("Blaze");
            go.transform.SetParent(root, false);
            go.transform.position = center;

            // La X sta sulla SUPERFICIE rivolta a chi guarda, non al centro geometrico: al centro
            // finirebbe DENTRO il fusto, e da quando la mesh del rilievo fa da occlusore
            // depth-only il tronco la nasconde per davvero — cosa che prima non accadeva solo
            // perche' nulla occludeva nulla. Il raggio glielo si passa perche' e' esattamente di
            // quanto va spostata in avanti.
            var bb = go.AddComponent<BillboardVR>();
            bb.Anchor = center;
            bb.SurfaceOffset = radius;

            for (int k = 0; k < 2; k++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = bar.GetComponent<Collider>(); if (col != null) Destroy(col);
                bar.transform.SetParent(go.transform, false);
                bar.transform.localScale = new Vector3(blazeSize, blazeSize * 0.2f, 0.02f);
                bar.transform.localRotation = Quaternion.Euler(0, 0, k == 0 ? 45f : -45f);
                var r = bar.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = cutMat;
            }
        }

        // ---- perimetro della buca ----------------------------------------------------------------

        private void BuildGapOutline(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return;

            var go = new GameObject("GapOutline");
            go.transform.SetParent(root, false);
            go.transform.position = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.material = gapMat;
            lr.startColor = lr.endColor = gapColor;
            lr.widthMultiplier = outlineWidth;
            lr.loop = true; lr.useWorldSpace = true;

            // Ogni lato viene suddiviso e ogni punto appoggiato al terreno reale: su un versante
            // un segmento diritto fra due vertici passerebbe sottoterra a meta' strada.
            int sub = Mathf.Max(1, outlineSubdivisions);
            var pts = new List<Vector3>(poly.Count * sub);
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                for (int k = 0; k < sub; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)sub);
                    pts.Add(new Vector3(p.x, SampleGround(p.x, p.y) + outlineLift, p.y));
                }
            }

            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) lr.SetPosition(i, pts[i]);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- terreno --------------------------------------------------------------------------------

        /// Suolo = superficie PIU' BASSA lungo la verticale, sondata in entrambe le direzioni: il
        /// collider dell'area copre anche chiome e tronchi, e un raggio solo verso il basso
        /// appoggerebbe i marker sulle cime degli alberi.
        /// <summary>
        /// Quota del terreno attorno a un fusto: si sonda su una corona di punti a distanza
        /// <paramref name="avoid"/> dall'asse e si tiene il piu' basso, cosi' il tronco stesso
        /// non entra mai nella misura. Se nessun punto della corona colpisce, si ripiega sulla
        /// verticale centrale — meglio una quota discutibile che un marker a quota zero.
        /// </summary>
        private float SampleGroundAround(float x, float z, float avoid)
        {
            const int probes = 6;
            float lowest = float.MaxValue;
            for (int i = 0; i < probes; i++)
            {
                float a = Mathf.PI * 2f * i / probes;
                float y = SampleGround(x + Mathf.Cos(a) * avoid, z + Mathf.Sin(a) * avoid);
                if (y != 0f && y < lowest) lowest = y;
            }
            if (lowest != float.MaxValue) return lowest;

            float centre = SampleGround(x, z);
            if (centre == 0f)
                Debug.LogWarning($"[MartellataViewerVR] nessun terreno sotto ({x:F1}, {z:F1}): " +
                                 "controlla il Terrain Layer del componente.");
            return centre;
        }

        private float SampleGround(float x, float z)
        {
            var up = Physics.RaycastAll(new Vector3(x, -rayLength, z), Vector3.up, rayLength * 2f, terrainLayer);
            var down = Physics.RaycastAll(new Vector3(x, rayLength, z), Vector3.down, rayLength * 2f, terrainLayer);

            float lowest = float.MaxValue;
            for (int i = 0; i < up.Length; i++) if (up[i].point.y < lowest) lowest = up[i].point.y;
            for (int i = 0; i < down.Length; i++) if (down[i].point.y < lowest) lowest = down[i].point.y;
            return lowest == float.MaxValue ? 0f : lowest;
        }

        private void SetStatus(string s) { Status = s; OnStateChanged?.Invoke(); }
    }

    /// <summary>Si orienta verso il giocatore a ogni frame (per la X).</summary>
    public class BillboardVR : MonoBehaviour
    {
        /// <summary>Centro dell'ancoraggio (l'asse del fusto). Se resta a zero si usa la
        /// posizione iniziale dell'oggetto, cosi' vecchi usi continuano a funzionare.</summary>
        public Vector3 Anchor;

        /// <summary>Di quanto scostarsi dall'ancora VERSO chi guarda: serve a posare il segno
        /// sulla superficie del fusto invece che nel suo centro, dove sarebbe sepolto.</summary>
        public float SurfaceOffset;

        private Camera cam;
        private bool anchored;

        private void Awake()
        {
            if (Anchor == Vector3.zero) Anchor = transform.position;
            anchored = true;
        }

        private void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector3 toCam = cam.transform.position - Anchor;
            toCam.y = 0f;                                   // il segno resta alla sua quota
            if (toCam.sqrMagnitude < 0.0001f) return;
            toCam.Normalize();

            if (anchored && SurfaceOffset > 0.001f)
                transform.position = Anchor + toCam * SurfaceOffset;

            transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
        }
    }
}
