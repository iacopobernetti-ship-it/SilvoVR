#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Artemis.EditorTools
{
    /// <summary>
    /// Genera il MANIFESTO dei dati LCC dentro StreamingAssets.
    ///
    /// Perche' serve: in una build Android StreamingAssets non e' una cartella, e' una porzione
    /// dell'apk compresso. Unity sa estrarne un file SE gli si dice come si chiama, ma non sa
    /// rispondere alla domanda "cosa c'e' dentro": Directory.GetFiles() non funziona, perche' non
    /// c'e' nessuna directory. L'elenco va quindi preparato QUI, in Editor, dove la cartella e'
    /// vera e si puo' scorrere davvero, e spedito nell'apk insieme ai dati.
    ///
    /// Formato: una riga per file, "percorso/relativo|dimensione_in_byte".
    /// La dimensione non e' un lusso: e' cio' che permette all'installatore di riconoscere una
    /// copia interrotta a meta' (file presente ma corto) e rifarla, invece di lasciarla li' rotta
    /// e far fallire il caricamento dello splat mesi dopo, in aula.
    ///
    /// Da eseguire OGNI VOLTA che si aggiungono o si rigenerano rilievi.
    /// Menu: Artemis → Genera manifesto splat.
    /// </summary>
    public static class SplatManifestBuilder
    {
        /// Cartella dei dati dentro StreamingAssets, e nome del manifesto: devono coincidere con
        /// quelli impostati su SplatInstaller.
        private const string RootFolder = "LCC";
        private const string ManifestName = "_files.txt";

        [MenuItem("Artemis/Genera manifesto splat")]
        public static void Build()
        {
            string root = Path.Combine(Application.streamingAssetsPath, RootFolder);
            if (!Directory.Exists(root))
            {
                EditorUtility.DisplayDialog("Manifesto splat",
                    $"Cartella non trovata:\n{root}\n\nMetti le cartelle delle aree " +
                    $"(Silvo01, Silvo02, …) dentro Assets/StreamingAssets/{RootFolder}/.", "Ok");
                return;
            }

            var sb = new StringBuilder();
            int count = 0;
            long total = 0;

            foreach (string full in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                // I .meta sono roba dell'Editor e nell'apk non servono a niente.
                if (full.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(full) == ManifestName) continue;   // non elencare se stesso

                // Percorso relativo a StreamingAssets, con separatori '/': e' la forma che
                // funziona sia come URL dentro l'apk sia come percorso su disco.
                string rel = full.Substring(Application.streamingAssetsPath.Length + 1)
                                 .Replace('\\', '/');
                long size = new FileInfo(full).Length;

                sb.Append(rel).Append('|').Append(size).Append('\n');
                count++;
                total += size;
            }

            string manifest = Path.Combine(root, ManifestName);
            File.WriteAllText(manifest, sb.ToString());
            AssetDatabase.Refresh();

            string msg = $"{count} file · {total / (1024f * 1024f):F0} MB\n\nScritto in:\n{manifest}";
            Debug.Log("[SplatManifestBuilder] " + msg.Replace("\n", " "));
            EditorUtility.DisplayDialog("Manifesto splat generato", msg, "Ok");
        }
    }
}
#endif
