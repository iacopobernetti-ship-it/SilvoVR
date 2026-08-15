using System;
using System.Collections.Generic;
using UnityEngine;
using Artemis.Vr;

namespace Artemis.Inventory
{
    /// <summary>
    /// L'inventario dell'area corrente. UN SOLO FILE PER AREA (inv_{area}.json), nessun nome da
    /// digitare: in visore la tastiera virtuale e' un supplizio, e un rilievo appartiene
    /// all'area su cui e' stato fatto — quella e' tutta la sua identita'.
    ///
    /// Vive nel prefab VrApp, quindi nasce e muore con la scena: entrando in un'area si carica
    /// il suo file, uscendo non resta nulla in memoria. Non esiste il caso "inventario di
    /// un'area aperto mentre sono in un'altra", che nel desktop richiedeva parecchia guardia.
    ///
    /// Il rilievo e' LOCALE: i dati restano sulla macchina. In Fase 4 la sessione condividera'
    /// il contenuto dell'inventario del docente, ma la misurazione restera' locale.
    /// </summary>
    public class StemInventory : MonoBehaviour
    {
        [Tooltip("Salva su file a ogni modifica: un rilievo di campo non deve poter andare " +
                 "perso perche' e' finita la batteria del visore.")]
        [SerializeField] private bool autoSave = true;

        [Tooltip("Prima di azzerare, conserva una copia del file con un timestamp. Non richiede " +
                 "di digitare nulla ed e' l'unica rete contro un 'Nuovo rilievo' involontario.")]
        [SerializeField] private bool keepBackupOnReset = true;

        private readonly List<StemRecord> stems = new List<StemRecord>();
        private int nextStemId = 1;
        private bool suppressSave;

        public static StemInventory Instance { get; private set; }

        /// <summary>Area corrente = scena corrente. Unica fonte di verita', nessun id da gestire.</summary>
        public string PlotId => AreaFlow.Instance != null
            ? AreaFlow.Instance.CurrentScene
            : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        public event Action OnInventoryChanged;

        // ---- ciclo di vita --------------------------------------------------------------------

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

        /// <summary>
        /// L'inventario appartiene a un'AREA DI SAGGIO. Fuori da li' non si carica nulla e non si
        /// scrive nulla: il PlotId e' il nome della scena, quindi in Simulation l'inventario si
        /// chiamerebbe "Simulation" e il suo file conterrebbe misure prese su un soprassuolo
        /// ricostruito — dati senza significato, che pero' vengono disegnati come veri.
        /// </summary>
        private bool ActiveHere
        {
            get
            {
                var flow = AreaFlow.Instance;
                return flow == null || flow.IsOnArea;
            }
        }

        private void Start() { if (ActiveHere) LoadFromDisk(); }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void LoadFromDisk()
        {
            suppressSave = true;
            stems.Clear();
            nextStemId = 1;
            foreach (var s in InventoryStore.Load(PlotId))
            {
                stems.Add(s);
                if (s.StemId >= nextStemId) nextStemId = s.StemId + 1;
            }
            suppressSave = false;

            if (stems.Count > 0)
                Debug.Log($"[StemInventory] area '{PlotId}': ripresi {stems.Count} alberi dal file.");
            OnInventoryChanged?.Invoke();
        }

        // ---- lettura ---------------------------------------------------------------------------

        public IReadOnlyList<StemRecord> Stems => stems;
        public int Count => stems.Count;

        /// <summary>Alberi per ettaro, data l'area nominale di saggio in m².</summary>
        public float NPerHa(float plotAreaM2) =>
            plotAreaM2 > 1f ? stems.Count * 10000f / plotAreaM2 : 0f;

        /// <summary>Area basimetrica per ettaro (m²/ha).</summary>
        public float GPerHa(float plotAreaM2)
        {
            if (plotAreaM2 <= 1f) return 0f;
            float g = 0f;
            foreach (var s in stems) g += s.BasalArea;
            return g * 10000f / plotAreaM2;
        }

        /// <summary>Diametro medio quadratico (m): la media che conserva l'area basimetrica,
        /// dendrometricamente corretta — non la media aritmetica dei diametri.</summary>
        public float QuadraticMeanDbh()
        {
            if (stems.Count == 0) return 0f;
            float sum = 0f;
            foreach (var s in stems) sum += s.Dbh * s.Dbh;
            return Mathf.Sqrt(sum / stems.Count);
        }

        // ---- modifiche --------------------------------------------------------------------------

        public int AddStem(Vector3 basePos, Vector3 axis, float dbh, float height)
        {
            var rec = new StemRecord
            {
                // L'area non si scrive nel record: sta gia' nell'intestazione del file, ed e'
                // per costruzione quella corrente (un file per area).
                StemId = nextStemId++,
                Base = basePos, Axis = axis, Dbh = dbh, Height = height, Marked = false
            };
            stems.Add(rec);
            Changed();
            return rec.StemId;
        }

        public void ToggleMark(int stemId)
        {
            for (int i = 0; i < stems.Count; i++)
                if (stems[i].StemId == stemId)
                { var r = stems[i]; r.Marked = !r.Marked; stems[i] = r; Changed(); return; }
        }

        public void RemoveStem(int stemId)
        {
            for (int i = 0; i < stems.Count; i++)
                if (stems[i].StemId == stemId) { stems.RemoveAt(i); Changed(); return; }
        }

        /// <summary>
        /// Nuovo rilievo: azzera l'inventario di QUESTA area e riscrive il file vuoto.
        /// IRREVERSIBILE (salvo il backup): chi chiama deve aver gia' chiesto conferma —
        /// SurveyPanel lo fa a due tocchi, perche' in visore un pulsante si sfiora per sbaglio.
        /// </summary>
        public void ResetInventory()
        {
            if (keepBackupOnReset && stems.Count > 0) InventoryStore.Backup(PlotId);

            int had = stems.Count;
            suppressSave = true;
            stems.Clear();
            nextStemId = 1;
            suppressSave = false;

            InventoryStore.Save(PlotId, stems);
            Debug.Log($"[StemInventory] area '{PlotId}': nuovo rilievo, {had} alberi azzerati.");
            OnInventoryChanged?.Invoke();
        }

        // ---- interni ----------------------------------------------------------------------------

        private void Changed()
        {
            OnInventoryChanged?.Invoke();
            // Non si scrive su disco fuori da un'area: e' la seconda meta' della stessa regola,
            // e impedisce che un file fantasma nasca di nuovo.
            if (autoSave && !suppressSave && ActiveHere) InventoryStore.Save(PlotId, stems);
        }
    }
}
