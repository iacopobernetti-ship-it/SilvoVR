using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Artemis.Vr
{
    /// <summary>
    /// Copia i dati LCC dall'apk alla cartella scrivibile del visore, al primo avvio.
    ///
    /// Perche' esiste: l'SDK LCC apre i file con IO nativo e dentro l'apk non c'e' un file
    /// system, c'e' un archivio compresso. I dati devono quindi finire su persistentDataPath —
    /// esattamente dove finivano quando li si spingeva a mano con SideQuest, ma senza dover
    /// preparare ogni visore e senza trasferimenti USB che si piantano a meta'. Qui la copia la
    /// fa l'applicazione, dall'interno, un file per volta.
    ///
    /// RIPRENDIBILE, ed e' il punto che conta: a ogni avvio si controlla file per file se la
    /// copia c'e' gia' ed e' della dimensione giusta. Se un'installazione si interrompe — batteria,
    /// visore tolto, app chiusa — al riavvio riparte da dove era rimasta invece di ricominciare
    /// da capo. Dal secondo avvio in poi il controllo dura una frazione di secondo e non copia
    /// nulla.
    ///
    /// Si scrive con DownloadHandlerFile, che scrive direttamente su disco: leggere un file da
    /// 60 MB in memoria per poi riscriverlo sarebbe un picco inutile su un dispositivo dove la
    /// memoria e' la risorsa scarsa.
    ///
    /// Da mettere nella scena BASE, non nel prefab VrApp: l'installazione va fatta una volta per
    /// avvio, non a ogni cambio scena.
    ///
    /// ATTENZIONE: finche' l'installazione e' in corso le aree non hanno ancora i propri dati.
    /// La riga di stato lo dice; se qualcuno entra comunque in un'area, LCCRendererVR fallira'
    /// con il suo errore "file NON trovato", che e' chiaro ma inutile da spiegare in aula.
    /// </summary>
    public class SplatInstaller : MonoBehaviour
    {
        [Tooltip("Cartella dei dati dentro StreamingAssets. Deve coincidere con quella usata da " +
                 "SplatManifestBuilder.")]
        [SerializeField] private string rootFolder = "LCC";

        [Tooltip("Nome del manifesto generato dal menu Artemis → Genera manifesto splat.")]
        [SerializeField] private string manifestName = "_files.txt";

        [Tooltip("Salta l'installazione in Editor: li' gli splat non si caricano comunque " +
                 "(skipInEditor su LCCRendererVR) e copiare 270 MB a ogni Play sarebbe un supplizio.")]
        [SerializeField] private bool skipInEditor = true;

        /// <summary>Vero mentre la copia e' in corso: chi vuole impedire il cambio scena puo'
        /// interrogarlo.</summary>
        public static bool Installing { get; private set; }
        public static float Progress01 { get; private set; }
        public static string Status { get; private set; } = "";

        private TMP_Text label;
        private float nextAttach;

        private void Start()
        {
#if UNITY_EDITOR
            if (skipInEditor)
            {
                Debug.Log("[SplatInstaller] Editor: installazione saltata.");
                enabled = false;
                return;
            }
#endif
            // Si installa SOLO se il progetto legge davvero dal disco. Con sorgente HttpUrl gli
            // splat arrivano dalla rete e copiare 270 MB sul visore sarebbe lavoro inutile —
            // per giunta invisibile, e quindi difficile da collegare alla propria causa quando ci
            // si chiede perche' il primo avvio impiega un minuto.
            var mode = SplatSourceConfig.Resolve(LCCRendererVR.SplatSource.PersistentData);
            if (mode != LCCRendererVR.SplatSource.PersistentData)
            {
                Debug.Log($"[SplatInstaller] sorgente di progetto = {mode}: nessuna installazione.");
                enabled = false;
                return;
            }

            StartCoroutine(InstallRoutine());
        }

        private void Update()
        {
            // L'etichetta si aggancia alla HUD appena c'e'; non si pretende il primo frame.
            if (label == null)
            {
                if (Time.unscaledTime < nextAttach) return;
                nextAttach = Time.unscaledTime + 0.5f;
                Attach();
                if (label == null) return;
            }

            if (Installing)
                label.text = $"{Status}   {Progress01:P0}";
            else if (label.text.Length > 0)
                label.text = "";
        }

        // ---- installazione ----------------------------------------------------------------------

        private IEnumerator InstallRoutine()
        {
            Installing = true;
            Progress01 = 0f;
            Status = "checking local data…";

            string manifestUrl = StreamingUrl($"{rootFolder}/{manifestName}");
            string manifestText = null;

            using (var req = UnityWebRequest.Get(manifestUrl))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail($"manifesto non leggibile ({req.error}) — hai eseguito " +
                         "Artemis → Genera manifesto splat prima della build?");
                    yield break;
                }
                manifestText = req.downloadHandler.text;
            }

            var entries = Parse(manifestText);
            if (entries.Count == 0) { Fail("manifesto vuoto."); yield break; }

            // Primo giro SENZA copiare: quanto manca davvero. Serve a non annunciare
            // un'installazione quando non c'e' niente da fare, cioe' a ogni avvio dopo il primo.
            var todo = new List<(string rel, long size)>();
            long todoBytes = 0;
            foreach (var e in entries)
            {
                string dst = Path.Combine(Application.persistentDataPath, e.rel);
                if (File.Exists(dst) && new FileInfo(dst).Length == e.size) continue;
                todo.Add(e);
                todoBytes += e.size;
            }

            if (todo.Count == 0)
            {
                Debug.Log($"[SplatInstaller] dati gia' installati ({entries.Count} file): niente da fare.");
                Done();
                yield break;
            }

            Debug.Log($"[SplatInstaller] da copiare {todo.Count} file su {entries.Count} " +
                      $"({todoBytes / (1024f * 1024f):F0} MB).");

            long copied = 0;
            int n = 0;
            foreach (var e in todo)
            {
                n++;
                Status = $"installing plot data… ({n}/{todo.Count})";

                string dst = Path.Combine(Application.persistentDataPath, e.rel);
                string dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Un file a meta' e' peggio di un file assente: si scrive su un nome temporaneo e
                // si rinomina solo a copia riuscita, cosi' il controllo per dimensione del prossimo
                // avvio non puo' scambiare un troncamento per una copia buona.
                string tmp = dst + ".part";
                if (File.Exists(tmp)) File.Delete(tmp);

                using (var req = UnityWebRequest.Get(StreamingUrl(e.rel)))
                {
                    req.downloadHandler = new DownloadHandlerFile(tmp);   // scrive su disco, non in RAM
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Fail($"copia fallita su '{e.rel}': {req.error}");
                        yield break;
                    }
                }

                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);

                copied += e.size;
                Progress01 = todoBytes > 0 ? Mathf.Clamp01((float)((double)copied / todoBytes)) : 1f;
            }

            Debug.Log($"[SplatInstaller] installazione completata: {todo.Count} file, " +
                      $"{copied / (1024f * 1024f):F0} MB in {Application.persistentDataPath}.");
            Done();
        }

        /// URL leggibile da UnityWebRequest sia dentro l'apk (jar:file://…) sia su disco.
        /// Path.Combine qui NON va bene: su Android streamingAssetsPath e' un URL, e i separatori
        /// di Windows lo romperebbero.
        private static string StreamingUrl(string relative) =>
            Application.streamingAssetsPath + "/" + relative;

        private static List<(string rel, long size)> Parse(string text)
        {
            var list = new List<(string, long)>();
            if (string.IsNullOrEmpty(text)) return list;

            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int bar = line.LastIndexOf('|');
                if (bar <= 0) continue;
                if (!long.TryParse(line.Substring(bar + 1), out long size)) continue;
                list.Add((line.Substring(0, bar), size));
            }
            return list;
        }

        private void Done()
        {
            Installing = false;
            Progress01 = 1f;
            Status = "";
        }

        private void Fail(string msg)
        {
            Installing = false;
            Status = "";
            Debug.LogError("[SplatInstaller] " + msg);
            if (label != null) label.text = "plot data not installed — see the log";
        }

        // ---- etichetta nella HUD ------------------------------------------------------------------

        /// Come per la sonda del frametime: l'etichetta si crea nella canvas della HUD senza
        /// toccare VrHud, cosi' questo componente si aggiunge e si toglie da solo.
        private void Attach()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;
            var canvasT = hud.transform.Find("VrHudCanvas");
            if (canvasT == null) return;

            var existing = canvasT.Find("SplatInstall");
            if (existing != null) { label = existing.GetComponent<TMP_Text>(); return; }

            var go = new GameObject("SplatInstall", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvasT, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(8f, 82f);      // sopra frametime e striscia diagnostica
            rt.offsetMax = new Vector2(-8f, 120f);

            label = go.GetComponent<TextMeshProUGUI>();
            label.fontSize = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            label.text = "";
        }
    }
}
