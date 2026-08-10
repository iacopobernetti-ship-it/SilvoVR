using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;
using Artemis.Inventory;

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
            if (flow.IsOnBase || flow.IsOnSimulation) { enabled = false; return; }

            var page = hud.CreateTab(tabTitle);
            infoLabel = hud.MakeLabel(page, "", 17);

            var row = hud.MakeRow(page);
            hud.MakeButton(row, "Open\nsimulation", () => AreaFlow.Instance?.GoToSimulation());
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
                ? "survey some trees first: the simulation is built from this plot's inventory"
                : $"{trees} trees surveyed here" + (hasMarking ? "  ·  a marking is saved" : "  ·  no marking yet");

            if (showLabel != null) showLabel.text = showing ? "Hide\nmarking" : "Show last\nmarking";
            if (showImg != null) showImg.color = showing ? hud.ActiveColor : hud.ButtonColor;

            statusLabel.text = v != null ? v.Status : "";
        }
    }
}
