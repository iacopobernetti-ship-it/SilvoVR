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
    ///   Band  — il cilindro sottile visibile, senza collider.
    ///
    /// I materiali si generano qui con una cascata di shader (URP/Unlit -> URP/Lit -> Unlit ->
    /// Sprites): CreatePrimitive assegna il Default-Material della pipeline Built-in, che in URP
    /// non esiste e diventa magenta acceso. Nessun materiale da preparare in editor.
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

        // ---- ciclo di vita ---------------------------------------------------------------------

        private void Update()
        {
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

            // La fascia visibile.
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "Band";
            var bandCol = band.GetComponent<Collider>();
            if (bandCol != null) Destroy(bandCol);    // il collider e' uno solo, sulla radice
            band.transform.SetParent(root.transform, false);

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
                // Il cilindro primitivo e' alto 2 unita' a scala 1: meta' spessore in Y.
                band.localScale = new Vector3(d, bandThickness * 0.5f, d);

                var r = band.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = rec.Marked ? MarkedMaterial : UnmarkedMaterial;
            }

            var tag = root.GetComponent<StemMarker>();
            if (tag != null) tag.StemId = rec.StemId;
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
