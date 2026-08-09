using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>
    /// Persistenza dell'inventario: UN SOLO FILE PER AREA, inv_{area}.json sotto
    /// persistentDataPath. Nessun nome da digitare — in visore scrivere su una tastiera
    /// virtuale e' un supplizio, e un rilievo di campo non ha bisogno di essere battezzato:
    /// appartiene all'area su cui e' stato fatto, e quella e' tutta la sua identita'.
    ///
    /// Conseguenza accettata consapevolmente: iniziare un nuovo rilievo AZZERA il precedente
    /// di quell'area. Chi chiama deve chiedere conferma (lo fa SurveyPanel, a due tocchi).
    /// Per non perdere comunque i dati esiste keepBackup: rinomina il vecchio file con un
    /// timestamp invece di sovrascriverlo — nessun nome da digitare, solo una rete di sicurezza.
    /// </summary>
    public static class InventoryStore
    {
        private const string Prefix = "inv_";

        [Serializable]
        private class Wrapper
        {
            public string plotId = "";
            public string savedAtUtc = "";
            public List<StemRecord> stems = new List<StemRecord>();
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "area";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        /// <summary>Il file dell'area. Uno solo, sempre lo stesso.</summary>
        public static string PathFor(string plotId) =>
            Path.Combine(Application.persistentDataPath, $"{Prefix}{Sanitize(plotId)}.json");

        public static bool Exists(string plotId) => File.Exists(PathFor(plotId));

        public static void Save(string plotId, IEnumerable<StemRecord> stems)
        {
            var w = new Wrapper
            {
                plotId = plotId ?? "",
                savedAtUtc = DateTime.UtcNow.ToString("o")
            };
            if (stems != null) w.stems.AddRange(stems);

            try { File.WriteAllText(PathFor(plotId), JsonUtility.ToJson(w, true)); }
            catch (Exception e) { Debug.LogError($"[InventoryStore] salvataggio area '{plotId}' fallito: {e.Message}"); }
        }

        public static List<StemRecord> Load(string plotId)
        {
            if (!Exists(plotId)) return new List<StemRecord>();
            try
            {
                var w = JsonUtility.FromJson<Wrapper>(File.ReadAllText(PathFor(plotId)));
                if (w == null) return new List<StemRecord>();

                // Il file dichiara la propria area: se non corrisponde, meglio non fidarsi —
                // le coordinate di un'altra area disegnerebbero marker plausibili ma sbagliati.
                if (!string.IsNullOrWhiteSpace(w.plotId) &&
                    !string.Equals(w.plotId, plotId, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[InventoryStore] '{Path.GetFileName(PathFor(plotId))}' dichiara " +
                                     $"l'area '{w.plotId}': ignorato.");
                    return new List<StemRecord>();
                }
                return w.stems ?? new List<StemRecord>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[InventoryStore] lettura area '{plotId}' fallita: {e.Message}");
                return new List<StemRecord>();
            }
        }

        /// <summary>
        /// Mette da parte il file corrente con un timestamp, invece di lasciarlo sovrascrivere.
        /// Rete di sicurezza contro un azzeramento involontario; nessun nome da digitare.
        /// </summary>
        public static void Backup(string plotId)
        {
            string src = PathFor(plotId);
            if (!File.Exists(src)) return;
            try
            {
                string dst = Path.Combine(
                    Application.persistentDataPath,
                    $"{Prefix}{Sanitize(plotId)}_{DateTime.Now:yyyyMMdd_HHmmss}.bak.json");
                File.Copy(src, dst, true);
                Debug.Log($"[InventoryStore] copia di sicurezza: {Path.GetFileName(dst)}");
            }
            catch (Exception e) { Debug.LogWarning($"[InventoryStore] backup area '{plotId}' fallito: {e.Message}"); }
        }

        public static void Delete(string plotId)
        {
            try { var p = PathFor(plotId); if (File.Exists(p)) File.Delete(p); }
            catch (Exception e) { Debug.LogError($"[InventoryStore] cancellazione area '{plotId}' fallita: {e.Message}"); }
        }
    }
}
