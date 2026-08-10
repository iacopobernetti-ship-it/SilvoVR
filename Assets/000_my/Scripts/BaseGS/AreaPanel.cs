using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Artemis.Vr
{
    /// <summary>
    /// La prima scheda della HUD: "Aree". Un pulsante per la Base + uno per area, generati
    /// da AreaFlow (una fonte sola, come PlotSwitchHUD generava da PlotLibrary). Verde =
    /// scena attiva; durante un caricamento i pulsanti si disattivano e la riga di stato
    /// mostra il progresso — il feedback che serve quando i tempi di rete non sono certi.
    ///
    /// Sta sullo stesso oggetto App persistente di VrHud e AreaFlow. E' anche il MODELLO
    /// per i pannelli di Fase 2: prendi la pagina con CreateTab, costruisci con MakeButton/
    /// MakeLabel, aggiorna a eventi + un refresh leggero periodico.
    /// </summary>
    public class AreaPanel : MonoBehaviour
    {
        private readonly Dictionary<string, Image> buttonImages = new Dictionary<string, Image>();
        private readonly List<Button> buttons = new List<Button>();
        private TMP_Text status;
        private bool built;
        private float nextRefresh;

        private void Start() { TryBuild(); }

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            if (Time.time >= nextRefresh)
            {
                nextRefresh = Time.time + 0.5f;
                RefreshColors();
            }
        }

        private void OnDestroy()
        {
            var flow = AreaFlow.Instance;
            if (flow == null) return;
            flow.OnLoadStarted -= OnLoadStarted;
            flow.OnLoadProgress -= OnLoadProgress;
            flow.OnLoadFinished -= OnLoadFinished;
        }

        // ---------------------------------------------------------------- costruzione

        /// Hud e Flow sono singleton su questo stesso oggetto, ma l'ordine di Awake fra
        /// componenti non e' garantito: si costruisce appena ci sono entrambi, senza pretese
        /// sul primo frame.
        private void TryBuild()
        {
            var hud = VrHud.Instance;
            var flow = AreaFlow.Instance;
            if (hud == null || flow == null) return;

            // In Simulation NO: da li' si esce con "Back to plot", che salva la martellata.
            // Un salto diretto ad un'altra area perderebbe il lavoro senza avvisare.
            if (flow.IsOnSimulation) { enabled = false; return; }

            var page = hud.CreateTab("Areas");

            hud.MakeLabel(page, "Choose the sample plot", 20);

            AddSceneButton(hud, flow, flow.BaseSceneName, flow.BaseLabel, isBase: true);
            foreach (var a in flow.Areas)
                AddSceneButton(hud, flow, a.sceneName, a.Label, isBase: false);

            status = hud.MakeLabel(page, "", 18);

            flow.OnLoadStarted += OnLoadStarted;
            flow.OnLoadProgress += OnLoadProgress;
            flow.OnLoadFinished += OnLoadFinished;

            built = true;
            RefreshColors();
        }

        private void AddSceneButton(VrHud hud, AreaFlow flow, string sceneName, string label, bool isBase)
        {
            var page = hud.CreateTab("Areas");             // idempotente: ritorna la stessa pagina
            string scene = sceneName;                     // copia locale per la closure
            var (btn, img) = hud.MakeButton(page, label,
                () => { if (isBase) flow.GoToBase(); else flow.GoToArea(scene); });
            buttonImages[scene] = img;
            buttons.Add(btn);
        }

        // ---------------------------------------------------------------- eventi flow

        private void OnLoadStarted(string scene)
        {
            foreach (var b in buttons) b.interactable = false;
            if (status != null) status.text = $"Loading {scene}…";
        }

        private void OnLoadProgress(float p)
        {
            if (status != null && AreaFlow.Instance != null && AreaFlow.Instance.IsBusy)
                status.text = $"Loading scene… {p:P0}";
        }

        private void OnLoadFinished(string scene)
        {
            foreach (var b in buttons) b.interactable = true;
            if (status != null)
                status.text = AreaFlow.Instance != null && AreaFlow.Instance.IsOnBase
                    ? ""
                    : "Scene ready — the splat may take a few more seconds.";
            RefreshColors();
        }

        // ---------------------------------------------------------------- stato

        private void RefreshColors()
        {
            var hud = VrHud.Instance;
            var flow = AreaFlow.Instance;
            if (hud == null || flow == null) return;

            // In Simulation NO: da li' si esce con "Back to plot", che salva la martellata.
            // Un salto diretto ad un'altra area perderebbe il lavoro senza avvisare.
            if (flow.IsOnSimulation) { enabled = false; return; }

            string current = flow.CurrentScene;
            foreach (var kv in buttonImages)
                kv.Value.color = kv.Key == current ? hud.ActiveColor : hud.ButtonColor;
        }
    }
}
