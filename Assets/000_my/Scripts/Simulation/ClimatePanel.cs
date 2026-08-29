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

        // I pulsanti, non solo le loro immagini: agli studenti servono VISIBILI ma inerti, e per
        // renderli inerti serve il Button.
        private readonly System.Collections.Generic.List<Button> pickerButtons =
            new System.Collections.Generic.List<Button>();
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
                pickerButtons.Add(btn);
            }

            decTitle = hud.MakeLabel(page, "Decade", 16);
            decRow = hud.MakeRow(page);
            foreach (var d in decades)
            {
                int y = d;
                var (btn, img) = hud.MakeButton(decRow, y.ToString(), () => SelectDecade(y));
                decadeImages[y] = img;
                pickerButtons.Add(btn);
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

            // Se una martellata c'e' gia', il nuovo scenario si applica SUBITO alle buche
            // esistenti invece che al prossimo taglio. Gli studenti ci arrivano per un'altra
            // strada — SetSharedClimate sul loro StandBuilder — ma il docente e chi lavora da
            // solo non passano di li', perche' sono la FONTE del dato e non il destinatario.
            var b = FindFirstObjectByType<StandBuilder>();
            if (b != null && b.FelledCount > 0) b.ReapplyClimate();

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

            // I selettori si VEDONO SEMPRE, anche allo studente: qui non sono un comando, sono
            // l'informazione principale della scheda. Sapere quale scenario e quale decade sono
            // in vigore fa parte della lezione — e' il parametro sotto cui si sta osservando il
            // bosco — mentre poterli cambiare no. Nascondendoli, lo studente non aveva modo di
            // sapere cosa stesse guardando; e siccome lo stato mostrato viene dalla sessione, si
            // aggiorna da solo appena il docente cambia scelta.
            bool canPick = VrSession.CanCommand;
            foreach (var b in pickerButtons) if (b != null) b.interactable = canPick;
            if (sspTitle != null)
                SetLine(sspTitle, canPick ? "Emission scenario" : "Emission scenario (set by the teacher)");
            if (decTitle != null)
                SetLine(decTitle, canPick ? "Decade" : "Decade (set by the teacher)");

            string curScenario = shared ? st.Scenario.Value.ToString() : client.Scenario;
            int curStart = shared ? st.StartYear.Value : client.StartYear;
            int curEnd   = shared ? st.EndYear.Value   : client.EndYear;

            // Il verde marca sempre la scelta in vigore, per tutti. Le altre voci restano piu'
            // spente per chi non puo' sceglierle, cosi' si capisce a colpo d'occhio che sono da
            // leggere e non da premere.
            Color idle = canPick ? hud.ButtonColor : new Color(0.16f, 0.16f, 0.16f, 1f);
            foreach (var kv in sspImages)
                kv.Value.color = kv.Key == curScenario ? hud.ActiveColor : idle;
            foreach (var kv in decadeImages)
                kv.Value.color = kv.Key == curStart ? hud.ActiveColor : idle;

            SetLine(periodLabel, $"{curScenario.ToUpperInvariant()}   {curStart}-{curEnd}");

            // --- i tre indici, dalla fonte giusta ---------------------------------------------
            bool hasData = shared ? st.HasClimateData : client.HasData;
            float tSummer = shared ? st.SummerTempC.Value      : client.SummerMeanTempC;
            float dm      = shared ? st.DeMartonneSummer.Value : client.DeMartonneSummer;
            float arid    = shared ? st.Aridity.Value          : client.Aridity01;

            if (!hasData)
            {
                SetLine(indicesLabel, "");
                SetLine(aridityLabel, "");
                SetLine(statusLabel, waiting ? "querying the climate service…"
                                             : (shared ? "waiting for the teacher's scenario"
                                                       : "no climate data yet"));
                return;
            }

            SetLine(indicesLabel, $"summer T {tSummer:F1} °C     De Martonne {dm:F1}");
            SetLine(aridityLabel, $"aridity  {arid:F2}" + Bar(arid));

            SetLine(statusLabel, waiting ? "updating…"
                               : shared ? "the teacher sets the scenario for the class"
                               : "fell again to apply this scenario to a new gap");
        }

        /// <summary>
        /// Scrive un'etichetta E le da' l'altezza che le serve. VrHud.MakeLabel fissa
        /// preferredHeight a UNA riga, ma quando TMP manda a capo un testo lungo la seconda riga
        /// finisce sopra l'etichetta successiva — ed e' quello che rendeva illeggibile il
        /// pannello sul visore piu' affollato. Vuota = altezza zero, niente buchi.
        /// </summary>
        private static void SetLine(TMP_Text t, string text)
        {
            if (t == null) return;
            if (t.text != text) t.text = text;

            var le = t.GetComponent<LayoutElement>();
            if (le == null) return;

            if (string.IsNullOrEmpty(text))
            {
                if (le.preferredHeight != 0f) le.preferredHeight = 0f;
                return;
            }

            t.ForceMeshUpdate();
            int lines = Mathf.Max(1, t.textInfo.lineCount);
            float h = (t.fontSize + 6f) * lines + 8f;
            if (!Mathf.Approximately(le.preferredHeight, h)) le.preferredHeight = h;
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
