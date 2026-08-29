using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;
using Artemis.Inventory;
using Artemis.Session;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Scheda "Felling", presente SOLO nella scena Simulation: si registra alla HUD soltanto se
    /// trova uno StandBuilder, quindi nelle aree non compare affatto — nessun pulsante che non
    /// avrebbe nulla da fare.
    ///
    /// Mostra lo stato del soprassuolo, il conteggio dei segni, i comandi di abbattimento e
    /// l'esito ecologico dell'ultima martellata. Il ritorno all'area salva automaticamente la
    /// martellata.
    ///
    /// GLI INDICI SI VEDONO ANCHE DA STUDENTE. Nella versione precedente il riquadro ecologico
    /// era di fatto muto sul visore degli studenti: restavano il conteggio del soprassuolo
    /// residuo e poco altro, mentre luce nella buca, aridita', G residua e idoneita' erano
    /// proprio i numeri che la lezione vuole far leggere a loro. Il calcolo del FIS avviene
    /// identico su ogni client (stesso inventario, stesso seme, stessa aridita' condivisa),
    /// quindi i valori c'erano gia': mancava soltanto di mostrarli. Il CLIMA sotto cui sono
    /// stati calcolati arriva dallo stato condiviso, cosi' lo studente legge lo scenario della
    /// classe e non quello fermo del proprio FutureClimateClient.
    /// </summary>
    public class SimulationPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Felling";
        [Tooltip("Secondi entro cui confermare 'Clear marks'.")]
        [SerializeField] private float clearConfirmSeconds = 4f;
        [Tooltip("Secondi entro cui confermare 'Reset stand'. Piu' lunghi della conferma dei " +
                 "segni: qui si buttano via un abbattimento e la sua rinnovazione.")]
        [SerializeField] private float resetConfirmSeconds = 6f;

        private bool built;
        private float nextRefresh, clearArmedUntil, resetArmedUntil;
        private StandBuilder builder;
        private SimMarkTool tool;

        private Button fellBtn;
        private Image fellImg;
        private RectTransform commandRow, resetRow, backRow;
        private TMP_Text markedLabel, standLabel, climateLabel, statusLabel, diagLabel;
        private TMP_Text fisTitle, fisIn1, fisIn2, fisOut1, fisOut2, standNowLabel;
        private TMP_Text clearLabel, resetLabel;
        private Image clearImg, resetImg;

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
            if (!VrSession.WorkAllowed) return;

            var page = hud.CreateTab(tabTitle);

            standLabel  = hud.MakeLabel(page, "", 18);
            markedLabel = hud.MakeLabel(page, "", 20);

            commandRow = hud.MakeRow(page);
            var (f, fi) = hud.MakeButton(commandRow, "Fell marked", OnFellClicked);
            var (c, ci) = hud.MakeButton(commandRow, "Clear marks", OnClearClicked);
            fellBtn = f; fellImg = fi;
            clearImg = ci; clearLabel = c.GetComponentInChildren<TMP_Text>();

            // Riga a se': il reset non e' un comando della martellata in corso, e' un comando
            // sulla SIMULAZIONE. Tenerlo accanto a "Fell marked" invitava a premerlo per sbaglio
            // proprio mentre si sta martellando, e i tre pulsanti in fila diventavano stretti,
            // cioe' difficili da centrare col ray.
            resetRow = hud.MakeRow(page);
            var (r, ri) = hud.MakeButton(resetRow, "Reset stand", OnResetClicked);
            resetImg = ri; resetLabel = r.GetComponentInChildren<TMP_Text>();

            // Il ritorno all'area NON sta nella scheda: va nella barra dei comandi, sempre
            // visibile. Chi apre Climate o Map non deve ritrovarsi senza via d'uscita.
            backRow = hud.CommandBar();
            hud.MakeButton(backRow, "Back to plot  (saves marking)", OnBackClicked);

            climateLabel = hud.MakeLabel(page, "", 16);

            // Il blocco del modello ha un titolo suo e due righe distinte, INGRESSI e USCITA.
            // Non e' cosmesi: il senso didattico del FIS sta nel nesso fra le quattro grandezze
            // che entrano e il giudizio che ne esce, e schiacciarle in un'unica riga lo nasconde
            // proprio a chi deve impararlo. Le stesse righe le vedono docente e studenti: il
            // calcolo avviene identico su ogni visore (stesso inventario, stesso seme, stessa
            // aridita' condivisa), quindi non c'era motivo perche' fossero riservate a uno solo.
            // UNA RIGA PER ETICHETTA, mai '\n': MakeLabel fissa preferredHeight a una sola riga
            // (size + 14), quindi un testo su due righe ne disegna due nello spazio di una e
            // finisce sopra l'etichetta successiva. E' il motivo per cui la scheda risultava
            // sovrapposta, e vale per chiunque aggiunga righe qui in futuro.
            fisTitle = hud.MakeLabel(page, "", 15);
            fisIn1   = hud.MakeLabel(page, "", 16);
            fisIn2   = hud.MakeLabel(page, "", 16);
            fisOut1  = hud.MakeLabel(page, "", 18);
            fisOut2  = hud.MakeLabel(page, "", 16);
            standNowLabel = hud.MakeLabel(page, "", 16);

            statusLabel = hud.MakeLabel(page, "", 15);
            diagLabel   = hud.MakeLabel(page, "", 12);

            built = true;
            Refresh();
        }

        // ---- azioni -----------------------------------------------------------------------------

        private void OnFellClicked()
        {
            if (!VrSession.CanCommand) return;      // abbatte solo il docente
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

        /// <summary>
        /// Riporta il bosco a com'era PRIMA della martellata. Due tocchi, come tutto cio' che
        /// distrugge lavoro.
        ///
        /// Serve in aula piu' di quanto sembri: la stessa martellata sotto ssp126 e sotto ssp585
        /// da' rinnovazioni diverse, e quel confronto e' la lezione. Senza reset l'unico modo di
        /// rifarla era uscire dalla simulazione e rientrare, perdendo per strada l'attenzione di
        /// tutti.
        ///
        /// In sessione NON si tocca il bosco direttamente: si azzerano i turni nello stato
        /// condiviso, e ogni visore — docente compreso — si ricostruisce da se' quando vede i
        /// turni tornare indietro. Un percorso solo, quindi nessuno puo' restare con un bosco
        /// diverso da quello degli altri.
        /// </summary>
        private void OnResetClicked()
        {
            if (!VrSession.CanCommand) return;
            if (builder == null) return;

            if (Time.time <= resetArmedUntil)
            {
                resetArmedUntil = 0f;

                var st = SessionState.Instance;
                if (st != null && st.IsSpawned) st.ResetStand();
                else builder.Rebuild();          // fuori sessione: si ricostruisce dal proprio file

                Tool()?.ClearMarks();
                if (statusLabel != null) statusLabel.text = "stand restored to its pre-marking state";
                return;
            }

            // Niente da annullare: nessun abbattimento e nessun segno.
            var t = Tool();
            if (builder.FelledCount == 0 && (t == null || t.MarkedCount == 0)) return;
            resetArmedUntil = Time.time + resetConfirmSeconds;
        }

        /// Uscendo si salva la martellata — ma MAI una vuota sopra una piena.
        ///
        /// La versione precedente salvava sempre, e bastava rientrare in Simulation e tornare
        /// indietro senza abbattere per azzerare il lavoro fatto prima: il file veniva riscritto
        /// con zero alberi e da quel momento "Show last marking" non aveva piu' nulla da mostrare.
        /// Una martellata vuota non e' un risultato da conservare, e' un giro a vuoto.
        private void OnBackClicked()
        {
            // In sessione riporta indietro la classe intera, quindi decide il docente.
            if (!VrSession.CanCommand) return;
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
            SetLine(standLabel, $"{builder.CurrentAreaId}  ·  {m.N} standing  ·  {builder.FelledCount} felled  ·  " +
                                $"{builder.YoungCount} seedlings");

            int marks = t != null ? t.MarkedCount : 0;
            int props = t != null ? t.ProposedCount : 0;

            if (VrSession.IsStudent)
                SetLine(markedLabel, props == 0
                    ? "aim at a tree and pull the trigger to propose it"
                    : $"{props} trees proposed by the class  ·  {marks} marked by the teacher");
            else
                SetLine(markedLabel, marks == 0
                    ? (props > 0 ? $"{props} proposed by students — mark the ones you agree with"
                                 : "aim at a tree and pull the trigger to mark it")
                    : $"{marks} marked for felling  ·  {props} student proposals");

            // Agli studenti i comandi si NASCONDONO invece di restare grigi: un pulsante inerte
            // invita comunque a premerlo, e a chiedersi perche' non risponde.
            bool canAct = VrSession.CanCommand;
            if (commandRow != null && commandRow.gameObject.activeSelf != canAct)
                commandRow.gameObject.SetActive(canAct);
            if (resetRow != null && resetRow.gameObject.activeSelf != canAct)
                resetRow.gameObject.SetActive(canAct);
            if (backRow != null && backRow.gameObject.activeSelf != canAct)
                backRow.gameObject.SetActive(canAct);

            if (fellBtn != null) fellBtn.interactable = canAct && marks > 0;
            if (fellImg != null) fellImg.color = (canAct && marks > 0) ? hud.ActiveColor : hud.ButtonColor;

            bool armed = Time.time <= clearArmedUntil;
            if (clearLabel != null) clearLabel.text = armed ? $"CONFIRM: clear {marks}" : "Clear marks";
            if (clearImg != null) clearImg.color = armed ? new Color(0.75f, 0.25f, 0.20f, 1f) : hud.ButtonColor;

            RefreshResetButton(hud);

            // --- clima sotto cui si sta simulando, uguale per tutti ----------------------------
            SetLine(climateLabel, ClimateLine());

            // --- esito ecologico: A TUTTI, non solo al docente ---------------------------------
            // G/ha residua e' il numero che il selvicoltore guarda per primo dopo un taglio;
            // idoneita' e fattore limitante sono la risposta del FIS a quella buca sotto quel
            // clima, cioe' il nesso che la lezione vuole far vedere.
            if (builder.FelledCount == 0)
            {
                // Prima di abbattere il modello non ha ancora nulla da dire: si mostra il
                // soprassuolo cosi' com'e', che e' il termine di paragone del dopo.
                SetLine(fisTitle, "stand before felling");
                SetLine(fisIn1, $"G {m.BasalAreaHa:F1} m²/ha  ·  {m.DensityHa:F0} N/ha");
                SetLine(fisIn2, $"dbh {m.MeanDbhCm:F1} cm  ·  h {m.MeanHeightM:F1} m");
                SetLine(fisOut1, "");
                SetLine(fisOut2, "");
                SetLine(standNowLabel, "");
            }
            else
            {
                SetLine(fisTitle, "regeneration model (Mamdani FIS)");

                // I QUATTRO ingressi del sistema, due per riga.
                SetLine(fisIn1, $"gap light {builder.LastLightPct:F0} %   ·   " +
                                $"aridity {builder.LastAridity:F2}");
                SetLine(fisIn2, $"residual G {builder.LastResidualGha:F1} m²/ha   ·   " +
                                $"diversity {builder.StationDiversity:F2}");

                // L'uscita: idoneita', fattore limitante, e cio' che ne e' seguito sul terreno.
                SetLine(fisOut1, $"suitability {builder.LastSuitability:F2}{Bar(builder.LastSuitability)}");

                // Spezzata a mano in due: era la riga piu' lunga del pannello, e quella che sul
                // visore del docente andava a capo per prima.
                SetLine(fisOut2, $"limiting: {builder.LastLimiting}   ·   {builder.YoungCount} seedlings");
                SetLine(standNowLabel, $"now {m.BasalAreaHa:F1} m²/ha  ·  {m.DensityHa:F0} N/ha");
            }

            SetLine(statusLabel, t != null ? t.Status : "");

            if (diagLabel != null)
            {
                var sync = FindFirstObjectByType<Artemis.Session.SimulationSyncVR>();
                SetLine(diagLabel, sync != null ? sync.Diagnostics : "");
            }
        }

        /// Scenario e periodo in vigore, con i due indici che spiegano l'aridita'. In sessione la
        /// fonte e' lo stato condiviso: il FutureClimateClient dello studente non ha mai
        /// interrogato l'API e mostrerebbe i valori con cui e' nato.
        private string ClimateLine()
        {
            var st = SessionState.Instance;
            if (st != null && st.IsSpawned && !VrSession.IsTeacher)
            {
                if (!st.HasClimateData) return "climate: waiting for the teacher";
                return $"{st.Scenario.Value} {st.StartYear.Value}-{st.EndYear.Value}  ·  " +
                       $"summer T {st.SummerTempC.Value:F1} °C  ·  DM {st.DeMartonneSummer.Value:F1}  ·  " +
                       $"aridity {st.Aridity.Value:F2}";
            }

            var c = FutureClimateClient.Instance;
            if (c == null || !c.HasData) return "climate: no data yet";
            return $"{c.Scenario} {c.Period}  ·  summer T {c.SummerMeanTempC:F1} °C  ·  " +
                   $"DM {c.DeMartonneSummer:F1}  ·  aridity {c.Aridity01:F2}";
        }

        /// <summary>
        /// Scrive un'etichetta E le da' l'altezza che le serve davvero.
        ///
        /// Perche' non basta evitare gli a-capo espliciti: VrHud.MakeLabel fissa preferredHeight
        /// a UNA riga (size + 14), ma quando il testo va a capo non lo decidiamo noi — lo decide
        /// TMP in base alla larghezza disponibile. Una riga lunga si spezza in due e la seconda
        /// finisce sopra l'etichetta successiva. Sul visore del docente si vedeva piu' che su
        /// quello dello studente per un motivo banale: il suo pannello e' piu' affollato (riga
        /// Reset e barra "Back to plot"), quindi si va a capo prima.
        ///
        /// Qui si misurano le righe EFFETTIVAMENTE disegnate e si adegua l'altezza. Le etichette
        /// vuote si ritirano a zero, cosi' non lasciano buchi nel pannello.
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

        /// Barretta di dieci tacche accanto all'idoneita'. Un numero fra 0 e 1 non dice da solo se
        /// e' tanto o poco, e in aula lo si guarda da lontano: la lunghezza si legge a colpo
        /// d'occhio, ed e' cio' che rende immediato il confronto fra due scenari climatici.
        private static string Bar(float v01)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v01) * 10f), 0, 10);
            return "   [" + new string('#', n) + new string('-', 10 - n) + "]";
        }

        private void RefreshResetButton(VrHud hud)
        {
            if (resetLabel == null) return;

            bool armed = Time.time <= resetArmedUntil;
            if (armed)
            {
                resetLabel.text = $"CONFIRM: undo {builder.FelledCount} felled";
                if (resetImg != null) resetImg.color = new Color(0.75f, 0.25f, 0.20f, 1f);
            }
            else
            {
                resetLabel.text = "Reset stand";
                if (resetImg != null) resetImg.color = hud.ButtonColor;
            }
        }
    }
}
