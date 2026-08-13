using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;
using Artemis.Inventory;
using Artemis.Session;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Scheda "Marking" nelle scene AREA: da qui si entra nella simulazione e si richiama
    /// l'ultima martellata salvata per quest'area.
    ///
    /// Non compare nella scena Simulation (dove c'e' gia' la scheda Felling) ne' nella Base,
    /// che non ha un inventario da simulare.
    /// </summary>
    public class SimulationAccessPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Marking";

        private bool built;
        private float nextRefresh;

        private Image showImg;
        private Button openBtn;
        private TMP_Text showLabel, infoLabel, statusLabel;

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.3f;
            Refresh();
        }

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            var flow = AreaFlow.Instance;
            if (hud == null || flow == null) return;

            // Solo nelle aree: la Base non ha inventario, la Simulation ha la sua scheda.
            if (!VrSession.WorkAllowed) return;
            if (flow.IsOnBase || flow.IsOnSimulation) { enabled = false; return; }

            var page = hud.CreateTab(tabTitle);
            infoLabel = hud.MakeLabel(page, "", 17);

            var row = hud.MakeRow(page);
            // Aprire la simulazione porta dentro l'intera classe: decide il docente.
            var (oBtn, oImg) = hud.MakeButton(row, "Open\nsimulation", () =>
            {
                if (VrSession.CanCommand) AreaFlow.Instance?.GoToSimulation();
            });
            openBtn = oBtn;
            var (sBtn, sImg) = hud.MakeButton(row, "Show last\nmarking", OnShowClicked);
            showImg = sImg;
            showLabel = sBtn.GetComponentInChildren<TMP_Text>();

            statusLabel = hud.MakeLabel(page, "", 15);

            built = true;
            Refresh();
        }

        private void OnShowClicked()
        {
            var v = MartellataViewerVR.Instance;
            var inv = StemInventory.Instance;
            if (v == null || inv == null) return;
            v.Toggle(inv.PlotId);
        }

        private void Refresh()
        {
            var hud = VrHud.Instance;
            var inv = StemInventory.Instance;
            if (hud == null || !built) return;

            string plot = inv != null ? inv.PlotId : "";
            int trees = inv != null ? inv.Count : 0;

            bool hasMarking = !string.IsNullOrEmpty(plot) && MartellataStore.Exists(plot);
            var v = MartellataViewerVR.Instance;
            bool showing = v != null && v.IsShowing;

            infoLabel.text = trees == 0
                ? (VrSession.IsStudent
                    ? "waiting for the teacher — the stand comes from their inventory"
                    : "SURVEY SOME TREES FIRST: the simulation is built from this plot's inventory")
                : $"{trees} trees surveyed here" + (hasMarking ? "  ·  a marking is saved" : "  ·  no marking yet");

            // Nascosto, non grigio: aprire la simulazione e' una decisione del docente.
            // In piu' resta INERTE finche' non c'e' un inventario: la simulazione si costruisce
            // dai rilievi del docente, e aprirla a mani vuote porta la classe davanti a un prato
            // senza che nulla spieghi perche'. Meglio fermarsi qui, dove la causa e' evidente.
            bool canOpen = VrSession.CanCommand && trees > 0;
            if (openBtn != null)
            {
                if (openBtn.gameObject.activeSelf != VrSession.CanCommand)
                    openBtn.gameObject.SetActive(VrSession.CanCommand);
                openBtn.interactable = canOpen;
            }
            if (showLabel != null) showLabel.text = showing ? "Hide\nmarking" : "Show last\nmarking";
            if (showImg != null) showImg.color = showing ? hud.ActiveColor : hud.ButtonColor;

            statusLabel.text = v != null ? v.Status : "";
        }
    }
}
