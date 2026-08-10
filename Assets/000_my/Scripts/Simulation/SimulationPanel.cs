using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;
using Artemis.Inventory;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Scheda "Felling", presente SOLO nella scena Simulation: si registra alla HUD soltanto se
    /// trova uno StandBuilder, quindi nelle aree non compare affatto — nessun pulsante che non
    /// avrebbe nulla da fare.
    ///
    /// Mostra lo stato del soprassuolo, il conteggio dei segni, il pulsante di abbattimento e
    /// l'esito ecologico dell'ultima martellata (luce nella buca, aridita', G residua, indice di
    /// idoneita' e fattore limitante). Il ritorno all'area salva automaticamente la martellata.
    /// </summary>
    public class SimulationPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Felling";
        [Tooltip("Secondi entro cui confermare 'Clear marks'.")]
        [SerializeField] private float clearConfirmSeconds = 4f;

        private bool built;
        private float nextRefresh, clearArmedUntil;
        private StandBuilder builder;
        private SimMarkTool tool;

        private Button fellBtn;
        private Image fellImg;
        private TMP_Text markedLabel, standLabel, fisLabel, statusLabel;
        private TMP_Text clearLabel;
        private Image clearImg;

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.25f;
            Refresh();
        }

        private void OnDestroy()
        {
            if (tool != null) tool.OnMarkingChanged -= Refresh;
        }

        // ---- costruzione ---------------------------------------------------------------------

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;

            // La scheda esiste solo dove c'e' un soprassuolo da martellare.
            builder = FindFirstObjectByType<StandBuilder>();
            if (builder == null) { enabled = false; return; }

            var page = hud.CreateTab(tabTitle);

            standLabel  = hud.MakeLabel(page, "", 18);
            markedLabel = hud.MakeLabel(page, "", 20);

            var row = hud.MakeRow(page);
            var (f, fi) = hud.MakeButton(row, "Fell marked", OnFellClicked);
            var (c, ci) = hud.MakeButton(row, "Clear marks", OnClearClicked);
            fellBtn = f; fellImg = fi;
            clearImg = ci; clearLabel = c.GetComponentInChildren<TMP_Text>();

            var backRow = hud.MakeRow(page);
            hud.MakeButton(backRow, "Back to plot\n(saves marking)", OnBackClicked);

            fisLabel    = hud.MakeLabel(page, "", 16);
            statusLabel = hud.MakeLabel(page, "", 15);

            built = true;
            Refresh();
        }

        // ---- azioni -----------------------------------------------------------------------------

        private void OnFellClicked()
        {
            var t = Tool();
            if (t == null || t.MarkedCount == 0) return;
            t.FellMarked();
        }

        /// Togliere i segni non distrugge dati, ma rifare la martellata di venti alberi si': due
        /// tocchi anche qui, con lo stesso schema del "New survey".
        private void OnClearClicked()
        {
            var t = Tool();
            if (t == null || t.MarkedCount == 0) return;

            if (Time.time <= clearArmedUntil) { clearArmedUntil = 0f; t.ClearMarks(); return; }
            clearArmedUntil = Time.time + clearConfirmSeconds;
        }

        /// Uscendo si salva la martellata — ma MAI una vuota sopra una piena.
        ///
        /// La versione precedente salvava sempre, e bastava rientrare in Simulation e tornare
        /// indietro senza abbattere per azzerare il lavoro fatto prima: il file veniva riscritto
        /// con zero alberi e da quel momento "Show last marking" non aveva piu' nulla da mostrare.
        /// Una martellata vuota non e' un risultato da conservare, e' un giro a vuoto.
        private void OnBackClicked()
        {
            if (builder != null)
            {
                if (builder.FelledCount > 0) builder.SaveMartellata();
                else Debug.Log("[SimulationPanel] nessun abbattimento: la martellata precedente " +
                               "dell'area resta com'era.");
            }
            AreaFlow.Instance?.ReturnFromSimulation();
        }

        private SimMarkTool Tool()
        {
            if (tool == null)
            {
                tool = SimMarkTool.Instance;
                if (tool != null) tool.OnMarkingChanged += Refresh;
            }
            return tool;
        }

        // ---- aggiornamento -------------------------------------------------------------------------

        private void Refresh()
        {
            var hud = VrHud.Instance;
            if (hud == null || !built || builder == null) return;
            var t = Tool();

            var residual = builder.ResidualStems;
            var m = StandMetrics.Compute(residual, builder.PlotAreaM2);
            standLabel.text = $"{builder.CurrentAreaId}  ·  {m.N} standing  ·  {builder.FelledCount} felled  ·  " +
                              $"{builder.YoungCount} seedlings";

            int marks = t != null ? t.MarkedCount : 0;
            markedLabel.text = marks == 0
                ? "aim at a tree and pull the trigger to mark it"
                : $"{marks} marked for felling";

            if (fellBtn != null) fellBtn.interactable = marks > 0;
            if (fellImg != null) fellImg.color = marks > 0 ? hud.ActiveColor : hud.ButtonColor;

            bool armed = Time.time <= clearArmedUntil;
            if (clearLabel != null) clearLabel.text = armed ? $"CONFIRM: clear {marks}" : "Clear marks";
            if (clearImg != null) clearImg.color = armed ? new Color(0.75f, 0.25f, 0.20f, 1f) : hud.ButtonColor;

            // Esito ecologico dell'ultima valutazione FIS. G/ha residua e' il numero che il
            // selvicoltore guarda per primo dopo un taglio.
            fisLabel.text = builder.FelledCount == 0
                ? $"G {m.BasalAreaHa:F1} m²/ha  ·  {m.DensityHa:F0} N/ha  ·  dbh {m.MeanDbhCm:F1} cm"
                : $"gap light {builder.LastLightPct:F0} %  ·  aridity {builder.LastAridity:F2}  ·  " +
                  $"G {builder.LastResidualGha:F1} m²/ha\nsuitability {builder.LastSuitability:F2}  " +
                  $"·  limiting: {builder.LastLimiting}";

            statusLabel.text = t != null ? t.Status : "";
        }
    }
}
