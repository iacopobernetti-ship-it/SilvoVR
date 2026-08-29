using System;
using System.Collections.Generic;
using UnityEngine;
using Artemis.Inventory;

namespace Artemis.Regeneration
{
    /// <summary>A generated young regeneration plant (record; instantiated from this).</summary>
    public struct YoungPlant
    {
        public int ModelIndex;
        public Vector3 Position;   // world XZ at ground; y = ground
        public float RotationY;
        public float TargetHeight; // metres; the plant is uniformly scaled to this
        public int ParentStemId;
    }

    /// <summary>
    /// Rebuilds a stand from a saved inventory (generated soil + walls + lighting + adult firs +
    /// Voronoi), and runs the local felling → regeneration mechanic: fell a tree, spawn x young
    /// in its Voronoi cell (random model / rotation / scale, spaced, kept off the borders). The
    /// inventory is read-only; felling lives in a separate state so Rebuild restores everything.
    /// Felling generates YoungPlant RECORDS then instantiates them — ready to be networked later.
    /// </summary>
    public class StandBuilder : MonoBehaviour
    {
        [Header("Assets")]
        [Tooltip("Catalogo dei modelli di pianta (adulto + giovane per specie). Uno solo: la " +
                 "doppia versione basso/alto dettaglio del desktop e' stata rimossa perche' su " +
                 "Quest il dettaglio alto non e' un'opzione praticabile, e un sottosistema che " +
                 "nessuno usera' e' solo un modo per sbagliare.")]
        [SerializeField] private PlantCatalog catalog;

        [SerializeField] private Material soilMaterial;
        [SerializeField] private Transform standRoot;
        [SerializeField] private LayerMask groundLayer;

        [Header("Source inventory")]
        [SerializeField] private bool buildOnStart = true;

        [Header("Ground (flat soil square)")]
        [SerializeField] private float buffer = 5f;
        [SerializeField] private float thickness = 0.2f;
        [SerializeField] private float tileMeters = 2f;
        [SerializeField] private bool autoGroundY = true;      // flatten to the stems' lowest base
        [SerializeField] private float groundY = 0f;
        [Tooltip("Costruisce un piano vuoto all'avvio della scena, anche senza alcun inventario " +
                 "caricato. Da tenere ATTIVO: il player entra in Simulation gia' in piedi e libero " +
                 "di muoversi, quindi senza un pavimento cadrebbe nel vuoto — e non si sceglie un " +
                 "inventario dalla lista mentre si sta precipitando. E' cosa diversa da " +
                 "'Build On Start', che invece carica anche un inventario (ripiegando sul primo " +
                 "file trovato su disco, cosa che qui non si vuole).")]
        [SerializeField] private bool buildEmptyPlaneOnStart = true;
        [Tooltip("Lato del piano vuoto iniziale, in metri. Deve bastare a contenere il punto di " +
                 "spawn con margine.")]
        [SerializeField] private float emptyPlaneSide = 40f;

        [Header("Player")]
        [Tooltip("Dopo ogni ricostruzione dello stand, riporta il player al centro del popolamento. " +
                 "Serve al cambio inventario: rilievi di aree diverse stanno a coordinate molto " +
                 "distanti, quindi restando fermo il player finirebbe fuori dal nuovo piano.")]
        [SerializeField] private bool repositionPlayerOnBuild = true;
        [Tooltip("Quanto sopra il piano posare il player (m).")]
        [SerializeField] private float playerGroundOffset = 0.2f;
        [Tooltip("Lo spawn pad usato da SpawnPointBinder in questa scena: viene spostato anch'esso " +
                 "al centro del popolamento, cosi' resta un riferimento valido invece che un punto " +
                 "fisso buono solo per il primo inventario caricato.")]
        [SerializeField] private Transform spawnPad;

        [Header("Boundary walls (invisible)")]
        [SerializeField] private bool generateWalls = true;
        [SerializeField] private float wallHeight = 30f;
        [SerializeField] private float wallThickness = 0.5f;

        [Header("Stand indices")]
        [SerializeField] private float plotAreaM2 = 400f;

        [Header("Lighting")]
        [SerializeField] private bool generateLighting = true;
        [SerializeField] private Color sunColor = new Color(1f, 0.96f, 0.88f);
        [SerializeField] private float sunIntensity = 1.5f;
        [SerializeField] private float sunElevation = 55f;
        [SerializeField] private float sunAzimuth = 40f;
        [SerializeField] private Color ambientColor = new Color(0.55f, 0.60f, 0.65f);
        [SerializeField] private float ambientIntensity = 1.1f;

        [Header("Interramento (sink)")]
        [Tooltip("Usato SOLO per le specie che non hanno ancora un youngSinkDepth proprio nel " +
                 "catalogo (valore -1): in quel caso il sink del giovane e' quello dell'adulto " +
                 "moltiplicato per questo fattore. Compilando il campo nel catalogo, questo non " +
                 "viene piu' consultato per quella specie.")]
        [SerializeField] private float youngSinkFallbackFactor = 0.25f;

        [Header("Selection")]
        [Tooltip("Layer of the (invisible) selection colliders on each tree.")]
        [SerializeField] private LayerMask treeLayer;
        [Tooltip("Fattore di allargamento del cilindro di selezione usato per le specie che non " +
                 "ne dichiarano uno proprio nel catalogo (valore -1). E' li' che va regolato modello " +
                 "per modello: l'inclinazione del fusto e' cotta nella mesh, quindi ogni prefab " +
                 "richiede la sua tolleranza.")]
        [SerializeField] private float selectionRadiusFactor = 2.5f;
        [Tooltip("Raggio minimo del cilindro di selezione (m): garantisce un bersaglio utilizzabile " +
                 "anche sugli alberi piu' sottili.")]
        [SerializeField] private float selectionRadiusMin = 0.35f;

        [Header("Felling -> regeneration")]
        [SerializeField] private int youngPerFelling = 8;
        [Tooltip("Minimum distance between young plants (m).")]
        [SerializeField] private float youngSpacing = 0.8f;
        [Tooltip("Keep young at least this far from the cell borders (m).")]
        [SerializeField] private float cellMargin = 1.0f;
        [Tooltip("Target height of a young plant (m); each is scaled uniformly to this, ± spread.")]
        [SerializeField] private float youngHeight = 1.5f;
        [Range(0f, 0.5f)] [Tooltip("Random height spread, e.g. 0.10 = ±10%.")]
        [SerializeField] private float youngHeightSpread = 0.10f;

        [Header("Regeneration model (FIS)")]
        [Tooltip("If off, uses the fixed Young Per Felling instead of the fuzzy model.")]
        [SerializeField] private bool useFIS = true;
        [Tooltip("Young plants per m² of gap at suitability = 1.")]
        [SerializeField] private float maxYoungDensity = 0.4f;
        [Tooltip("Gap area -> relative light mapping (two points).")]
        [SerializeField] private float lightMinArea = 6f;
        [SerializeField] private float lightMinPct = 8f;
        [SerializeField] private float lightMaxArea = 60f;
        [SerializeField] private float lightMaxPct = 55f;
        [Range(0f, 1f)] [Tooltip("Station structural diversity (not derivable from geometry).")]
        [SerializeField] private float stationDiversity = 0.5f;
        [Range(0f, 1f)] [Tooltip("Aridity used if the FutureClimate API has no data yet.")]
        [SerializeField] private float fallbackAridity = 0.4f;

        [Header("Debug")]
        [SerializeField] private bool drawVoronoi = true;
        [SerializeField] private float gizmoLift = 0.15f;

        private readonly Dictionary<int, GameObject> trees = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> selectors = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, int> cellOf = new Dictionary<int, int>();
        private readonly HashSet<int> felled = new HashSet<int>();
        private readonly List<YoungPlant> youngRecords = new List<YoungPlant>();
        private readonly List<GameObject> youngGOs = new List<GameObject>();
        private readonly List<int> validYoung = new List<int>();

        private GameObject ground, sun, walls;
        private List<StemRecord> current = new List<StemRecord>();
        private List<Vector2> sites = new List<Vector2>();
        private List<List<Vector2>> cells = new List<List<Vector2>>();
        private Vector2 squareCenter; private float squareSide; private float builtGroundY;

        public string CurrentInventoryName { get; private set; } = "";
        public float PlotAreaM2 => plotAreaM2;
        public Vector2 SquareCenter => squareCenter;
        public float SquareSide => squareSide;
        public event Action OnRebuilt;

        // Last FIS evaluation, exposed for the HUD documentation panel.
        public float LastLightPct { get; private set; }
        public float LastAridity { get; private set; }

        // ---- clima condiviso (Fase 4) ------------------------------------------------------
        private bool hasSharedClimate;
        private float sharedAridity;
        private string sharedScenario = "";
        private int sharedStart, sharedEnd;

        /// <summary>Scenario e aridita' ricevuti dalla sessione. Da quel momento il FIS usa
        /// questi e non piu' la propria interrogazione all'API.</summary>
        public void SetSharedClimate(string scenario, int startYear, int endYear, float aridity01)
        {
            sharedScenario = scenario ?? "";
            sharedStart = startYear; sharedEnd = endYear;
            sharedAridity = Mathf.Clamp01(aridity01);
            hasSharedClimate = true;
        }

        public bool HasSharedClimate => hasSharedClimate;
        public string SharedScenario => sharedScenario;
        public string SharedPeriod => $"{sharedStart}-{sharedEnd}";
        public float SharedAridity => sharedAridity;
        public float LastResidualGha { get; private set; }
        public float LastSuitability { get; private set; }
        public string LastLimiting { get; private set; } = "-";
        public bool UseFIS => useFIS;
        public float StationDiversity => stationDiversity;

        private RegenerationEvaluator fis;
        private RegenerationEvaluator Fis => fis ?? (fis = new RegenerationEvaluator());

        public IReadOnlyList<StemRecord> OriginalStems => current;
        public int FelledCount => felled.Count;
        public int YoungCount => youngGOs.Count;
        public float GroundY => builtGroundY;
        /// Flat ground level (kept as a method so callers don't need to change).
        public float SampleGround(float x, float z) => builtGroundY;
        public IReadOnlyList<List<Vector2>> Cells => cells;

        public bool TryGetCell(int stemId, out List<Vector2> cell)
        {
            cell = null;
            if (cellOf.TryGetValue(stemId, out int i) && i >= 0 && i < cells.Count) { cell = cells[i]; return true; }
            return false;
        }

        public bool TryGetBase(int stemId, out Vector3 pos)
        {
            foreach (var s in current) if (s.StemId == stemId) { pos = new Vector3(s.Base.x, builtGroundY, s.Base.z); return true; }
            pos = default; return false;
        }

        /// Atomic: base position AND Voronoi cell for the same stem, guaranteed consistent.
        public bool TryGetPlot(int stemId, out Vector3 basePos, out List<Vector2> cell)
        {
            basePos = default; cell = null;
            if (!cellOf.TryGetValue(stemId, out int i)) return false;
            if (i < 0 || i >= cells.Count || i >= current.Count) return false;
            var rec = current[i];
            if (rec.StemId != stemId) return false;                 // guard against any drift
            basePos = new Vector3(rec.Base.x, builtGroundY, rec.Base.z);
            cell = cells[i];
            return cell != null && cell.Count >= 3;
        }

        public List<StemRecord> ResidualStems
        {
            get { var l = new List<StemRecord>(); foreach (var s in current) if (!felled.Contains(s.StemId)) l.Add(s); return l; }
        }

        private void Start()
        {
            // L'area di provenienza la ricorda AreaFlow: si entra in Simulation DA un'area, e il
            // soprassuolo e' quello del suo inventario. Non c'e' nulla da scegliere ne' da
            // digitare — se non si viene da nessuna parte, resta il solo pavimento.
            // IN SESSIONE, chi non e' il docente NON costruisce dai propri file: il soprassuolo
            // della lezione e' quello del docente e arriva replicato. Senza questo controllo ogni
            // studente costruirebbe il PROPRIO bosco (dai propri rilievi) e lo vedrebbe sostituire
            // un istante dopo — o, peggio, resterebbe con quello se la pubblicazione tardasse:
            // due persone convinte di guardare lo stesso bosco, che invece e' diverso.
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.IsListening && !nm.IsServer)
            {
                Debug.Log("[StandBuilder] studente in sessione: attendo il soprassuolo del docente.");
                CurrentAreaId = Artemis.Vr.AreaFlow.OriginArea;
                if (buildEmptyPlaneOnStart) Build(new List<StemRecord>());
                return;
            }

            string origin = Artemis.Vr.AreaFlow.OriginArea;
            if (!string.IsNullOrWhiteSpace(origin)) { BuildFromArea(origin); return; }

            Debug.LogWarning("[StandBuilder] nessuna area di provenienza: costruisco solo il piano. " +
                             "Si entra in Simulation da un'area, non direttamente.");
            if (buildEmptyPlaneOnStart) Build(new List<StemRecord>());
        }

        public void Rebuild() => BuildFromArea(CurrentAreaId);

        /// Nominal plot area comes from the session (it varies with the plot).
        public void SetPlotArea(float m2) { if (m2 > 1f) plotAreaM2 = m2; }

        /// Build from records received over the network instead of a local file.
        public void BuildShared(List<StemRecord> stems, string name)
        {
            CurrentInventoryName = name ?? "";
            Build(stems);
        }

        /// <summary>
        /// Costruisce il soprassuolo dall'inventario di UN'AREA. Nella versione VR non esistono
        /// inventari con nome: ogni area ha il suo unico file, quindi l'identita' dello stand e'
        /// l'area stessa — niente elenchi, niente "ultimo usato", niente ripieghi sul primo file
        /// trovato su disco (che nel desktop poteva far comparire il popolamento di un'altra area).
        /// </summary>
        public void BuildFromArea(string plotId)
        {
            CurrentAreaId = plotId ?? "";
            CurrentInventoryName = CurrentAreaId;   // conservato: la martellata lo scrive nel file
            var stems = string.IsNullOrEmpty(CurrentAreaId)
                ? new List<StemRecord>()
                : InventoryStore.Load(CurrentAreaId);
            Debug.Log($"[StandBuilder] area '{CurrentAreaId}': {stems.Count} alberi dall'inventario.");
            Build(stems);
        }

        /// <summary>L'area da cui proviene il soprassuolo attualmente costruito.</summary>
        public string CurrentAreaId { get; private set; } = "";

        public void Build(List<StemRecord> stems)
        {
            Clear();
            current = stems ?? new List<StemRecord>();
            RebuildValidYoung();

            ComputeSquare();
            GenerateGround();
            GenerateWalls();
            GenerateLighting();
            foreach (var s in current) SpawnAdult(s);
            RecomputeVoronoi();

            RepositionPlayerToStandCentre();

            OnRebuilt?.Invoke();
        }

        /// <summary>
        /// Puts the local player in the middle of the stand after a rebuild. Loading a different
        /// inventory replaces the whole stand, and inventories surveyed on different plots sit at
        /// very different world coordinates: the player would otherwise stay where it was, which
        /// after the swap is somewhere off the new soil square entirely.
        ///
        /// The target is the CENTRE OF THE BOUNDING BOX of the stems, not their mean position. The
        /// mean is pulled towards whichever corner happens to be densest, which can land the player
        /// near an edge; the bounding-box centre is the geometric middle of the stand and — being
        /// exactly what the soil square is built around — is guaranteed to have ground under it.
        ///
        /// Only the LOCAL player is moved: every client rebuilds its own stand from the same shared
        /// records, so each one repositions itself. No RPC involved.
        /// </summary>
        private void RepositionPlayerToStandCentre()
        {
            if (!repositionPlayerOnBuild) return;

            var target3 = new Vector3(squareCenter.x, builtGroundY + playerGroundOffset, squareCenter.y);

            // Il pad va spostato comunque, anche se il player non c'e' ancora: sara' lui il
            // riferimento usato da SpawnPointBinder quando il player comparira'.
            if (spawnPad != null) spawnPad.position = target3;

            // In VR il giocatore e' il rig XR della scena, non un NetworkObject: si sposta la
            // RADICE dell'XR Origin. (Il ramo Netcode del desktop rientrera' in Fase 4, quando la
            // sessione esistera' davvero — oggi importarlo qui costerebbe una dipendenza da un
            // pacchetto che il progetto non ha nemmeno installato.)
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;

            var cc = origin.GetComponent<CharacterController>();
            bool had = cc != null && cc.enabled;
            if (had) cc.enabled = false;      // altrimenti sovrascrive la scrittura su position
            origin.transform.position = target3;
            if (had) cc.enabled = true;

            Debug.Log($"[StandBuilder] rig riposizionato al centro del popolamento: {target3}");
        }

        public void Clear()
        {
            foreach (var kv in trees) if (kv.Value != null) Destroy(kv.Value);
            foreach (var kv in selectors) if (kv.Value != null) Destroy(kv.Value);
            foreach (var g in youngGOs) if (g != null) Destroy(g);
            trees.Clear(); selectors.Clear(); cellOf.Clear();
            felled.Clear(); youngRecords.Clear(); youngGOs.Clear();
            if (ground != null) Destroy(ground); ground = null;
            if (sun != null) Destroy(sun); sun = null;
            if (walls != null) Destroy(walls); walls = null;
            sites.Clear(); cells.Clear();
        }

        // ---------- felling -> regeneration ----------

        public void Fell(int stemId) => FellMany(new[] { stemId });

        // ---------- martellata capture / save ----------

        public bool HasMartellata => felled.Count > 0;

        /// Capture the current felled state (trees + gap cells + FIS parameters) for saving/replay.
        public MartellataData BuildMartellata()
        {
            // L'area E' l'identita' della martellata: quella da cui lo stand e' stato costruito.
            // Nel desktop questo campo veniva inseguito fra sessione, inventario e PlotContext, e
            // bastava che tutti e tre tacessero per produrre martellate senza area, non piu'
            // attribuibili a nulla. Qui la fonte e' una sola e non puo' mancare.
            var d = new MartellataData
            {
                inventoryName = CurrentAreaId,
                plotId = CurrentAreaId
            };

            if (hasSharedClimate)
            { d.scenario = sharedScenario; d.startYear = sharedStart; d.endYear = sharedEnd; }
            else
            {
                var c = FutureClimateClient.Instance;
                if (c != null) { d.scenario = c.Scenario; d.startYear = c.StartYear; d.endYear = c.EndYear; }
            }

            d.lightPct = LastLightPct; d.aridity = LastAridity; d.residualGha = LastResidualGha;
            d.diversity = StationDiversity; d.suitability = LastSuitability; d.limiting = LastLimiting;

            foreach (var id in felled)
            {
                foreach (var s in current)
                    if (s.StemId == id)
                    {
                        // L'ASSE del fusto, non il punto cliccato: quello sta sulla corteccia, e
                        // un anello centrato li' risulta TANGENTE all'albero invece che attorno —
                        // lo stesso difetto gia' corretto sui segni di misura in area. Lo scarto
                        // e' un raggio, cioe' 15-20 cm su un fusto da 35: si vede benissimo.
                        // MarkAnchor ripiega su Base per gli inventari vecchi, che l'asse non
                        // ce l'hanno.
                        var a = s.MarkAnchor;
                        d.felled.Add(new MartellataData.FelledTree { stemId = id, x = a.x, z = a.z, dbh = s.Dbh });
                        break;
                    }

                if (cellOf.TryGetValue(id, out int idx) && idx >= 0 && idx < cells.Count && cells[idx] != null)
                {
                    var gc = new MartellataData.GapCell { stemId = id };
                    gc.polygon.AddRange(cells[idx]);
                    d.gaps.Add(gc);
                }
            }
            return d;
        }

        /// <summary>Salva la martellata come UNICA dell'area: sovrascrive la precedente.</summary>
        public void SaveMartellata() => MartellataStore.Save(CurrentAreaId, BuildMartellata());

        /// <summary>
        /// Salva la martellata anche quando si esce dalla scena senza premere "Back to plot".
        ///
        /// Perche' serve: quel pulsante e' del DOCENTE, e lo studente non lo preme mai — e' il
        /// docente a portare via la classe. Risultato: sul visore dello studente il file della
        /// martellata non nasceva affatto, e tornando in area il visualizzatore diceva
        /// correttamente "nessuna martellata salvata". Il dato pero' ce l'ha eccome: ha
        /// riapplicato gli stessi turni con lo stesso seme, quindi il suo BuildMartellata()
        /// produce esattamente lo stesso contenuto di quello del docente. Mancava solo di
        /// scriverlo.
        ///
        /// Vale la regola di sempre — MAI una martellata vuota sopra una piena: senza abbattimenti
        /// non si tocca il file, cosi' un giro a vuoto in Simulation non cancella il lavoro fatto
        /// prima.
        /// </summary>
        private void OnDestroy()
        {
            if (felled.Count == 0 || string.IsNullOrWhiteSpace(CurrentAreaId)) return;
            SaveMartellata();
            Debug.Log($"[StandBuilder] uscita dalla simulazione: martellata di '{CurrentAreaId}' " +
                      $"salvata in locale ({felled.Count} alberi).");
        }

        /// <summary>Restore a saved marking: reload its inventory (or rebuild), then fell its trees.</summary>
        public void ApplyMartellata(MartellataData d)
        {
            if (d == null) return;
            if (!string.IsNullOrWhiteSpace(d.plotId) && d.plotId != CurrentAreaId) BuildFromArea(d.plotId);
            else Rebuild();

            var ids = new List<int>();
            foreach (var t in d.felled) ids.Add(t.stemId);
            if (ids.Count > 0) FellMany(ids);
        }

        /// <summary>
        /// Fell a set of marked trees as one martellata: group them into GAPS (contiguous Voronoi
        /// cells), evaluate the regeneration FIS once per gap (light from the gap area, aridity from
        /// FutureClimate, residual G, station diversity), and spawn young = suitability × density ×
        /// area, distributed across the gap's cells proportionally to their area.
        /// </summary>
        /// Seeded overload: all clients must produce identical young-plant positions.
        public void FellMany(IEnumerable<int> stemIds, int seed)
        {
            var state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seed);
            try { FellMany(stemIds); }
            finally { UnityEngine.Random.state = state; }
        }

        public void FellMany(IEnumerable<int> stemIds)
        {
            var valid = new List<int>();
            foreach (var id in stemIds) if (!felled.Contains(id) && cellOf.ContainsKey(id)) valid.Add(id);
            if (valid.Count == 0) return;

            var adj = Voronoi2D.Adjacency(cells);
            var gaps = GroupGaps(valid, adj);

            // remove felled trees first so the residual basal area excludes them
            foreach (var id in valid)
            {
                felled.Add(id);
                if (trees.TryGetValue(id, out var t) && t != null) Destroy(t); trees.Remove(id);
                if (selectors.TryGetValue(id, out var s) && s != null) Destroy(s); selectors.Remove(id);
            }

            float residualGha = StandMetrics.Compute(ResidualStems, plotAreaM2).BasalAreaHa;
            // In sessione il clima e' quello PUBBLICATO dal docente: gli studenti non interrogano
            // l'API — dieci visori sullo stesso endpoint sono dieci richieste identiche — e
            // soprattutto devono valutare il FIS sullo stesso numero, altrimenti la stessa
            // martellata darebbe rinnovazioni diverse su schermi diversi.
            float aridity = hasSharedClimate ? sharedAridity
                : (FutureClimateClient.Instance != null && FutureClimateClient.Instance.HasData)
                    ? FutureClimateClient.Instance.Aridity01 : fallbackAridity;
            LastAridity = aridity; LastResidualGha = residualGha;   // for the HUD

            foreach (var gap in gaps)
            {
                float totalArea = 0f;
                foreach (var idx in gap) totalArea += Voronoi2D.Area(cells[idx]);
                if (totalArea < 0.01f) continue;

                int totalYoung;
                if (useFIS)
                {
                    float light = AreaToLight(totalArea);
                    var res = Fis.Evaluate(light, aridity, residualGha, stationDiversity);
                    totalYoung = Mathf.RoundToInt(res.Value * maxYoungDensity * totalArea);
                    LastLightPct = light; LastSuitability = res.Value; LastLimiting = res.Limiting;
                }
                else totalYoung = youngPerFelling;

                foreach (var idx in gap)
                {
                    float a = Voronoi2D.Area(cells[idx]);
                    int count = Mathf.RoundToInt(totalYoung * (a / totalArea));
                    foreach (var rec in GenerateYoungInCell(idx, StemOfCell(idx), count))
                    {
                        youngRecords.Add(rec);
                        var go = InstantiateYoung(rec);
                        if (go != null) youngGOs.Add(go);
                    }
                }
            }
            OnRebuilt?.Invoke();
        }

        private List<List<int>> GroupGaps(List<int> validStems, List<HashSet<int>> adj)
        {
            var cellIdxs = new HashSet<int>();
            foreach (var id in validStems) if (cellOf.TryGetValue(id, out int i)) cellIdxs.Add(i);

            var visited = new HashSet<int>();
            var gaps = new List<List<int>>();
            foreach (var start in cellIdxs)
            {
                if (visited.Contains(start)) continue;
                var gap = new List<int>();
                var stack = new Stack<int>(); stack.Push(start); visited.Add(start);
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); gap.Add(c);
                    foreach (var nb in adj[c])
                        if (cellIdxs.Contains(nb) && !visited.Contains(nb)) { visited.Add(nb); stack.Push(nb); }
                }
                gaps.Add(gap);
            }
            return gaps;
        }

        private int StemOfCell(int idx) => (idx >= 0 && idx < current.Count) ? current[idx].StemId : -1;

        private float AreaToLight(float area)
        {
            float t = (area - lightMinArea) / Mathf.Max(0.01f, lightMaxArea - lightMinArea);
            return Mathf.Lerp(lightMinPct, lightMaxPct, Mathf.Clamp01(t));
        }

        private void RebuildValidYoung()
        {
            validYoung.Clear();
            if (catalog == null) return;
            for (int i = 0; i < catalog.Count; i++)
                if (catalog.species[i] != null && catalog.species[i].young != null) validYoung.Add(i);
        }

        private List<YoungPlant> GenerateYoungInCell(int cellIdx, int parentStemId, int count)
        {
            var recs = new List<YoungPlant>();
            if (count <= 0 || cellIdx < 0 || cellIdx >= cells.Count || validYoung.Count == 0) return recs;
            var cell = cells[cellIdx];
            if (cell == null || cell.Count < 3) return recs;

            Vector2 min = cell[0], max = cell[0];
            foreach (var v in cell) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }

            var placed = new List<Vector2>();
            int attempts = 0, maxAttempts = count * 40;
            while (placed.Count < count && attempts < maxAttempts)
            {
                attempts++;
                var p = new Vector2(UnityEngine.Random.Range(min.x, max.x), UnityEngine.Random.Range(min.y, max.y));
                if (!Voronoi2D.PointInPolygon(p, cell)) continue;
                if (Voronoi2D.DistanceToEdges(p, cell) < cellMargin) continue;
                bool ok = true;
                foreach (var q in placed) if (Vector2.Distance(p, q) < youngSpacing) { ok = false; break; }
                if (!ok) continue;

                placed.Add(p);
                int model = validYoung[UnityEngine.Random.Range(0, validYoung.Count)];
                recs.Add(new YoungPlant
                {
                    ModelIndex = model,
                    Position = new Vector3(p.x, builtGroundY, p.y),
                    RotationY = UnityEngine.Random.value * 360f,
                    TargetHeight = youngHeight * (1f + UnityEngine.Random.Range(-youngHeightSpread, youngHeightSpread)),
                    ParentStemId = parentStemId
                });
            }
            return recs;
        }

        private GameObject InstantiateYoung(YoungPlant rec)
        {
            if (catalog == null || rec.ModelIndex < 0 || rec.ModelIndex >= catalog.Count) return null;
            var entry = catalog.species[rec.ModelIndex];
            if (entry == null || entry.young == null) return null;

            var parent = standRoot != null ? standRoot : transform;
            var go = Instantiate(entry.young, parent);
            go.transform.rotation = Quaternion.Euler(0f, rec.RotationY, 0f);

            float native = MeasureHeight(go);
            float scale = native > 0.001f ? rec.TargetHeight / native : 1f;
            go.transform.localScale = Vector3.one * scale;

            // Interramento dei GIOVANI, distinto da quello degli adulti: l'apparato radicale di una
            // piantina e' molto piu' superficiale, e affondarla quanto un albero maturo la fa
            // sparire nel terreno. Vedi youngSinkFactor.
            AlignAndSeat(go, rec.Position.x, rec.Position.z, entry.YoungSink(youngSinkFallbackFactor) * scale);
            return go;
        }

        // ---------- geometry ----------

        private void ComputeSquare()
        {
            if (current.Count == 0)
            {
                // Piano vuoto: centrato sull'origine e abbastanza ampio da reggere il player allo
                // spawn. Il vecchio 2*buffer+1 dava un quadratino di pochi metri, sufficiente come
                // segnaposto ma non come pavimento su cui trovarsi a camminare.
                squareCenter = Vector2.zero;
                squareSide = Mathf.Max(emptyPlaneSide, 2f * buffer + 1f);
                builtGroundY = groundY;
                return;
            }

            Vector2 min = current[0].PlanXY, max = current[0].PlanXY; float minY = current[0].Base.y;
            foreach (var s in current)
            {
                min = Vector2.Min(min, s.PlanXY); max = Vector2.Max(max, s.PlanXY);
                if (s.Base.y < minY) minY = s.Base.y;
            }
            squareCenter = (min + max) * 0.5f;
            squareSide = Mathf.Max(max.x - min.x, max.y - min.y) + 2f * buffer;
            builtGroundY = autoGroundY ? minY : groundY;
        }

        private void GenerateGround()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "BareSoil";
            ground.layer = LayerFromMask(groundLayer);
            if (standRoot != null) ground.transform.SetParent(standRoot, true);
            ground.transform.localScale = new Vector3(squareSide, thickness, squareSide);
            ground.transform.position = new Vector3(squareCenter.x, builtGroundY - thickness * 0.5f, squareCenter.y);

            if (soilMaterial != null)
            {
                var mr = ground.GetComponent<Renderer>();
                var inst = new Material(soilMaterial);
                float tiles = Mathf.Max(1f, squareSide / Mathf.Max(0.1f, tileMeters));
                var st = new Vector2(tiles, tiles);
                inst.mainTextureScale = st;
                if (inst.HasProperty("_BaseMap")) inst.SetTextureScale("_BaseMap", st);
                mr.material = inst;
            }
        }

        private static int LayerFromMask(LayerMask mask)
        {
            int m = mask.value;
            for (int i = 0; i < 32; i++) if ((m & (1 << i)) != 0) return i;
            return 0;
        }

        private void GenerateLighting()
        {
            if (!generateLighting) return;
            sun = new GameObject("SimSun");
            if (standRoot != null) sun.transform.SetParent(standRoot, true);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional; light.color = sunColor; light.intensity = sunIntensity; light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor * ambientIntensity;
        }

        private void GenerateWalls()
        {
            if (!generateWalls) return;
            walls = new GameObject("BoundaryWalls");
            if (standRoot != null) walls.transform.SetParent(standRoot, true);
            float half = squareSide * 0.5f, cx = squareCenter.x, cz = squareCenter.y, cy = builtGroundY + wallHeight * 0.5f;
            int gl = LayerFromMask(groundLayer);
            AddWall("Wall_N", new Vector3(cx, cy, cz + half), new Vector3(squareSide + wallThickness, wallHeight, wallThickness), gl);
            AddWall("Wall_S", new Vector3(cx, cy, cz - half), new Vector3(squareSide + wallThickness, wallHeight, wallThickness), gl);
            AddWall("Wall_E", new Vector3(cx + half, cy, cz), new Vector3(wallThickness, wallHeight, squareSide + wallThickness), gl);
            AddWall("Wall_W", new Vector3(cx - half, cy, cz), new Vector3(wallThickness, wallHeight, squareSide + wallThickness), gl);
        }

        private void AddWall(string name, Vector3 pos, Vector3 size, int layer)
        {
            var w = new GameObject(name); w.transform.SetParent(walls.transform, false); w.transform.position = pos; w.layer = layer;
            w.AddComponent<BoxCollider>().size = size;
        }

        // ---------- reconstruction ----------

        private void SpawnAdult(StemRecord s)
        {
            if (catalog == null || catalog.Count == 0) return;
            var entry = catalog.Pick(s.StemId);
            if (entry == null || entry.adult == null) return;

            var parent = standRoot != null ? standRoot : transform;
            var go = Instantiate(entry.adult, parent);
            go.transform.SetPositionAndRotation(new Vector3(s.Base.x, builtGroundY, s.Base.z), Quaternion.identity);

            float native = MeasureHeight(go);
            float target = Mathf.Max(s.Height, 0.5f);
            float scale = native > 0.001f ? target / native : 1f;
            go.transform.localScale = Vector3.one * scale;

            var rng = new System.Random(s.StemId);
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            Vector3 collarPos = AlignAndSeat(go, s.Base.x, s.Base.z, entry.sinkDepth * scale);
            trees[s.StemId] = go;

            // Capsula invisibile di selezione, posizionata sul marcatore "centro" del prefab.
            //
            // Il marcatore e' messo a mano nel modello, nel punto in cui si vuole la capsula: e'
            // esatto per definizione e non dipende da come e' costruita la mesh. Stimarlo dalla
            // geometria (media dei vertici in una fascia di altezza) si e' rivelato inaffidabile,
            // perche' il colletto radicale di questi modelli e' vistoso ed eccentrico e la
            // rotazione casuale attorno a Y ne sposta il baricentro in modo diverso per ogni albero.
            var sel = new GameObject($"Sel_{s.StemId}");
            if (standRoot != null) sel.transform.SetParent(standRoot, true);
            // X e Z dal marcatore "centro" (l'asse del fusto), Y dal colletto (il livello del
            // suolo): la capsula copre l'albero dal punto in cui esce da terra fino alla cima.
            // La quota del marcatore nel prefab e' una scelta libera e qui non entra.
            sel.transform.position = new Vector3(collarPos.x, collarPos.y + target * 0.5f, collarPos.z);
            sel.layer = LayerFromMask(treeLayer);
            var cap = sel.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.height = target;
            cap.radius = Mathf.Max(s.Dbh * 0.5f * entry.SelectionFactor(selectionRadiusFactor),
                                   selectionRadiusMin);
            var tag = sel.AddComponent<StandTree>(); tag.StemId = s.StemId;
            selectors[s.StemId] = sel;
        }

        private static float MeasureHeight(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return 0f;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.size.y;
        }

        // Align using a child marker named "centro" at the trunk collar: land it on (x,z) and on
        // the ground (sunk). Falls back to bounds if the marker is absent.
        /// <summary>Positions the model and returns the world position of the "centro" marker —
        /// the point the prefab itself declares as the trunk base. The selection capsule is built
        /// on this value, so marker and capsule agree by construction.</summary>
        private Vector3 AlignAndSeat(GameObject go, float x, float z, float sinkScaled)
        {
            float g = SampleGround(x, z);
            var centro = FindChild(go.transform, "centro");
            if (centro != null)
            {
                // X e Z dal marcatore: dice dove passa l'asse del fusto.
                Vector3 c = centro.position;
                go.transform.position += new Vector3(x - c.x, 0f, z - c.z);

                // La QUOTA invece NON viene dal marcatore. "centro" serve a dichiarare DOVE PASSA
                // L'ASSE del fusto, e puo' essere collocato a qualunque altezza risulti comoda nel
                // modello. Appoggiando quel punto sul terreno l'albero veniva sollevato di tutta
                // la sua altezza dal colletto, restando sospeso con le radici per aria.
                //
                // Per sedere l'albero serve il punto piu' basso della sua geometria, meno
                // l'interramento dichiarato nel catalogo: e' quello che fa SeatOnGround.
                SeatOnGround(go, g, sinkScaled);

                // Si restituiscono X e Z del marcatore — l'asse del fusto — ma con la quota del
                // COLLETTO, cioe' il livello del suolo. La Y del marcatore e' arbitraria (sta dove
                // si e' scelto di metterlo nel prefab) e non serve a nessuno: la capsula deve
                // partire da dove il tronco esce da terra, non da meta' fusto.
                return new Vector3(centro.position.x, g, centro.position.z);
            }

            // Nessun marcatore "centro" nel prefab: si ripiega su una stima geometrica, meno
            // precisa. Vedi RecenterXZ.
            RecenterXZ(go, x, z);
            SeatOnGround(go, g, sinkScaled);
            return new Vector3(x, g, z);
        }

        private static Transform FindChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (string.Equals(c.name, childName, StringComparison.OrdinalIgnoreCase)) return c;
                var found = FindChild(c, childName);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Fallback for prefabs WITHOUT a "centro" marker: centres the model on (x,z) using the
        /// mean of the vertices between 2 % and 10 % of its height.
        ///
        /// It is only an approximation — that band also contains the root flare, which on these
        /// models is broad and off-centre, so the estimate drifts from the real stem axis by an
        /// amount that varies with the tree's random Y rotation. Add a "centro" marker to the
        /// prefab, positioned where the selection capsule should stand, and this path is not used
        /// at all.
        /// </summary>
        private Vector3 RecenterXZ(GameObject go, float x, float z)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return go.transform.position;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float y0 = b.min.y + b.size.y * 0.02f, y1 = b.min.y + b.size.y * 0.10f;
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var verts = mf.sharedMesh.vertices; var tr = r.transform;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 w = tr.TransformPoint(verts[i]);
                    if (w.y >= y0 && w.y <= y1) { sum += w; count++; }
                }
            }
            Vector3 axis = count > 0 ? sum / count : b.center;
            go.transform.position += new Vector3(x - axis.x, 0f, z - axis.z);
            return new Vector3(x, axis.y, z);
        }

        private void SeatOnGround(GameObject go, float groundHeight, float sink)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                go.transform.position += Vector3.up * ((groundHeight - b.min.y) - sink);
            }
            else { var p = go.transform.position; p.y = groundHeight - sink; go.transform.position = p; }
        }

        // ---------- voronoi ----------

        private void RecomputeVoronoi()
        {
            sites.Clear();
            foreach (var s in current) sites.Add(s.PlanXY);
            cells = Voronoi2D.Compute(sites, squareCenter, squareSide);

            cellOf.Clear();
            for (int i = 0; i < current.Count; i++)
            {
                cellOf[current[i].StemId] = i;
                if (selectors.TryGetValue(current[i].StemId, out var sel) && sel != null)
                {
                    var t = sel.GetComponent<StandTree>();
                    if (t != null) t.CellIndex = i;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawVoronoi || cells == null) return;
            float y = builtGroundY + gizmoLift;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            foreach (var cell in cells)
            {
                if (cell == null || cell.Count < 2) continue;
                for (int i = 0; i < cell.Count; i++)
                {
                    Vector2 a = cell[i], b = cell[(i + 1) % cell.Count];
                    Gizmos.DrawLine(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y));
                }
            }
            Gizmos.color = Color.yellow;
            foreach (var s in sites) Gizmos.DrawSphere(new Vector3(s.x, y, s.y), 0.15f);
        }

        /// <summary>
        /// Riporta il POPOLAMENTO a prima della martellata, lasciando stare tutto il resto.
        ///
        /// Perche' non si ricostruisce da capo (Build/BuildShared): il quadrato di suolo nasce dal
        /// bounding box degli steli dell'INVENTARIO, e il ripristino rimette in piedi proprio
        /// quell'inventario — centro, lato e quota sarebbero identici a quelli di adesso. Rifare
        /// suolo, muri e luce per ottenerli uguali e' lavoro sprecato che porta con se' tre
        /// effetti collaterali veri: il collider del piano sparisce per un frame (e in quel frame
        /// la gravita' lavora indisturbata: e' la caduta nel vuoto vista dopo il reset), il rig
        /// viene riposizionato al centro anche se il giocatore stava tranquillo altrove, e la
        /// classe perde il confronto "prima/dopo" proprio nel momento in cui serve guardarlo.
        ///
        /// Le celle di Voronoi non si ricalcolano per necessita' ma per pulizia: sono gia'
        /// costruite su `current`, cioe' sull'inventario intero, e non sono mai cambiate con
        /// l'abbattimento. La chiamata serve solo a riassegnare CellIndex ai selettori nuovi.
        ///
        /// Fa scattare OnRebuilt perche' chi disegna sopra il soprassuolo (SimMarkTool per i
        /// poligoni, MapPanel per la mappa) deve ridisegnare: gli alberi tornati in piedi non
        /// avevano piu' una cella disegnata. Effetto collaterale accettato: anche XrRigPlacer
        /// ascolta quell'evento e riporta il rig al centro del quadrato — non e' piu' pericoloso
        /// (il suolo non e' mai stato distrutto e la quota viene dal builder), ma se in aula
        /// risultasse fastidioso il rimedio e' separare l'evento "il suolo e' cambiato" da
        /// "il soprassuolo e' cambiato".
        /// </summary>
        public void RestoreStand()
        {
            if (felled.Count == 0 && youngGOs.Count == 0) return;

            // 1. via la rinnovazione: e' nata dall'abbattimento che stiamo annullando.
            foreach (var g in youngGOs) if (g != null) Destroy(g);
            youngGOs.Clear();
            youngRecords.Clear();

            // 2. rimettere in piedi gli abbattuti. I record non se ne sono mai andati: `current`
            //    e' l'inventario, e l'abbattimento toglie l'ALBERO, non il dato.
            int restored = 0;
            foreach (var s in current)
            {
                if (!felled.Contains(s.StemId)) continue;
                if (trees.ContainsKey(s.StemId)) continue;   // gia' in piedi: non duplicare
                SpawnAdult(s);
                restored++;
            }
            felled.Clear();

            // 3. selettori nuovi -> CellIndex da riassegnare.
            RecomputeVoronoi();

            // 4. gli indici dell'ultima valutazione FIS non descrivono piu' nulla di esistente:
            //    azzerarli evita che la HUD continui a mostrare la luce di una buca richiusa.
            LastLightPct = 0f;
            LastSuitability = 0f;
            LastLimiting = "-";
            LastResidualGha = StandMetrics.Compute(ResidualStems, plotAreaM2).BasalAreaHa;

            Debug.Log($"[StandBuilder] soprassuolo ripristinato: {restored} alberi rimessi in piedi, " +
                      "rinnovazione rimossa, suolo intatto.");

            OnRebuilt?.Invoke();
        }
    }
}
