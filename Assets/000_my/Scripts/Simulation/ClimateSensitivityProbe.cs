using System.Collections;
using System.Text;
using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Diagnostic probe for the aridity chain. Queries the FutureClimate API across every scenario
    /// and a range of decades, then prints one table with the raw climate values, the De Martonne
    /// indices and the resulting FIS input — plus the suitability the regeneration model would
    /// return for a fixed reference gap.
    ///
    /// Why this exists: field testing showed a constant suitability, and from inside the running
    /// application there was no way to tell whether the cause was the climate signal being genuinely
    /// flat at this site, the normalisation squashing it, or the fuzzy sets saturating. The table
    /// separates the three: look at the SUMMER columns to see how much the projection actually
    /// moves, at ARID01 to see how much of that survives normalisation, and at SUIT to see what
    /// reaches the output.
    ///
    /// Tune AridityCalibration until ARID01 spans a useful part of 0..1 across the scenarios that
    /// matter for the teaching session — the responsive band of the fuzzy sets is roughly 0.20-0.95.
    ///
    /// Put this on any GameObject in the Simulation scene alongside FutureClimateClient, enter Play,
    /// and run it from the component context menu.
    /// </summary>
    public class ClimateSensitivityProbe : MonoBehaviour
    {
        [Header("Sweep")]
        [Tooltip("Decadi da interrogare (anno iniziale).")]
        [SerializeField] private int[] decades = { 2030, 2050, 2070, 2090 };
        [Tooltip("Attesa fra le chiamate, per non sovraccaricare l'endpoint di sviluppo.")]
        [SerializeField] private float delaySeconds = 0.4f;

        [Header("Buca di riferimento per il confronto")]
        [Tooltip("Luce relativa nella buca [%].")]
        [SerializeField] private float refLightPct = 55f;
        [Tooltip("Area basimetrica residua [m²/ha].")]
        [SerializeField] private float refBasalArea = 30f;
        [Tooltip("Diversita' strutturale [0..1].")]
        [SerializeField] private float refDiversity = 0.5f;

        [ContextMenu("Esegui sweep climatico")]
        public void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ClimateProbe] serve la modalita' Play."); return; }
            var client = FutureClimateClient.Instance;
            if (client == null) { Debug.LogError("[ClimateProbe] nessun FutureClimateClient in scena."); return; }
            StopAllCoroutines();
            StartCoroutine(Sweep(client));
        }

        private IEnumerator Sweep(FutureClimateClient client)
        {
            var evaluator = new RegenerationEvaluator();
            var sb = new StringBuilder();
            sb.AppendLine("[ClimateProbe] sweep scenari x decadi");
            sb.AppendLine($"  buca di riferimento: luce {refLightPct:F0} %, G {refBasalArea:F1} m²/ha, diversita' {refDiversity:F2}");
            sb.AppendLine($"  taratura: DM estivo {AridityCalibration.SummerHumidI:F0}->{AridityCalibration.SummerAridI:F0}, " +
                          $"T estiva {AridityCalibration.CoolSummerC:F0}->{AridityCalibration.HotSummerC:F0} °C, " +
                          $"peso termico {AridityCalibration.HeatWeight:F2}");
            sb.AppendLine();
            sb.AppendLine("  SCENARIO  DECADE   Pann  Tann   Pest  Test   DMann  DMest   ARID01   SUIT  LIMITING");

            float minA = 1f, maxA = 0f, minS = 1f, maxS = 0f;

            foreach (var ssp in FutureClimateClient.Scenarios)
            {
                foreach (int d in decades)
                {
                    client.SetScenario(ssp, d, d + 9);

                    // Attende la risposta: HasData resta vero fra le chiamate, quindi si aspetta
                    // l'evento contando i frame invece di leggere un flag che non cambia.
                    bool done = false;
                    void OnUpdated() => done = true;
                    client.OnClimateUpdated += OnUpdated;
                    float t = 0f;
                    while (!done && t < 15f) { t += Time.deltaTime; yield return null; }
                    client.OnClimateUpdated -= OnUpdated;

                    if (!done)
                    {
                        sb.AppendLine($"  {ssp,-9} {d}s    (nessuna risposta entro 15 s)");
                        continue;
                    }

                    float a = client.Aridity01;
                    var res = evaluator.Evaluate(refLightPct, a, refBasalArea, refDiversity);

                    minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
                    minS = Mathf.Min(minS, res.Value); maxS = Mathf.Max(maxS, res.Value);

                    sb.AppendLine($"  {ssp,-9} {d}s  {client.AnnualPrecipMm,5:F0} {client.AnnualMeanTempC,5:F1}  " +
                                  $"{client.SummerPrecipMm,5:F0} {client.SummerMeanTempC,5:F1}  " +
                                  $"{client.DeMartonneAnnual,6:F1} {client.DeMartonneSummer,6:F1}   " +
                                  $"{a,6:F3}  {res.Value,5:F3}  {res.Limiting}");

                    if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);
                }
            }

            sb.AppendLine();
            sb.AppendLine($"  escursione aridita':    {minA:F3} -> {maxA:F3}   (ampiezza {maxA - minA:F3})");
            sb.AppendLine($"  escursione suitability: {minS:F3} -> {maxS:F3}   (ampiezza {maxS - minS:F3})");
            sb.AppendLine();
            if (maxA - minA < 0.10f)
                sb.AppendLine("  DIAGNOSI: l'aridita' varia troppo poco. Restringi la scala del De Martonne " +
                              "estivo attorno ai valori reali della colonna DMest, oppure alza HeatWeight " +
                              "per dare piu' peso alla temperatura estiva (colonna Test), che al variare " +
                              "degli scenari si muove piu' dell'indice.");
            else if (maxS - minS < 0.10f)
                sb.AppendLine("  DIAGNOSI: l'aridita' varia ma la suitability no. Il segnale si perde nei " +
                              "fuzzy set: verifica che ARID01 cada nella fascia reattiva (~0,20-0,95) e non " +
                              "oltre la saturazione di 'critical'.");
            else
                sb.AppendLine("  DIAGNOSI: la catena risponde — il segnale climatico arriva fino all'output.");

            Debug.Log(sb.ToString());
        }

        [ContextMenu("Mappa di risposta del FIS (senza API)")]
        public void ResponseSurface()
        {
            var ev = new RegenerationEvaluator();
            var sb = new StringBuilder();
            sb.AppendLine($"[ClimateProbe] suitability al variare di luce e aridita' " +
                          $"(G {refBasalArea:F0} m²/ha, diversita' {refDiversity:F2})");
            sb.Append("   luce\\arid ");
            float[] arid = { 0.10f, 0.25f, 0.40f, 0.55f, 0.70f, 0.85f, 1.00f };
            foreach (var a in arid) sb.Append($"{a,7:F2}");
            sb.AppendLine();
            foreach (float L in new[] { 5f, 10f, 15f, 20f, 30f, 40f, 55f, 75f })
            {
                sb.Append($"   {L,6:F0} %  ");
                foreach (var a in arid) sb.Append($"{ev.Evaluate(L, a, refBasalArea, refDiversity).Value,7:F2}");
                sb.AppendLine();
            }
            sb.AppendLine("   Una riga o colonna con valori tutti uguali segnala una zona in cui il modello " +
                          "non discrimina.");
            Debug.Log(sb.ToString());
        }
    }
}
