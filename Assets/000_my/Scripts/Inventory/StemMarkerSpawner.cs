using System.Collections.Generic;
using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>Etichetta: un raycast sul marker risale al record dell'albero.</summary>
    public class StemMarker : MonoBehaviour
    {
        public int StemId;
    }

    /// <summary>
    /// Disegna il segno di misura su ogni albero rilevato, imitando la pratica dendrometrica:
    /// una FASCIA ORIZZONTALE a 1.30 m attorno al fusto, come il tratto di vernice che si da'
    /// in bosco all'altezza di cavallettamento. Il diametro della fascia e' quello misurato, e
    /// il suo centro e' l'ASSE del fusto ricavato dal fit — non il punto cliccato, che sta
    /// sulla corteccia e produrrebbe un anello sbilenco.
    ///
    /// Rosso = rilevato (segno di misura), arancio = marcato (candidato al taglio), come nella
    /// martellata reale dove i due segni sono distinti.
    ///
    /// Struttura di ogni marker:
    ///   root  — StemMarker + CapsuleCollider ALTA E INVISIBILE: la fascia e' sottile e col ray
    ///           sarebbe quasi impuntabile, quindi il volume da colpire e' un cilindro di 1.6 m
    ///           attorno al fusto, mentre cio' che si VEDE resta il solo segno;
    ///   Band  — la striscia sottile visibile, senza collider.
    ///
    /// LA FASCIA NON E' PIU' UN CILINDRO PRIMITIVO. Il cilindro di Unity ha i TAPPI, e un tappo
    /// visto dall'alto e' un disco pieno: con gli occhi a ~1.65 m e il segno a 1.30 m lo si
    /// guarda sempre dall'alto, tanto piu' aperto quanto piu' si e' vicini — da cui i "dischi"
    /// verdi in visore, mentre da una vista orizzontale (la finestra Game sul PC) lo stesso
    /// oggetto sembrava corretto. Una pennellata vera non ha tappo: qui la fascia e' la sola
    /// SUPERFICIE LATERALE, generata come mesh.
    ///
    /// La striscia e' DOUBLE-SIDED per GEOMETRIA (ogni quad e' scritto due volte, con winding
    /// opposto) e non per materiale: '_Cull' esiste su URP/Unlit ma non sui fallback della
    /// cascata, e affidarsi a una proprieta' che potrebbe non esserci e' lo stesso genere di
    /// fragilita' che con Shader.Find e' gia' costata cara. Serve perche' attraversando la
    /// fascia — o guardandola dal lato interno dove sopravanza il fusto — una faccia sola
    /// sparirebbe.
    ///
    /// La mesh e' UNA SOLA, statica e condivisa da tutti i marker: raggio 0.5 e altezza 1, cosi'
    /// la scala del transform da' direttamente diametro e spessore in metri. Generare una mesh
    /// a runtime e' sicuro (a differenza degli shader, le mesh non vengono rimosse dalla build:
    /// qui la si costruisce vertice per vertice, non la si cerca).
    ///
    /// I materiali si generano con una cascata di shader (URP/Unlit -> URP/Lit -> Unlit ->
    /// Sprites): CreatePrimitive assegnava il Default-Material della pipeline Built-in, che in
    /// URP non esiste e diventa magenta acceso. Nessun materiale da preparare in editor.
    /// </summary>
    public class StemMarkerSpawner : MonoBehaviour
    {
        [Header("Segno di misura")]
        [Tooltip("Quota del segno: la stessa del rilievo (1.30 m, altezza di petto).")]
        [SerializeField] private float breastHeight = 1.30f;
        [Tooltip("Spessore verticale della fascia in metri — un tratto di pennello, non una banda.")]
        [SerializeField] private float bandThickness = 0.06f;
        [Tooltip("Quanto la fascia sopravanza il diametro misurato. 1.03 = 3% in piu', cosi' " +
                 "circonda la corteccia invece di sparirci dentro.")]
        [SerializeField] private float bandOversize = 1.03f;
        [Tooltip("Segmenti della circonferenza. 32 e' gia' liscio su un fusto; alzarlo costa " +
                 "poco perche' la mesh e' una sola per tutti i marker.")]
        [SerializeField] private int bandSegments = 32;

        [Header("Colori")]
        [Tooltip("Albero rilevato: rosso, come il segno di cavallettamento.")]
        [SerializeField] private Color unmarkedColor = new Color(0.85f, 0.10f, 0.10f);
        [Tooltip("Albero marcato per il taglio: arancio, distinto dal segno di misura.")]
        [SerializeField] private Color markedColor = new Color(0.98f, 0.55f, 0.05f);

        [Header("Puntamento")]
        [Tooltip("Altezza del volume invisibile da colpire col ray (m).")]
        [SerializeField] private float pickHeight = 1.6f;
        [Tooltip("Raggio minimo del volume da colpire (m): un fusto sottile resta comunque " +
                 "puntabile senza doverci prendere la mira al centimetro.")]
        [SerializeField] private float minPickRadius = 0.15f;

        private readonly Dictionary<int, GameObject> markers = new Dictionary<int, GameObject>();
        private StemInventory bound;
        private static Material unmarkedMat, markedMat;
        private static Mesh bandMesh;
        private static int bandMeshSegments;

        // ---- ciclo di vita ---------------------------------------------------------------------

        /// <summary>
        /// I segni di misura esistono SOLO in un'area di saggio.
        ///
        /// Questo componente vive nel prefab VrApp, quindi esiste anche in Base e in Simulation,
        /// dove disegna qualunque cosa StemInventory abbia caricato — e StemInventory carica il
        /// file che porta il nome della scena. Basta un inv_Simulation.json finito su disco per
        /// sbaglio (una misura confermata in Simulation, quando i due strumenti si contendevano
        /// il grilletto) e da quel momento un anello compare in mezzo al bosco simulato a ogni
        /// ingresso, sempre nello stesso punto, su quel visore soltanto. Il file su disco resta,
        /// ma non lo guarda piu' nessuno.
        /// </summary>
        private bool ActiveHere
        {
            get
            {
                var flow = Artemis.Vr.AreaFlow.Instance;
                return flow == null || flow.IsOnArea;
            }
        }

        private void Update()
        {
            if (!ActiveHere)
            {
                if (bound != null) { bound.OnInventoryChanged -= Rebuild; bound = null; ClearAll(); }
                return;
            }

            var inv = StemInventory.Instance;
            if (inv == bound) return;

            if (bound != null) bound.OnInventoryChanged -= Rebuild;
            bound = inv;
            if (bound != null) { bound.OnInventoryChanged += Rebuild; Rebuild(); }
        }

        private void OnDisable()
        {
            if (bound != null) bound.OnInventoryChanged -= Rebuild;
            bound = null;
            ClearAll();
        }

        // ---- costruzione ---------------------------------------------------------------------

        private void Rebuild()
        {
            var inv = StemInventory.Instance;
            if (inv == null) { ClearAll(); return; }

            var alive = new HashSet<int>();
            foreach (var rec in inv.Stems)
            {
                alive.Add(rec.StemId);
                if (!markers.TryGetValue(rec.StemId, out var go) || go == null)
                {
                    go = BuildMarker(rec.StemId);
                    markers[rec.StemId] = go;
                }
                Apply(go, rec);
            }

            var stale = new List<int>();
            foreach (var kv in markers) if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale) { if (markers[id] != null) Destroy(markers[id]); markers.Remove(id); }
        }

        private GameObject BuildMarker(int stemId)
        {
            var root = new GameObject($"StemMark_{stemId}");
            var tag = root.AddComponent<StemMarker>();
            tag.StemId = stemId;

            // Volume invisibile per il puntamento: la fascia da sola sarebbe impuntabile.
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1;                       // asse Y
            col.height = pickHeight;
            col.center = new Vector3(0f, pickHeight * 0.5f, 0f);

            // La striscia visibile: niente CreatePrimitive, quindi niente tappi e nessun
            // collider da rimuovere subito dopo averlo creato.
            var band = new GameObject("Band", typeof(MeshFilter), typeof(MeshRenderer));
            band.transform.SetParent(root.transform, false);
            band.GetComponent<MeshFilter>().sharedMesh = BandMesh(bandSegments);

            var r = band.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            return root;
        }

        private void Apply(GameObject root, StemRecord rec)
        {
            // Ancorato all'ASSE del fusto, non al punto cliccato.
            root.transform.SetPositionAndRotation(rec.MarkAnchor, Quaternion.identity);

            float d = Mathf.Max(rec.Dbh, 0.02f) * bandOversize;

            var col = root.GetComponent<CapsuleCollider>();
            if (col != null) col.radius = Mathf.Max(d * 0.5f, minPickRadius);

            var band = root.transform.Find("Band");
            if (band != null)
            {
                band.localPosition = new Vector3(0f, breastHeight, 0f);
                // La mesh e' unitaria (diametro 1, altezza 1): la scala E' la misura in metri.
                band.localScale = new Vector3(d, bandThickness, d);

                var r = band.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = rec.Marked ? MarkedMaterial : UnmarkedMaterial;
            }

            var tag = root.GetComponent<StemMarker>();
            if (tag != null) tag.StemId = rec.StemId;
        }

        // ---- mesh della fascia ---------------------------------------------------------------

        /// <summary>
        /// Striscia cilindrica SENZA TAPPI: raggio 0.5, altezza 1, centrata sull'origine. Ogni
        /// quad viene emesso due volte con winding opposto, cosi' la fascia si vede anche da
        /// dentro senza dipendere da una proprieta' '_Cull' che i fallback della cascata di
        /// shader potrebbero non avere.
        ///
        /// Una sola mesh per tutti i marker: e' statica e si rigenera solo se cambia il numero
        /// di segmenti.
        /// </summary>
        private static Mesh BandMesh(int segments)
        {
            segments = Mathf.Clamp(segments, 8, 128);
            if (bandMesh != null && bandMeshSegments == segments) return bandMesh;

            var verts = new Vector3[segments * 2];
            var uvs = new Vector2[segments * 2];
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                float x = Mathf.Cos(a) * 0.5f, z = Mathf.Sin(a) * 0.5f;
                verts[i * 2]     = new Vector3(x, -0.5f, z);
                verts[i * 2 + 1] = new Vector3(x,  0.5f, z);
                float u = i / (float)segments;
                uvs[i * 2]     = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            var tris = new int[segments * 12];      // 2 triangoli per faccia x 2 facce x 3 indici
            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int b0 = i * 2, t0 = b0 + 1;
                int n  = (i + 1) % segments;
                int b1 = n * 2, t1 = b1 + 1;

                // faccia esterna
                tris[t++] = b0; tris[t++] = t0; tris[t++] = b1;
                tris[t++] = t0; tris[t++] = t1; tris[t++] = b1;
                // faccia interna (winding invertito)
                tris[t++] = b1; tris[t++] = t0; tris[t++] = b0;
                tris[t++] = b1; tris[t++] = t1; tris[t++] = t0;
            }

            var m = new Mesh { name = "M_StemBand" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();

            bandMesh = m;
            bandMeshSegments = segments;
            return bandMesh;
        }

        // ---- materiali ---------------------------------------------------------------------------

        private Material UnmarkedMaterial => unmarkedMat != null ? unmarkedMat
            : (unmarkedMat = MakeUnlit(unmarkedColor, "M_StemMark_Measured"));

        private Material MarkedMaterial => markedMat != null ? markedMat
            : (markedMat = MakeUnlit(markedColor, "M_StemMark_Marked"));

        /// <summary>
        /// Materiale unlit a prova di build. Unlit e non Lit di proposito: sotto copertura il
        /// bosco e' scuro e un segno illuminato risulterebbe spento proprio dove serve leggerlo;
        /// unlit tiene il colore pieno e costa meno su Quest.
        ///
        /// La cascata di shader esiste perche' Shader.Find vede solo cio' che e' finito nella
        /// build: un URP/Unlit non referenziato da nessun materiale viene rimosso e tornerebbe
        /// null — da cui il magenta. E' la stessa lezione di UnlitMaterials sul desktop.
        /// </summary>
        public static Material MakeUnlit(Color c, string name = "M_Unlit")
        {
            string[] candidates =
            {
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Simple Lit",
                "Unlit/Color",
                "Sprites/Default"
            };

            foreach (var s in candidates)
            {
                var sh = Shader.Find(s);
                if (sh == null) continue;
                var m = new Material(sh) { name = name };
                SetColor(m, c);
                if (s != candidates[0])
                    Debug.LogWarning($"[StemMarkerSpawner] '{candidates[0]}' non e' in questa build: " +
                                     $"uso '{s}'. Metti un materiale che lo usi sotto Assets/Resources " +
                                     "per includerlo correttamente.");
                return m;
            }

            Debug.LogError("[StemMarkerSpawner] nessuno shader unlit disponibile: i segni saranno magenta.");
            return null;
        }

        /// Tinta compatibile URP: scrive _BaseColor e/o _Color a seconda dello shader.
        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
            m.color = c;
        }

        public static void SetColor(Renderer r, Color c)
        {
            if (r != null) SetColor(r.material, c);
        }

        private void ClearAll()
        {
            foreach (var kv in markers) if (kv.Value != null) Destroy(kv.Value);
            markers.Clear();
        }
    }
}
