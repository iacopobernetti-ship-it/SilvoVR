using TMPro;
using UnityEngine;

namespace Artemis.EditorTools
{
    /// <summary>
    /// SONDA TEMPORANEA — da rimuovere prima del pilot (lista del §6).
    ///
    /// Mostra il FRAMETIME in visore, appena sopra la striscia diagnostica della HUD. Serve a
    /// misurare quanto costa una modifica invece di discutere se "sembra piu' lento": si fa un
    /// giro con la modifica attiva, si legge il numero, si disattiva e si rifa' lo stesso giro.
    ///
    /// Perche' in HUD e non con un tool esterno: OVR Metrics Tool e adb sono un'altra catena da
    /// far funzionare proprio mentre si sta gia' inseguendo un problema, e in visore i log non
    /// si leggono. La diagnostica del progetto e' sempre stata dentro il pannello, ed e' li'
    /// che si guarda senza togliersi il visore.
    ///
    /// Cosa mostra e perche' TRE numeri e non uno:
    ///  - MEDIA: il costo di regime, quello che dice se si sta dentro il budget;
    ///  - PEGGIORE della finestra: la vera causa del "lag" percepito. Un frame da 40 ms ogni
    ///    tanto si sente eccome, ma sparisce dentro una media che resta a 12 — per questo la
    ///    media da sola non basta a dare torto o ragione a un'impressione;
    ///  - SFORI: quanti frame nella finestra hanno superato il budget. Uno ogni tanto e'
    ///    tollerabile, il 20 % e' un problema.
    ///
    /// Il riferimento e' il BUDGET del refresh: a 72 Hz un frame dura 13.9 ms, e tutto —
    /// applicazione, splat, occlusore, rete — deve starci dentro. Verde = dentro, rosso = fuori.
    ///
    /// Da mettere sull'oggetto App del prefab VrApp, cosi' e' presente in ogni scena. Si
    /// riaggancia da sola alla HUD a ogni cambio scena (la HUD si ricostruisce insieme al resto).
    /// </summary>
    public class FrameTimeProbe : MonoBehaviour
    {
        [Tooltip("Millisecondi disponibili per frame. 13.9 = 72 Hz, 11.1 = 90 Hz. E' la soglia " +
                 "che colora la riga e che conta gli sfori.")]
        [SerializeField] private float budgetMs = 13.9f;

        [Tooltip("Ampiezza della finestra di misura, in secondi. Mezzo secondo e' abbastanza " +
                 "reattivo da seguire una rotazione della testa e abbastanza lungo da non " +
                 "ballare a ogni frame.")]
        [SerializeField] private float windowSeconds = 0.5f;

        [Tooltip("Spegnendolo la riga sparisce senza togliere il componente: comodo per l'A/B, " +
                 "perche' anche disegnare questa etichetta costa qualcosa.")]
        [SerializeField] private bool show = true;

        private TMP_Text label;
        private float windowStart;
        private int frames;
        private float sumMs, worstMs;
        private int overBudget;
        private float nextAttach;

        private void Update()
        {
            // La HUD muore e rinasce a ogni scena: l'etichetta va riagganciata, con la solita
            // pazienza (nessuna pretesa sul primo frame) invece che una volta sola in Start.
            if (label == null)
            {
                if (Time.unscaledTime < nextAttach) return;
                nextAttach = Time.unscaledTime + 0.5f;
                Attach();
                if (label == null) return;
                ResetWindow();
            }

            if (!show)
            {
                if (label.text.Length > 0) label.text = "";
                return;
            }

            // Tempo NON scalato: Time.deltaTime seguirebbe un eventuale timeScale e mentirebbe
            // proprio quando si misura.
            float ms = Time.unscaledDeltaTime * 1000f;
            frames++;
            sumMs += ms;
            if (ms > worstMs) worstMs = ms;
            if (ms > budgetMs) overBudget++;

            if (Time.unscaledTime - windowStart < windowSeconds) return;

            float avg = frames > 0 ? sumMs / frames : 0f;
            float fps = avg > 0.01f ? 1000f / avg : 0f;
            int overPct = frames > 0 ? Mathf.RoundToInt(overBudget * 100f / frames) : 0;

            label.text = $"frame {avg:F1} ms ({fps:F0} fps)  ·  peggiore {worstMs:F1} ms  ·  " +
                         $"sfori {overPct}%  ·  budget {budgetMs:F1} ms";

            // Il colore guarda la MEDIA e gli SFORI insieme: una media buona con un quinto dei
            // frame fuori budget non e' una situazione sana, e va segnalata.
            bool healthy = avg <= budgetMs && overPct < 10;
            label.color = healthy ? new Color(0.6f, 1f, 0.6f, 0.9f)
                                  : new Color(1f, 0.75f, 0.35f, 0.95f);

            ResetWindow();
        }

        private void ResetWindow()
        {
            windowStart = Time.unscaledTime;
            frames = 0; sumMs = 0f; worstMs = 0f; overBudget = 0;
        }

        /// <summary>
        /// Crea l'etichetta come figlia della canvas della HUD, ancorata in basso appena SOPRA
        /// la striscia diagnostica (che occupa la fascia 4-42). Non si tocca VrHud: cosi' questa
        /// sonda si toglie cancellando un componente, senza rimettere mano a un file che nel
        /// frattempo potrebbe essere cambiato per altri motivi.
        ///
        /// L'always-on-top non serve applicarlo qui: VrHud lo ripassa periodicamente su TUTTI i
        /// grafici figli della canvas, quindi anche su questo.
        /// </summary>
        private void Attach()
        {
            var hud = Artemis.Vr.VrHud.Instance;
            if (hud == null) return;

            var canvasT = hud.transform.Find("VrHudCanvas");
            if (canvasT == null) return;

            var existing = canvasT.Find("FrameTime");
            if (existing != null) { label = existing.GetComponent<TMP_Text>(); return; }

            var go = new GameObject("FrameTime", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvasT, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(8f, 44f);     // subito sopra la striscia diagnostica
            rt.offsetMax = new Vector2(-8f, 78f);

            label = go.GetComponent<TextMeshProUGUI>();
            label.fontSize = 15;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = "";
        }
    }
}
