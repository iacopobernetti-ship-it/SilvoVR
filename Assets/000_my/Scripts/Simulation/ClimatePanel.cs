using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;
using Artemis.Session;

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
    /// TRE INDICI, non nove. La versione precedente mostrava sei numeri grezzi (P e T annuali,
    /// P e T estive) piu' due De Martonne piu' l'aridita': in visore, su un pannello letto in
    /// piedi in mezzo a una classe, era una tabella che nessuno leggeva. Restano i tre che
    /// raccontano la catena per intero, e sono gli stessi che ClimateSensitivityProbe stampa
    /// quando si deve capire perche' il modello non risponde:
    ///   T estiva      — il segnale climatico che si muove davvero fra scenari a questo sito;
    ///   De Martonne   — l'indice di aridita' classico, quello con un nome che il forestale sa;
    ///   aridita' 0-1  — cio' che ENTRA nel FIS, e quindi l'unico numero che spiega la
    ///                   rinnovazione che si vedra' nelle buche.
    /// I valori grezzi restano nel log di FutureClimateClient, dove servono per la taratura.
    ///
    /// La chiamata all'API e' asincrona: durante l'attesa i valori PRECEDENTI restano a schermo
    /// con un puntino di attesa accanto, invece di lasciare il pannello vuoto con la sola scritta
    /// "querying the climate service…" — che era tutto cio' che si vedeva, e non diceva nulla
    /// del bosco.
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
        private TMP_Text periodLabel, indicesLabel, aridityLabel, statusLabel;
        private RectTransform sspRow, decRow;
        private TMP_Text sspTitle, decTitle;
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
            if (!VrSession.WorkAllowed) return;                // attende la sessione

            var page = hud.CreateTab(tabTitle);
            sspTitle = hud.MakeLabel(page, "Emission scenario", 16);

            sspRow = hud.MakeRow(page);
            foreach (var ssp in FutureClimateClient.Scenarios)
            {
                string s = ssp;                                  // copia per la closure
                var (btn, img) = hud.MakeButton(sspRow, s.ToUpperInvariant(), () => SelectScenario(s));
                sspImages[s] = img;
            }

            decTitle = hud.MakeLabel(page, "Decade", 16);
            decRow = hud.MakeRow(page);
            foreach (var d in decades)
            {
                int y = d;
                var (btn, img) = hud.MakeButton(decRow, y.ToString(), () => SelectDecade(y));
                decadeImages[y] = img;
            }

            periodLabel  = hud.MakeLabel(page, "", 17);
            indicesLabel = hud.MakeLabel(page, "", 19);
            aridityLabel = hud.MakeLabel(page, "", 22);
            statusLabel  = hud.MakeLabel(page, "", 14);

            client.OnClimateUpdated += OnUpdated;
            built = true;
            Refresh();
        }

        /// Solo il docente sceglie. Agli studenti i selettori spariscono: vedere quale scenario
        /// e' in vigore fa parte della lezione, poterlo cambiare no.
        private void SelectScenario(string ssp)
        {
            if (client == null || !VrSession.CanCommand) return;
            waiting = true;
            client.SetScenarioOnly(ssp);
        }

        private void SelectDecade(int start)
        {
            if (client == null || !VrSession.CanCommand) return;
            waiting = true;
            client.SetDecade(start);
        }

        /// Il docente pubblica l'esito: gli studenti non interrogano mai l'API. Da oggi pubblica
        /// anche i due indici che spiegano l'aridita', altrimenti la HUD dello studente potrebbe
        /// mostrare solo il risultato senza le ragioni.
        private void OnUpdated()
        {
            waiting = false;
            var st = SessionState.Instance;
            if (st != null && st.IsSpawned && VrSession.IsTeacher && client != null)
                st.SetClimate(client.Scenario, client.StartYear, client.EndYear, client.Aridity01,
                              client.SummerMeanTempC, client.DeMartonneSummer);
            Refresh();
        }

        private void Refresh()
        {
            var hud = VrHud.Instance;
            if (hud == null || client == null || !built) return;

            // In sessione lo stato mostrato e' quello CONDIVISO, non quello del proprio client:
            // uno studente non interroga l'API, quindi il suo FutureClimateClient e' fermo ai
            // valori iniziali e mostrerebbe una scelta che non e' quella della classe.
            var st = SessionState.Instance;
            bool shared = st != null && st.IsSpawned && !VrSession.IsTeacher;

            // Lo studente non sceglie: i selettori spariscono del tutto, restano i valori.
            bool showPickers = VrSession.CanCommand;
            if (sspRow != null && sspRow.gameObject.activeSelf != showPickers)
                sspRow.gameObject.SetActive(showPickers);
            if (decRow != null && decRow.gameObject.activeSelf != showPickers)
                decRow.gameObject.SetActive(showPickers);
            if (sspTitle != null) sspTitle.gameObject.SetActive(showPickers);
            if (decTitle != null) decTitle.gameObject.SetActive(showPickers);

            string curScenario = shared ? st.Scenario.Value.ToString() : client.Scenario;
            int curStart = shared ? st.StartYear.Value : client.StartYear;
            int curEnd   = shared ? st.EndYear.Value   : client.EndYear;

            foreach (var kv in sspImages)
                kv.Value.color = kv.Key == curScenario ? hud.ActiveColor : hud.ButtonColor;
            foreach (var kv in decadeImages)
                kv.Value.color = kv.Key == curStart ? hud.ActiveColor : hud.ButtonColor;

            periodLabel.text = $"{curScenario.ToUpperInvariant()}   {curStart}-{curEnd}";

            // --- i tre indici, dalla fonte giusta ---------------------------------------------
            bool hasData = shared ? st.HasClimateData : client.HasData;
            float tSummer = shared ? st.SummerTempC.Value      : client.SummerMeanTempC;
            float dm      = shared ? st.DeMartonneSummer.Value : client.DeMartonneSummer;
            float arid    = shared ? st.Aridity.Value          : client.Aridity01;

            if (!hasData)
            {
                indicesLabel.text = "";
                aridityLabel.text = "";
                statusLabel.text = waiting ? "querying the climate service…"
                                           : (shared ? "waiting for the teacher's scenario"
                                                     : "no climate data yet");
                return;
            }

            indicesLabel.text = $"summer T {tSummer:F1} °C     De Martonne {dm:F1}";
            aridityLabel.text = $"aridity  {arid:F2}" + Bar(arid);

            statusLabel.text = waiting ? "updating…"
                             : shared ? "the teacher sets the scenario for the class"
                             : "fell again to apply this scenario to a new gap";
        }

        /// <summary>
        /// Barretta di dieci tacche accanto all'aridita'. Un numero fra 0 e 1 non dice da solo
        /// se e' tanto o poco, e in aula lo si guarda da lontano: la lunghezza si legge in un
        /// colpo d'occhio anche senza mettere a fuoco la cifra, ed e' cio' che rende immediato
        /// il confronto fra due scenari.
        /// </summary>
        private static string Bar(float v01)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v01) * 10f), 0, 10);
            return "   [" + new string('#', n) + new string('-', 10 - n) + "]";
        }
    }
}
