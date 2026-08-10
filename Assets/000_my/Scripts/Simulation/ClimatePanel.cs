using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Scheda "Climate": scenario SSP e decade che alimentano l'aridita' del FIS, quindi la
    /// quantita' di rinnovazione che compare nelle buche. E' il comando piu' "didattico" della
    /// simulazione — la stessa martellata sotto ssp126 e sotto ssp585 da' esiti diversi, ed e'
    /// esattamente il confronto che si vuole far vedere in aula.
    ///
    /// Si registra solo dove c'e' un FutureClimateClient, quindi nella sola scena Simulation.
    ///
    /// La chiamata all'API e' asincrona: finche' non risponde restano i valori precedenti, e la
    /// riga di stato lo dichiara invece di lasciar credere che il dato sia gia' aggiornato.
    /// </summary>
    public class ClimatePanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Climate";
        [Tooltip("Decadi selezionabili (anno iniziale).")]
        [SerializeField] private int[] decades = { 2030, 2050, 2070, 2090 };

        private bool built;
        private float nextRefresh;
        private FutureClimateClient client;

        private readonly System.Collections.Generic.Dictionary<string, Image> sspImages =
            new System.Collections.Generic.Dictionary<string, Image>();
        private readonly System.Collections.Generic.Dictionary<int, Image> decadeImages =
            new System.Collections.Generic.Dictionary<int, Image>();
        private TMP_Text valuesLabel, aridityLabel, statusLabel;
        private bool waiting;

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.3f;
            Refresh();
        }

        private void OnDestroy()
        {
            if (client != null) client.OnClimateUpdated -= OnUpdated;
        }

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;

            client = FutureClimateClient.Instance;
            if (client == null) { enabled = false; return; }   // non e' la scena giusta

            var page = hud.CreateTab(tabTitle);
            hud.MakeLabel(page, "Emission scenario", 16);

            var sspRow = hud.MakeRow(page);
            foreach (var ssp in FutureClimateClient.Scenarios)
            {
                string s = ssp;                                  // copia per la closure
                var (btn, img) = hud.MakeButton(sspRow, s.ToUpperInvariant(), () => SelectScenario(s));
                sspImages[s] = img;
            }

            hud.MakeLabel(page, "Decade", 16);
            var decRow = hud.MakeRow(page);
            foreach (var d in decades)
            {
                int y = d;
                var (btn, img) = hud.MakeButton(decRow, y.ToString(), () => SelectDecade(y));
                decadeImages[y] = img;
            }

            valuesLabel  = hud.MakeLabel(page, "", 15);
            aridityLabel = hud.MakeLabel(page, "", 20);
            statusLabel  = hud.MakeLabel(page, "", 14);

            client.OnClimateUpdated += OnUpdated;
            built = true;
            Refresh();
        }

        private void SelectScenario(string ssp)
        {
            if (client == null) return;
            waiting = true;
            client.SetScenarioOnly(ssp);
        }

        private void SelectDecade(int start)
        {
            if (client == null) return;
            waiting = true;
            client.SetDecade(start);
        }

        private void OnUpdated() { waiting = false; Refresh(); }

        private void Refresh()
        {
            var hud = VrHud.Instance;
            if (hud == null || client == null || !built) return;

            foreach (var kv in sspImages)
                kv.Value.color = kv.Key == client.Scenario ? hud.ActiveColor : hud.ButtonColor;
            foreach (var kv in decadeImages)
                kv.Value.color = kv.Key == client.StartYear ? hud.ActiveColor : hud.ButtonColor;

            if (!client.HasData)
            {
                valuesLabel.text = "";
                aridityLabel.text = "";
                statusLabel.text = waiting ? "querying the climate service…" : "no climate data yet";
                return;
            }

            valuesLabel.text =
                $"annual  P {client.AnnualPrecipMm:F0} mm   T {client.AnnualMeanTempC:F1} °C\n" +
                $"summer  P {client.SummerPrecipMm:F0} mm   T {client.SummerMeanTempC:F1} °C\n" +
                $"De Martonne  annual {client.DeMartonneAnnual:F1}  ·  summer {client.DeMartonneSummer:F1}";

            aridityLabel.text = $"aridity {client.Aridity01:F2}   ({client.Scenario} {client.Period})";
            statusLabel.text = waiting ? "querying the climate service…"
                                       : "fell again to apply this scenario to a new gap";
        }
    }
}
