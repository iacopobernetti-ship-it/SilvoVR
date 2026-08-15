using System.Text;
using Unity.Netcode;
using UnityEngine;
using TMPro;
using Artemis.Vr;

namespace Artemis.Session
{
    /// <summary>
    /// Scheda "Avatars": diagnostica TEMPORANEA per capire perche' un avatar non si veda.
    ///
    /// Esiste perche' il sintomo "non lo vedo" ha almeno cinque cause che da dentro il visore
    /// sono indistinguibili — l'oggetto non esiste, esiste ma e' altrove, e' vicino ma senza
    /// mesh, ha i renderer spenti, oppure sta su un layer che la camera non disegna. Elencarle
    /// tutte in chiaro costa una scheda e chiude la questione in una prova sola, invece di
    /// un'altra tornata di ipotesi.
    ///
    /// Per ogni avatar in scena riporta: proprietario, se e' il proprio, distanza e direzione
    /// rispetto alla testa, quota, layer, quanti renderer ha e quanti ne sono accesi, e se il
    /// layer e' visibile alla camera principale.
    ///
    /// Da mettere sull'oggetto App del prefab VrApp. Da RIMUOVERE quando il caso e' chiuso.
    /// </summary>
    public class AvatarDebugPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Avatars";
        [SerializeField] private float refreshSeconds = 0.5f;

        private bool built;
        private float nextRefresh;
        private TMP_Text report;

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshSeconds;
            Refresh();
        }

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;

            var page = hud.CreateTab(tabTitle);
            report = hud.MakeLabel(page, "", 13, TextAlignmentOptions.TopLeft);
            var le = report.GetComponent<UnityEngine.UI.LayoutElement>();
            if (le != null) { le.preferredHeight = 320; le.flexibleHeight = 1; }
            report.enableWordWrapping = true;

            built = true;
        }

        private void Refresh()
        {
            var sb = new StringBuilder();
            var cam = Camera.main;
            Vector3 me = cam != null ? cam.transform.position : Vector3.zero;

            var nm = NetworkManager.Singleton;
            sb.AppendLine(nm != null && nm.IsListening
                ? $"net: {(nm.IsServer ? "host/teacher" : "client/student")} · id {nm.LocalClientId} · " +
                  $"clients {nm.ConnectedClientsIds.Count}"
                : "net: offline");
            sb.AppendLine($"cam: {(cam == null ? "NULL" : cam.name)}  pos {me}");
            sb.AppendLine();

            var avatars = FindObjectsByType<VrPlayerAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (avatars.Length == 0) { sb.AppendLine("NESSUN avatar in scena."); report.text = sb.ToString(); return; }

            foreach (var a in avatars)
            {
                if (a == null) continue;
                var t = a.transform;

                int total = 0, on = 0;
                foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                { total++; if (r.enabled && r.gameObject.activeInHierarchy) on++; }

                // La posizione che conta e' quella della TESTA: la radice puo' restare ferma.
                var headT = t.Find("Head");
                Vector3 p = headT != null ? headT.position : t.position;

                int layer = headT != null ? headT.gameObject.layer : t.gameObject.layer;
                bool visibleToCam = cam != null && (cam.cullingMask & (1 << layer)) != 0;

                Vector3 d = p - me;
                float dist = d.magnitude;
                string dir = dist < 0.01f ? "SOVRAPPOSTO"
                           : $"{(d.y > 0.5f ? "sopra " : d.y < -0.5f ? "sotto " : "")}{dist:F1} m";

                sb.AppendLine($"owner {a.OwnerClientId}{(a.IsOwner ? " (io)" : "")} · {dir}");
                sb.AppendLine($"   pos {p}  ·  layer {LayerMask.LayerToName(layer)}" +
                              $"{(visibleToCam ? "" : "  ← NON VISIBILE ALLA CAMERA")}");
                sb.AppendLine($"   renderer accesi {on}/{total}" +
                              $"{(total == 0 ? "  ← NESSUNA MESH" : on == 0 ? "  ← TUTTI SPENTI" : "")}");
            }

            report.text = sb.ToString();
        }
    }
}
