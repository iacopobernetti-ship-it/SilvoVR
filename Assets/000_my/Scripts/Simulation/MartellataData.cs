using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Registrazione di una martellata: gli alberi segnati per l'abbattimento, le celle di Voronoi
    /// delle buche di rinnovazione (un poligono per cella abbattuta) e i parametri ecologici della
    /// valutazione FIS. Le Y NON si salvano: la scena reale recupera le quote vere del terreno
    /// con un raycast sul suo collider — un terreno piatto simulato e un versante gaussiano non
    /// hanno le stesse altezze, ma hanno le stesse coordinate in pianta.
    /// </summary>
    [Serializable]
    public class MartellataData
    {
        [Serializable] public class FelledTree { public int stemId; public float x, z; public float dbh; }
        [Serializable] public class GapCell { public int stemId; public List<Vector2> polygon = new List<Vector2>(); }

        public string inventoryName = "";
        /// Area a cui la martellata appartiene. Le coordinate non significano nulla altrove.
        public string plotId = "";
        public string scenario = "";
        public int startYear, endYear;
        public string savedAtUtc = "";

        // parametri ecologici (ultima valutazione FIS della martellata)
        public float lightPct, aridity, residualGha, diversity, suitability;
        public string limiting = "-";

        public List<FelledTree> felled = new List<FelledTree>();
        public List<GapCell> gaps = new List<GapCell>();
    }

    /// <summary>
    /// Persistenza della martellata: UN SOLO FILE PER AREA, martellata_{area}.json.
    ///
    /// Stessa filosofia dell'inventario e stessa ragione: in visore non si digitano nomi. Ogni
    /// area conserva la sua ULTIMA martellata, salvata automaticamente quando si lascia la
    /// simulazione. Una nuova martellata sovrascrive la precedente — se serve conservarla,
    /// e' il momento di deciderlo prima di rifarla, non dopo.
    /// </summary>
    public static class MartellataStore
    {
        private const string Prefix = "martellata_";

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "area";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        public static string PathFor(string plotId) =>
            Path.Combine(Application.persistentDataPath, $"{Prefix}{Sanitize(plotId)}.json");

        public static bool Exists(string plotId) => File.Exists(PathFor(plotId));

        public static void Save(string plotId, MartellataData data)
        {
            if (string.IsNullOrWhiteSpace(plotId) || data == null) return;
            data.plotId = plotId;
            data.savedAtUtc = DateTime.UtcNow.ToString("o");
            try
            {
                File.WriteAllText(PathFor(plotId), JsonUtility.ToJson(data, true));
                Debug.Log($"[MartellataStore] area '{plotId}': salvata martellata di " +
                          $"{data.felled.Count} alberi e {data.gaps.Count} buche.");
            }
            catch (Exception e) { Debug.LogError($"[MartellataStore] salvataggio '{plotId}' fallito: {e.Message}"); }
        }

        public static MartellataData Load(string plotId)
        {
            if (!Exists(plotId)) return null;
            try
            {
                var d = JsonUtility.FromJson<MartellataData>(File.ReadAllText(PathFor(plotId)));
                if (d == null) return null;

                // Il file dichiara la propria area: se non corrisponde non si usa. Coordinate di
                // un'altra area disegnerebbero anelli e poligoni plausibili ma sbagliati — e
                // sembrare giusti e' peggio che mancare.
                if (!string.IsNullOrWhiteSpace(d.plotId) &&
                    !string.Equals(d.plotId, plotId, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[MartellataStore] il file di '{plotId}' dichiara l'area " +
                                     $"'{d.plotId}': ignorato.");
                    return null;
                }
                return d;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MartellataStore] lettura '{plotId}' fallita: {e.Message}");
                return null;
            }
        }

        public static void Delete(string plotId)
        {
            try { var p = PathFor(plotId); if (File.Exists(p)) File.Delete(p); }
            catch (Exception e) { Debug.LogError($"[MartellataStore] cancellazione '{plotId}' fallita: {e.Message}"); }
        }
    }
}
