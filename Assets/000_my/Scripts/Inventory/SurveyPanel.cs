using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;

namespace Artemis.Inventory
{
    /// <summary>
    /// La scheda "Rilievo" della HUD VR — erede immersivo di InventoryHUD, costruita con le
    /// factory di VrHud (stesso schema di AreaPanel).
    ///
    /// REGOLA appresa a caro prezzo: la scheda si costruisce appena c'e' la HUD, e NON aspetta
    /// che ci sia anche il VrSurveyTool. Se lo strumento manca, la scheda compare lo stesso e
    /// LO DICE. Un pannello diagnostico che sparisce proprio quando qualcosa non funziona non
    /// serve a nulla — e in visore, senza log, e' l'unica finestra sullo stato del sistema.
    ///
    /// Mostra: i tre modi, la misura in sospeso, conferma/annulla (ridondanti rispetto ai tasti
    /// A/B: in aula la ridondanza e' un pregio), i dati aggregati di popolamento, il pulsante
    /// "Nuovo rilievo" a due tocchi e una riga diagnostica sull'input.
    /// </summary>
    public class SurveyPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Survey";

        [Tooltip("Secondi entro cui va confermato 'Nuovo rilievo'. Scaduti, la richiesta decade " +
                 "da sola: in visore un pulsante si sfiora per sbaglio, e questo azzera dati di campo.")]
        [SerializeField] private float resetConfirmSeconds = 5f;

        private bool built;
        private float nextRefresh;
        private float resetArmedUntil;
        private VrSurveyTool bound;

        private Image measureImg, markImg, removeImg, resetImg;
        private Button confirmBtn, cancelBtn;
        private RectTransform confirmRow;
        private TMP_Text hintLabel, pendingLabel, statsLabel, statusLabel, diagLabel, resetLabel;

        // ---- ciclo di vita --------------------------------------------------------------------

        private void Update()
        {
            if (!built) { TryBuild(); return; }

            // Lo strumento puo' comparire dopo (ordine di Awake non garantito) o mancare del
            // tutto: ci si aggancia quando c'e', senza mai bloccare la costruzione della scheda.
            var tool = VrSurveyTool.Instance;
            if (tool != bound)
            {
                if (bound != null) bound.OnStateChanged -= Refresh;
                bound = tool;
                if (bound != null) bound.OnStateChanged += Refresh;
            }

            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.2f;
            Refresh();
        }

        private void OnDestroy()
        {
            if (bound != null) bound.OnStateChanged -= Refresh;
        }

        // ---- costruzione: SERVE SOLO LA HUD ------------------------------------------------------

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;                 // unica dipendenza reale

            var page = hud.CreateTab(tabTitle);

            hintLabel = hud.MakeLabel(page, "", 18);

            // I callback risolvono lo strumento AL CLIC, non alla costruzione: cosi' i pulsanti
            // esistono anche se lo strumento arriva dopo, e non catturano un riferimento morto.
            // Tre pulsanti in RIGA invece che in colonna: piu' quadrati, quindi molto piu'
            // facili da centrare col ray rispetto a barre lunghe e strette.
            var modeRow = hud.MakeRow(page);
            var (mBtn, mImg) = hud.MakeButton(modeRow, "Measure",  () => VrSurveyTool.Instance?.SetMode(VrSurveyTool.ToolMode.Measure));
            var (kBtn, kImg) = hud.MakeButton(modeRow, "Mark",   () => VrSurveyTool.Instance?.SetMode(VrSurveyTool.ToolMode.Mark));
            var (xBtn, xImg) = hud.MakeButton(modeRow, "Remove", () => VrSurveyTool.Instance?.SetMode(VrSurveyTool.ToolMode.Remove));
            measureImg = mImg; markImg = kImg; removeImg = xImg;

            pendingLabel = hud.MakeLabel(page, "", 20);

            // Conferma/annulla affiancati. Le etichette ricordano i grip, che hanno sostituito
            // A/B: su Quest il tasto A e' intercettato dal menu di sistema.
            confirmRow = hud.MakeRow(page);
            var (cBtn, _) = hud.MakeButton(confirmRow, "Confirm\n(right grip)", () => VrSurveyTool.Instance?.ConfirmPending());
            var (aBtn, _) = hud.MakeButton(confirmRow, "Discard\n(left grip)",  () => VrSurveyTool.Instance?.CancelPending());
            confirmBtn = cBtn; cancelBtn = aBtn;

            statsLabel = hud.MakeLabel(page, "", 18);

            var (rBtn, rImg) = hud.MakeButton(page, "New survey", OnResetClicked);
            resetImg = rImg;
            resetLabel = rBtn.GetComponentInChildren<TMP_Text>();

            statusLabel = hud.MakeLabel(page, "", 16);
            diagLabel   = hud.MakeLabel(page, "", 12);

            built = true;
            Refresh();
        }

        // ---- nuovo rilievo (due tocchi) -----------------------------------------------------------

        /// Primo tocco: arma e dichiara quanti alberi si perderebbero. Secondo tocco entro
        /// resetConfirmSeconds: esegue. Nessuna finestra modale, che in VR sarebbe piu' invasiva
        /// del problema che risolve.
        private void OnResetClicked()
        {
            var inv = StemInventory.Instance;
            if (inv == null) return;

            if (Time.time <= resetArmedUntil)
            {
                resetArmedUntil = 0f;
                inv.ResetInventory();
                if (statusLabel != null) statusLabel.text = "Inventory cleared — new survey";
                return;
            }

            if (inv.Count == 0) { inv.ResetInventory(); return; }   // niente da perdere, niente da chiedere
            resetArmedUntil = Time.time + resetConfirmSeconds;
        }

        // ---- aggiornamento ---------------------------------------------------------------------

        private void Refresh()
        {
            if (!built) return;
            var hud  = VrHud.Instance;
            var tool = VrSurveyTool.Instance;
            var inv  = StemInventory.Instance;
            if (hud == null) return;

            // --- assenze dichiarate, non silenziose -------------------------------------------
            if (tool == null || inv == null)
            {
                if (hintLabel != null) hintLabel.text = "Survey unavailable";
                if (statusLabel != null)
                    statusLabel.text =
                        (tool == null ? "VrSurveyTool missing" : "") +
                        (tool == null && inv == null ? " and " : "") +
                        (inv == null ? "StemInventory missing" : "") +
                        " on the App object of the VrApp prefab";
                if (diagLabel != null) diagLabel.text = "";
                SetModeColors(hud, null);
                if (confirmRow != null) confirmRow.gameObject.SetActive(false);
                if (pendingLabel != null) pendingLabel.text = "";
                if (statsLabel != null) statsLabel.text = "";
                RefreshResetButton(hud, inv);
                return;
            }

            // --- funzionamento normale ---------------------------------------------------------
            if (hintLabel != null) hintLabel.text = tool.StepHint;
            SetModeColors(hud, tool);

            if (confirmRow != null) confirmRow.gameObject.SetActive(tool.HasPending);

            if (pendingLabel != null)
                pendingLabel.text = tool.HasPending
                    ? $"d = {tool.PendingDbh * 100f:F1} cm   ·   h = {tool.PendingHeight:F1} m"
                    : "";

            if (statsLabel != null)
            {
                float areaM2 = CurrentAreaM2();
                statsLabel.text = inv.Count == 0
                    ? "no trees surveyed yet"
                    : $"{inv.Count} trees  ·  {inv.NPerHa(areaM2):F0} N/ha  ·  " +
                      $"{inv.GPerHa(areaM2):F1} m²/ha  ·  dg {inv.QuadraticMeanDbh() * 100f:F1} cm";
            }

            RefreshResetButton(hud, inv);

            if (statusLabel != null) statusLabel.text = tool.Status;
            if (diagLabel   != null) diagLabel.text   = tool.Diagnostics;
        }

        private void SetModeColors(VrHud hud, VrSurveyTool tool)
        {
            Color idle = hud.ButtonColor, on = hud.ActiveColor;
            if (measureImg != null) measureImg.color = tool != null && tool.Mode == VrSurveyTool.ToolMode.Measure ? on : idle;
            if (markImg    != null) markImg.color    = tool != null && tool.Mode == VrSurveyTool.ToolMode.Mark    ? on : idle;
            if (removeImg  != null) removeImg.color  = tool != null && tool.Mode == VrSurveyTool.ToolMode.Remove  ? on : idle;
        }

        private void RefreshResetButton(VrHud hud, StemInventory inv)
        {
            if (resetLabel == null || hud == null) return;

            bool armed = Time.time <= resetArmedUntil;
            if (armed)
            {
                int n = inv != null ? inv.Count : 0;
                resetLabel.text = $"CONFIRM: delete {n} trees";
                if (resetImg != null) resetImg.color = new Color(0.75f, 0.25f, 0.20f, 1f);
            }
            else
            {
                resetLabel.text = "New survey";
                if (resetImg != null) resetImg.color = hud.ButtonColor;
            }
        }

        /// Area nominale dell'area di saggio corrente, dichiarata in AreaFlow (dato di progetto,
        /// non ricavato dalla geometria). 400 m² come ripiego se non e' dichiarata.
        private float CurrentAreaM2()
        {
            var flow = AreaFlow.Instance;
            if (flow == null) return 400f;
            foreach (var a in flow.Areas)
                if (string.Equals(a.sceneName, flow.CurrentScene, System.StringComparison.OrdinalIgnoreCase))
                    return a.areaM2;
            return 400f;
        }
    }
}
