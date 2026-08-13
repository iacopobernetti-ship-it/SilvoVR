using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace Artemis.Vr
{
    /// <summary>
    /// FASE 1 — cambio AREA = cambio SCENA, versione SINGLE PLAYER.
    ///
    /// Nessun automatismo: si parte nella scena Base (un piano, la HUD) e ci si resta finche'
    /// l'utente non sceglie un'area dalla HUD. Con gli splat su rete i tempi di caricamento
    /// non sono garantiti, quindi ogni transizione e' ESPLICITA e ha un progresso osservabile
    /// (OnLoadProgress) che la HUD mostra.
    ///
    /// Il caricamento e' ASINCRONO (LoadSceneAsync): un LoadScene sincrono congela il frame,
    /// e in visore un congelamento e' un pugno nello stomaco — il compositor Quest mostra
    /// comunque il suo schermo di grazia, ma meglio dargli meno lavoro possibile.
    ///
    /// FASE 4 (multiplayer): questo e' il punto dove rientrera' il ramo Netcode — GoTo fara'
    /// NetworkManager.SceneManager.LoadScene se c'e' una sessione e il locale e' il docente,
    /// esattamente come SessionFlow desktop. L'API pubblica non cambiera'.
    ///
    /// Vive sull'oggetto App persistente (PersistentRoot) della scena Base.
    /// </summary>
    public class AreaFlow : MonoBehaviour
    {
        [Serializable]
        public class AreaDef
        {
            [Tooltip("Nome ESATTO della scena in Build Settings (= id canonico area: Silvo01…).")]
            public string sceneName = "";
            [Tooltip("Etichetta sul pulsante. Vuota = sceneName.")]
            public string label = "";
            [Tooltip("Area nominale in m². Non usata in Fase 1; e' qui perche' e' un dato " +
                     "dell'area e questo e' il suo posto (in Fase 2 guidera' gli indici /ha).")]
            public float areaM2 = 400f;

            public string Label => string.IsNullOrWhiteSpace(label) ? sceneName : label;
        }

        [Tooltip("La scena hub: piano semplice, nessuno splat. Ci si torna dalla HUD.")]
        [SerializeField] private string baseSceneName = "Base";
        [Tooltip("La scena della simulazione: soprassuolo ricostruito con alberi 3D su suolo " +
                 "piatto. Una sola per tutte le aree — si costruisce dall'inventario dell'area " +
                 "da cui si e' entrati.")]
        [SerializeField] private string simulationSceneName = "Simulation";
        [Tooltip("Etichetta del pulsante per tornare alla Base.")]
        [SerializeField] private string baseLabel = "Base";

        [Tooltip("Le aree, nell'ordine dei pulsanti della HUD.")]
        [SerializeField] private List<AreaDef> areas = new List<AreaDef>();

        public static AreaFlow Instance { get; private set; }

        /// <summary>
        /// L'area da cui si e' entrati in Simulation. E' STATICA di proposito: l'istanza di
        /// AreaFlow muore col cambio scena (architettura rev.2, niente persistenza), mentre
        /// questo dato deve sopravvivere proprio a quel passaggio — serve a Simulation per
        /// sapere quale inventario ricostruire, e serve al ritorno per riportare il giocatore
        /// nell'area giusta. E' l'unico stato che attraversa le scene, ed e' una stringa.
        /// </summary>
        public static string OriginArea { get; private set; } = "";

        public string SimulationSceneName => simulationSceneName;

        /// <summary>Chi puo' cambiare scena adesso: il docente, o chiunque fuori sessione.</summary>
        public static bool CanSwitch => Artemis.Session.VrSession.CanCommand;
        public bool IsOnSimulation => CurrentScene == simulationSceneName;

        /// <summary>Siamo in un'area di saggio (non Base, non Simulation). Serve ai pannelli per
        /// registrarsi SOLO dove hanno senso: una HUD piena di linguette inutili invita a premere
        /// pulsanti a caso.</summary>
        public bool IsOnArea => !IsOnBase && !IsOnSimulation;

        /// <summary>Nome scena di destinazione, appena parte un caricamento.</summary>
        public event Action<string> OnLoadStarted;
        /// <summary>Progresso 0..1 del caricamento in corso.</summary>
        public event Action<float> OnLoadProgress;
        /// <summary>Nome scena, a caricamento completato e scena attiva.</summary>
        public event Action<string> OnLoadFinished;

        public bool IsBusy { get; private set; }
        public string CurrentScene => SceneManager.GetActiveScene().name;
        public bool IsOnBase => CurrentScene == baseSceneName;
        public string BaseSceneName => baseSceneName;
        public string BaseLabel => baseLabel;
        public IReadOnlyList<AreaDef> Areas => areas;

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

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void GoToBase() => GoTo(baseSceneName);

        /// <summary>
        /// Entra in Simulation ricordando da dove. Chiamabile solo da un'area: dalla Base non
        /// avrebbe un inventario da cui costruire il soprassuolo.
        /// </summary>
        /// <summary>
        /// Ricorda l'area di provenienza a OGNI cambio scena, non solo quando si preme il
        /// pulsante: gli studenti non lo premono mai — li porta il docente — e senza questo la
        /// loro simulazione non saprebbe da quale area proviene.
        /// </summary>
        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (s.name != simulationSceneName && s.name != baseSceneName) OriginArea = s.name;
        }

        private void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
        private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        public void GoToSimulation()
        {
            if (IsOnSimulation) return;
            if (CurrentScene == baseSceneName)
            {
                Debug.LogWarning("[AreaFlow] la simulazione si apre da un'area di saggio, non dalla Base.");
                return;
            }
            OriginArea = CurrentScene;
            GoTo(simulationSceneName);
        }

        /// <summary>Torna all'area da cui si era entrati in Simulation.</summary>
        public void ReturnFromSimulation()
        {
            string back = !string.IsNullOrWhiteSpace(OriginArea) ? OriginArea : baseSceneName;
            GoTo(back);
        }

        public void GoToArea(string sceneName) => GoTo(sceneName);

        private void GoTo(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return;
            if (IsBusy) { Debug.LogWarning($"[AreaFlow] caricamento gia' in corso — '{sceneName}' ignorato."); return; }
            if (CurrentScene == sceneName) return;

            bool known = sceneName == baseSceneName || sceneName == simulationSceneName ||
                         areas.Exists(a => string.Equals(a.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                Debug.LogError($"[AreaFlow] scena '{sceneName}' non dichiarata (ne' Base ne' area).");
                return;
            }

            // ---- in sessione comanda il docente -------------------------------------------
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                if (!nm.IsServer)
                {
                    // Gli studenti non chiamano mai questo metodo (la scheda Areas non esiste
                    // per loro), ma se ci arrivassero da un'altra strada devono essere fermati
                    // qui: due client che caricano scene diverse sono due lezioni separate.
                    Debug.Log("[AreaFlow] solo il docente cambia scena.");
                    return;
                }

                // Caricamento di rete: la scena si carica su TUTTI, docente compreso. Un solo
                // percorso di codice, quindi il docente non puo' vedere qualcosa di diverso
                // dagli studenti — ed e' NGO a portare anche chi si collega a lezione iniziata.
                OnLoadStarted?.Invoke(sceneName);
                var status = nm.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started)
                    Debug.LogError($"[AreaFlow] LoadScene di rete '{sceneName}' fallito: {status}");
                return;
            }

            StartCoroutine(LoadRoutine(sceneName));
        }

        // ---- scorciatoie da EDITOR ---------------------------------------------------------
        // Cambiare area senza toccare la UI: clic destro sull'intestazione del componente
        // AreaFlow nell'Inspector, in Play mode. Serve a collaudare il cambio scena in modo
        // INDIPENDENTE dall'input XR — se i pulsanti non rispondono, queste funzionano
        // comunque e permettono di isolare "il flusso di scena e' rotto" da "l'input e' rotto".

        [ContextMenu("Vai a: Base")]
        private void CtxBase() => GoToBase();

        [ContextMenu("Vai a: area 1")]
        private void CtxArea1() => GoToAreaIndex(0);

        [ContextMenu("Vai a: area 2")]
        private void CtxArea2() => GoToAreaIndex(1);

        [ContextMenu("Vai a: area 3")]
        private void CtxArea3() => GoToAreaIndex(2);

        [ContextMenu("Vai a: area 4")]
        private void CtxArea4() => GoToAreaIndex(3);

        /// <summary>Carica l'n-esima area dichiarata. Utile anche da altri script/test.</summary>
        public void GoToAreaIndex(int index)
        {
            if (index < 0 || index >= areas.Count)
            { Debug.LogWarning($"[AreaFlow] nessuna area di indice {index} (ne sono dichiarate {areas.Count})."); return; }
            GoToArea(areas[index].sceneName);
        }

        [ContextMenu("Vai a: Simulation")]
        private void CtxSim() => GoToSimulation();

        [ContextMenu("Stato: dove siamo")]
        private void CtxState() =>
            Debug.Log($"[AreaFlow] scena attiva '{CurrentScene}' · occupato={IsBusy} · " +
                      $"aree dichiarate={areas.Count} · base='{baseSceneName}'");

        private IEnumerator LoadRoutine(string sceneName)
        {
            IsBusy = true;
            OnLoadStarted?.Invoke(sceneName);
            OnLoadProgress?.Invoke(0f);
            Debug.Log($"[AreaFlow] --- cambio scena: {CurrentScene} -> {sceneName} ---");

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError($"[AreaFlow] LoadSceneAsync('{sceneName}') nullo: scena in Build Settings?");
                IsBusy = false;
                yield break;
            }

            while (!op.isDone)
            {
                // AsyncOperation.progress arriva a 0.9 e poi salta a done: normalizza a 0..1.
                OnLoadProgress?.Invoke(Mathf.Clamp01(op.progress / 0.9f));
                yield return null;
            }

            OnLoadProgress?.Invoke(1f);
            IsBusy = false;
            Debug.Log($"[AreaFlow] scena attiva: {CurrentScene}");
            OnLoadFinished?.Invoke(sceneName);

            // NOTA Fase 1: questo segna la fine del caricamento SCENA, non dello SPLAT — il
            // Gaussian Splatting continua a scaricarsi in async dentro la scena nuova (Start
            // dell'LCCRendererVR originale). Il player intanto sta gia' su un collider solido.
        }
    }
}
