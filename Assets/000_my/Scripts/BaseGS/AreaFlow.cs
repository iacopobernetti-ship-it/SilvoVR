using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
        [Tooltip("Etichetta del pulsante per tornare alla Base.")]
        [SerializeField] private string baseLabel = "Base";

        [Tooltip("Le aree, nell'ordine dei pulsanti della HUD.")]
        [SerializeField] private List<AreaDef> areas = new List<AreaDef>();

        public static AreaFlow Instance { get; private set; }

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
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void GoToBase() => GoTo(baseSceneName);

        public void GoToArea(string sceneName) => GoTo(sceneName);

        private void GoTo(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return;
            if (IsBusy) { Debug.LogWarning($"[AreaFlow] caricamento gia' in corso — '{sceneName}' ignorato."); return; }
            if (CurrentScene == sceneName) return;

            bool known = sceneName == baseSceneName ||
                         areas.Exists(a => string.Equals(a.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                Debug.LogError($"[AreaFlow] scena '{sceneName}' non dichiarata (ne' Base ne' area).");
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
