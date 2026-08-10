using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Client for the Artemis FutureClimate API (POST /gardens/bioclim). Fetches WorldClim-style
    /// BIO1..BIO19 for a location/scenario/period, then derives aridity indices (De Martonne,
    /// annual and summer) used to drive the regeneration FIS. Async, non-blocking. Base URL and
    /// query are inspector-set (the endpoint IP will change).
    /// </summary>
    public class FutureClimateClient : MonoBehaviour
    {
        public static FutureClimateClient Instance { get; private set; }

        [Header("Endpoint (will change — set here)")]
        [SerializeField] private string baseUrl = "http://80.211.133.205:8004";
        [SerializeField] private string path = "/gardens/bioclim";

        [Header("Query — station & scenario")]
        [SerializeField] private double lat = 43.5;
        [SerializeField] private double lon = 11.25;
        [SerializeField] private string scenario = "ssp245";
        [SerializeField] private int startYear = 2041;
        [SerializeField] private int endYear = 2060;

        [Header("Aridity calibration — tune on real API output")]
        [Tooltip("Indice di De Martonne ESTIVO considerato del tutto umido (mappa a 0). ATTENZIONE: " +
                 "e' la scala ESTIVA — i riferimenti classici 40/20/10 valgono per l'indice annuale, " +
                 "e usarli sull'estivo e' cio' che schiacciava l'aridita' a 0,97 per ogni scenario.")]
        [SerializeField] private float summerHumidIndex = 20f;
        [Tooltip("Indice di De Martonne estivo considerato del tutto arido (mappa a 1).")]
        [SerializeField] private float summerAridIndex = 5f;
        [Tooltip("Temperatura media del trimestre piu' caldo senza stress termico (mappa a 0), °C.")]
        [SerializeField] private float coolSummerC = 14f;
        [Tooltip("Temperatura media del trimestre piu' caldo di massimo stress termico (mappa a 1), °C.")]
        [SerializeField] private float hotSummerC = 24f;
        [Tooltip("Peso della componente termica nell'aridita' composita; il resto va all'indice di " +
                 "De Martonne. 0 = solo indice (comportamento originale). Serve perche' a questo " +
                 "sito l'indice da solo varia dell'1 % fra scenari: pioggia e temperatura crescono " +
                 "insieme e si compensano, mentre la temperatura estiva da sola si muove molto piu'.")]
        [Range(0f, 1f)][SerializeField] private float heatWeight = 0.5f;

        [Header("Behaviour")]
        [SerializeField] private bool fetchOnStart = true;
        [Tooltip("Use SUMMER De Martonne (driest/warmest quarter) as the aridity source; else annual.")]
        [SerializeField] private bool useSummerAridity = true;

        // Results
        public bool HasData { get; private set; }
        public float AnnualPrecipMm { get; private set; }
        public float AnnualMeanTempC { get; private set; }
        public float SummerPrecipMm { get; private set; }   // warmest quarter
        public float SummerMeanTempC { get; private set; }  // warmest quarter
        public float DeMartonneAnnual { get; private set; }
        public float DeMartonneSummer { get; private set; }
        public float Aridity01 { get; private set; }        // FIS input [0..1], 1 = most arid
        public string Scenario => scenario;
        public string Period => $"{startYear}-{endYear}";
        public int StartYear => startYear;
        public int EndYear => endYear;

        public static readonly string[] Scenarios = { "ssp126", "ssp245", "ssp585" };
        public const int ApiMinYear = 2020;
        public const int ApiMaxYear = 2099;

        public void SetScenarioOnly(string ssp) { scenario = ssp; Fetch(); }

        /// Set a 10-year decade window by its first year (e.g. 2050 -> 2050-2059), clamped to the API range.
        public void SetDecade(int decadeStart)
        {
            int s = Mathf.Clamp(decadeStart, ApiMinYear, ApiMaxYear - 9);
            startYear = s; endYear = s + 9;
            Fetch();
        }

        public event Action OnClimateUpdated;

        [Serializable]
        private struct BioclimRequest
        {
            public string scenario;
            public double lon;
            public double lat;
            public int start_year;
            public int end_year;
            public bool include_monthly_climatology;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            ApplyCalibration();
        }

        /// <summary>Pushes the Inspector values into the static calibration used by the evaluator.
        /// Kept as a separate call so it also runs from OnValidate: tuning these while the scene is
        /// playing then re-fetching is the whole point of having them exposed.</summary>
        private void ApplyCalibration()
        {
            AridityCalibration.SummerHumidI = summerHumidIndex;
            AridityCalibration.SummerAridI = summerAridIndex;
            AridityCalibration.CoolSummerC = coolSummerC;
            AridityCalibration.HotSummerC = hotSummerC;
            AridityCalibration.HeatWeight = heatWeight;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyCalibration();
            // In Play, ricalcola subito l'aridita' con i nuovi parametri senza rifare la chiamata:
            // i dati climatici sono gia' in memoria, cambia solo come vengono normalizzati.
            if (Application.isPlaying && HasData)
            {
                Aridity01 = useSummerAridity
                    ? RegenerationEvaluator.CompositeSummerAridity(SummerPrecipMm, SummerMeanTempC)
                    : RegenerationEvaluator.AridityFromDeMartonne(AnnualPrecipMm, AnnualMeanTempC,
                            AridityCalibration.AnnualHumidI, AridityCalibration.AnnualAridI);
                OnClimateUpdated?.Invoke();
            }
        }
#endif
        private void Start() { if (fetchOnStart) Fetch(); }

        public void Fetch() => StartCoroutine(FetchRoutine());
        public void SetScenario(string ssp, int y0, int y1) { scenario = ssp; startYear = y0; endYear = y1; Fetch(); }

        private IEnumerator FetchRoutine()
        {
            ApplyCalibration();
            string url = baseUrl.TrimEnd('/') + path;
            var payload = new BioclimRequest
            {
                scenario = scenario,
                lon = lon,
                lat = lat,
                start_year = startYear,
                end_year = endYear,
                include_monthly_climatology = true
            };
            string body = JsonUtility.ToJson(payload);   // invariant formatting, exact field names

            using (var req = new UnityWebRequest(url, "POST"))
            {
                byte[] raw = System.Text.Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.certificateHandler = new AcceptAllCertificates();   // allow the plain-HTTP dev endpoint
                req.disposeCertificateHandlerOnDispose = true;

                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok) { Debug.LogError($"[FutureClimate] {req.responseCode} {req.error}\n{req.downloadHandler.text}"); yield break; }

                try { Parse(req.downloadHandler.text); }
                catch (Exception e) { Debug.LogError($"[FutureClimate] parse failed: {e.Message}"); yield break; }

                HasData = true;
                OnClimateUpdated?.Invoke();
                // Log esteso: senza i valori ESTIVI non si puo' capire se un'aridita' che non
                // cambia dipende dal clima o dalla normalizzazione. Sono i due numeri da guardare
                // per tarare AridityCalibration su questo sito.
                Debug.Log($"[FutureClimate] {scenario} {Period}: P={AnnualPrecipMm:F0}mm T={AnnualMeanTempC:F1}°C " +
                          $"| summer P={SummerPrecipMm:F0}mm T={SummerMeanTempC:F1}°C " +
                          $"| DM annual={DeMartonneAnnual:F1} summer={DeMartonneSummer:F1} " +
                          $"| aridity01={Aridity01:F3}");
            }
        }

        private void Parse(string json)
        {
            AnnualPrecipMm = GetNumber(json, "bio12_annual_precipitation_mm");
            AnnualMeanTempC = GetNumber(json, "bio01_annual_mean_temperature_c");
            SummerPrecipMm = GetNumber(json, "bio18_precipitation_of_warmest_quarter_mm");
            SummerMeanTempC = GetNumber(json, "bio10_mean_temperature_of_warmest_quarter_c");

            DeMartonneAnnual = Compute(AnnualPrecipMm, AnnualMeanTempC);
            DeMartonneSummer = Compute(SummerPrecipMm * 4f, SummerMeanTempC);  // quarter mm -> annualised for DM scale

            // Aridita' per il FIS. La versione precedente passava il De Martonne ESTIVO alla
            // normalizzazione con i riferimenti ANNUALI (40/10): per un sito montano fresco come
            // Vallombrosa l'indice estivo vale ~10-11, cioe' era gia' all'estremo arido di quella
            // scala, e ogni scenario finiva a 0,97-0,98. Con la membership "critical" satura da 0,85
            // in su, il FIS vedeva input identici e restituiva sempre lo stesso valore.
            Aridity01 = useSummerAridity
                ? RegenerationEvaluator.CompositeSummerAridity(SummerPrecipMm, SummerMeanTempC)
                : RegenerationEvaluator.AridityFromDeMartonne(AnnualPrecipMm, AnnualMeanTempC,
                        AridityCalibration.AnnualHumidI, AridityCalibration.AnnualAridI);
        }

        private static float Compute(float p, float t) { float d = t + 10f; return d > 0.01f ? p / d : 60f; }

        /// Accepts any certificate — permits the plain-HTTP development endpoint. Replace with a
        /// proper HTTPS endpoint for production.
        private sealed class AcceptAllCertificates : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => true;
        }

        // Minimal number extractor for a JSON key (avoids a full parser dependency).
        private static float GetNumber(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return 0f;
            int colon = json.IndexOf(':', k);
            if (colon < 0) return 0f;
            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-' || json[i] == '+' || json[i] == '.' || json[i] == 'e' || json[i] == 'E')) i++;
            return float.TryParse(json.Substring(start, i - start), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }
    }
}