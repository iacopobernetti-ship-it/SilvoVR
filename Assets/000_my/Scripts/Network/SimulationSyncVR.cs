using System.Collections.Generic;
using UnityEngine;
using Artemis.Inventory;
using Artemis.Regeneration;
using Artemis.Vr;

namespace Artemis.Session
{
    /// <summary>
    /// Allinea la scena Simulation allo stato condiviso, cosi' ogni visore mostra lo STESSO
    /// bosco, le stesse proposte e la stessa rinnovazione.
    ///
    ///  - soprassuolo: lo pubblica il docente entrando (il suo inventario dell'area di
    ///    provenienza) e ogni studente lo ricostruisce dal contenuto ricevuto — mai dai propri
    ///    file, che contengono i SUOI rilievi e darebbero a ciascuno un bosco diverso;
    ///  - abbattimenti: ogni turno si riapplica localmente con il SEME condiviso, quindi le
    ///    piantine nascono negli stessi punti ovunque, anche per chi si collega a lezione
    ///    iniziata: gli basta riapplicare i turni in ordine;
    ///  - clima: lo sceglie il docente, gli altri lo subiscono — ed e' giusto cosi', e' il
    ///    parametro della lezione.
    ///
    /// Fuori sessione non fa nulla, quindi la scena resta perfettamente usabile da soli.
    /// Va sull'oggetto SimTools della scena Simulation, accanto a StandBuilder.
    /// </summary>
    public class SimulationSyncVR : MonoBehaviour
    {
        [SerializeField] private StandBuilder builder;

        [Tooltip("Secondi di attesa prima di segnalare che il docente non ha alberi da " +
                 "condividere. Serve una tolleranza: nei primi istanti StandBuilder puo' non " +
                 "aver ancora costruito, e un allarme immediato sarebbe un falso allarme a ogni " +
                 "ingresso in simulazione.")]
        [SerializeField] private float waitBeforeComplaining = 3f;

        private int builtInventoryVersion = -1;
        private bool published;
        private int appliedRounds;
        private string lastClimate = "";
        private float startedAt;
        private bool complained;

        /// <summary>Diagnostica leggibile in visore: la mostra SimulationPanel.</summary>
        public string Diagnostics { get; private set; } = "";

        private void Start()
        {
            if (builder == null) builder = FindFirstObjectByType<StandBuilder>();
            startedAt = Time.time;
        }

        private void Update()
        {
            var st = SessionState.Instance;
            if (st == null || !st.IsSpawned || builder == null) return;

            SyncInventory(st);
            SyncFelling(st);
            SyncClimate(st);

            Diagnostics =
                $"{(VrSession.IsTeacher ? "teacher" : "student")} · shared {st.Inventory.Count} stems · " +
                $"local {(builder.OriginalStems != null ? builder.OriginalStems.Count : 0)} · " +
                $"published {published} · rounds {appliedRounds}/{st.RoundCount} · area '{st.PlotId.Value}'";
        }

        // ---- soprassuolo ---------------------------------------------------------------------

        private void SyncInventory(SessionState st)
        {
            if (st.Inventory == null || st.Inventory.Count == 0)
            {
                PublishIfTeacher(st);
                return;
            }

            int version = Version(st);
            if (version == builtInventoryVersion) return;

            builder.SetPlotArea(st.PlotAreaM2.Value);
            builder.BuildShared(st.ReadInventory(), st.PlotId.Value.ToString());

            builtInventoryVersion = version;
            appliedRounds = 0;          // un nuovo bosco riparte intatto
        }

        /// Il docente condivide il proprio soprassuolo appena entra: la classe comincia gia'
        /// allineata, senza che nessuno debba premere nulla.
        private void PublishIfTeacher(SessionState st)
        {
            if (published || !VrSession.IsTeacher) return;

            var stems = builder.OriginalStems;
            if (stems == null || stems.Count == 0)
            {
                // Il docente non ha ancora un soprassuolo da condividere. Puo' essere questione
                // di un frame (StandBuilder deve ancora costruire) oppure di un inventario vuoto
                // per quell'area — che e' un errore d'uso, non un guasto, e va detto invece di
                // lasciare una classe davanti a un prato.
                if (Time.time - startedAt > waitBeforeComplaining && !complained)
                {
                    complained = true;
                    Debug.LogWarning($"[SimulationSyncVR] il docente non ha alberi da condividere " +
                                     $"per l'area '{builder.CurrentAreaId}': rileva qualche pianta " +
                                     "prima di aprire la simulazione.");
                }
                return;
            }

            float area = AreaM2Of(builder.CurrentAreaId);
            st.SetPlot(builder.CurrentAreaId, area);
            st.PublishInventory(new List<StemRecord>(stems));
            published = true;

            // Il soprassuolo appena pubblicato E' quello che il docente ha GIA' costruito in
            // locale (StandBuilder.Start l'ha caricato dal suo file un attimo fa): si marca la
            // versione come gia' costruita, altrimenti al frame successivo SyncInventory vedrebbe
            // un inventario condiviso "nuovo" e lo ricostruirebbe identico — doppio build, doppio
            // riposizionamento del rig, doppio OnRebuilt. Era visibile nel log come una coppia
            // di "rig riposizionato" a pochi frame di distanza, e raddoppiava esattamente la
            // finestra in cui rig e CharacterController passano di mano.
            builtInventoryVersion = Version(st);

            Debug.Log($"[SimulationSyncVR] condiviso il soprassuolo di '{builder.CurrentAreaId}' " +
                      $"({stems.Count} alberi, {area:F0} m²).");
        }

        /// Rilevatore di cambiamento a buon mercato: il docente sostituisce l'intera lista,
        /// quindi conteggio piu' primo e ultimo id bastano a distinguere due inventari.
        private static int Version(SessionState st)
        {
            var inv = st.Inventory;
            if (inv == null || inv.Count == 0) return 0;
            int h = inv.Count * 397;
            h = h * 31 + inv[0].StemId;
            h = h * 31 + inv[inv.Count - 1].StemId;
            return h;
        }

        private static float AreaM2Of(string plotId)
        {
            var flow = AreaFlow.Instance;
            if (flow == null || string.IsNullOrEmpty(plotId)) return 400f;
            foreach (var a in flow.Areas)
                if (string.Equals(a.sceneName, plotId, System.StringComparison.OrdinalIgnoreCase))
                    return a.areaM2;
            return 400f;
        }

        // ---- abbattimenti ------------------------------------------------------------------------

        private void SyncFelling(SessionState st)
        {
            int total = st.RoundCount;

            // REGRESSIONE dei turni = il docente ha premuto "Reset stand".
            //
            // Un abbattimento non si annulla da se': gli alberi sono stati distrutti e la
            // rinnovazione e' nata. Si RIPRISTINA il solo popolamento — gli abbattuti tornano in
            // piedi dai record dell'inventario, che non se ne sono mai andati, e le piantine
            // spariscono — lasciando intatti suolo, muri e luce.
            //
            // Non si usa BuildShared, che ricostruirebbe tutto: il quadrato di suolo nasce dallo
            // stesso inventario e verrebbe rifatto identico, ma nel frattempo il suo collider
            // sparirebbe per un frame — e in quel frame la gravita' lavora indisturbata. Era
            // esattamente la caduta nel vuoto che gli studenti prendevano dopo il reset.
            if (total < appliedRounds)
            {
                Debug.Log($"[SimulationSyncVR] turni tornati da {appliedRounds} a {total}: " +
                          "ripristino il popolamento (il suolo resta dov'e').");
                builder.RestoreStand();
                appliedRounds = 0;

                // Restano da riapplicare i turni superstiti: dopo un reset completo sono zero,
                // ma il ciclo qui sotto copre gratis anche un eventuale annullamento parziale.
            }

            if (appliedRounds >= total) return;

            for (int round = appliedRounds; round < total; round++)
            {
                var ids = st.StemsOfRound(round);
                if (ids.Count > 0) builder.FellMany(ids, st.SeedOfRound(round));
            }
            appliedRounds = total;
        }

        // ---- clima ---------------------------------------------------------------------------------

        /// Il docente interroga l'API e ne pubblica l'esito; gli studenti NON chiamano il servizio
        /// (dieci visori sullo stesso endpoint sono dieci richieste identiche) ma applicano il
        /// valore ricevuto, cosi' il FIS lavora ovunque sullo stesso numero.
        private void SyncClimate(SessionState st)
        {
            string sig = $"{st.Scenario.Value}|{st.StartYear.Value}|{st.EndYear.Value}|{st.Aridity.Value:F3}";
            if (sig == lastClimate) return;
            lastClimate = sig;

            if (VrSession.IsTeacher) return;    // il docente e' la fonte, non il destinatario

            builder.SetSharedClimate(st.Scenario.Value.ToString(), st.StartYear.Value,
                                     st.EndYear.Value, st.Aridity.Value);
        }
    }
}
