using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;

namespace Artemis.Inventory
{
    /// <summary>
    /// Scheda "Inventario": la tabella degli alberi rilevati, erede della lista che c'era nella
    /// versione non immersiva. Una riga per albero (numero, diametro, altezza, marcatura) dentro
    /// una finestra ritagliata, con uno SLIDER per scorrere — cosi' la lunghezza dell'elenco non
    /// e' un limite e il pannello non cresce a dismisura.
    ///
    /// Perche' uno slider e non il trascinamento: in visore trascinare un contenuto con il ray
    /// e' impreciso e stancante, mentre un cursore si aggancia una volta e si muove col polso.
    ///
    /// Toccando una riga si marca/smarca l'albero: e' la stessa operazione del modo "Marca" ma
    /// dall'elenco, comoda quando l'albero e' lontano o nascosto dietro altri.
    /// </summary>
    public class InventoryTablePanel : MonoBehaviour
    {
        [Header("Tabella")]
        [SerializeField] private string tabTitle = "Inventory";
        [Tooltip("Altezza DESIDERATA della finestra di scorrimento (px). Se la pagina non basta " +
                 "la finestra si comprime fino al minimo qui sotto, cosi' lo slider resta sempre " +
                 "dentro il pannello.")]
        [SerializeField] private float viewportHeight = 200f;
        [Tooltip("Altezza minima della finestra: sotto questa non si comprime.")]
        [SerializeField] private float minViewportHeight = 90f;
        [Tooltip("Altezza di ogni riga (px). Piu' alta = piu' facile da toccare col ray.")]
        [SerializeField] private float rowHeight = 46f;
        [Tooltip("Corpo del testo delle righe. Si auto-riduce se non ci sta.")]
        [SerializeField] private float rowFontSize = 20f;
        [SerializeField] private Color markedRowColor = new Color(0.55f, 0.32f, 0.10f, 1f);

        [Header("Slider di scorrimento (VERTICALE, a destra della tabella)")]
        [Tooltip("Larghezza della barra (px). 44 = 4.4 cm: allargala se fatichi ad agganciarla col ray.")]
        [SerializeField] private float sliderWidth = 44f;
        [Tooltip("Altezza della maniglia (px). Una maniglia corta e' difficile da prendere in " +
                 "visore: meglio abbondare.")]
        [SerializeField] private float handleHeight = 64f;
        [Tooltip("Margine sopra e sotto entro cui la maniglia scorre (px).")]
        [SerializeField] private float sliderEndMargin = 14f;
        [Tooltip("Opacita' della maniglia quando non c'e' nulla da scorrere.")]
        [Range(0f, 1f)]
        [SerializeField] private float handleIdleAlpha = 0.6f;
        [Tooltip("Sfondo della barra.")]
        [SerializeField] private Color sliderTrackColor = new Color(0.16f, 0.17f, 0.20f, 1f);
        [Tooltip("Colore della maniglia. Deve STACCARE dalla barra: maniglia e barra dello stesso " +
                 "grigio sono la ragione per cui sembrava non esserci.")]
        [SerializeField] private Color handleColor = new Color(0.88f, 0.90f, 0.94f, 1f);
        [Tooltip("Sfondo della finestra della tabella.")]
        [SerializeField] private Color viewportColor = new Color(0f, 0f, 0f, 0.25f);

        private bool built;
        private float nextRefresh;
        private StemInventory bound;
        private int lastSignature = -1;

        private TMP_Text header;
        private RectTransform content;
        private RectTransform viewport;
        private Slider scrollbar;
        private readonly List<GameObject> rows = new List<GameObject>();

        // ---- ciclo di vita ---------------------------------------------------------------------

        private void Update()
        {
            if (!built) { TryBuild(); return; }

            var inv = StemInventory.Instance;
            if (inv != bound)
            {
                if (bound != null) bound.OnInventoryChanged -= MarkDirty;
                bound = inv;
                if (bound != null) bound.OnInventoryChanged += MarkDirty;
                MarkDirty();
            }

            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.3f;
            RefreshIfNeeded();

            // Le altezze del layout possono non essere ancora calcolate al momento della
            // ricostruzione: rivalutare lo scorrimento a ogni tick evita che lo slider resti
            // spento per sempre a causa di una misura letta troppo presto.
            ApplyScroll();
        }

        private void OnDestroy()
        {
            if (bound != null) bound.OnInventoryChanged -= MarkDirty;
        }

        private void MarkDirty() => lastSignature = -1;

        // ---- costruzione ------------------------------------------------------------------------

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;

            var page = hud.CreateTab(tabTitle);
            header = hud.MakeLabel(page, "", 18);

            // --- riga: tabella a sinistra, barra di scorrimento a destra ----------------------
            var rowGo = new GameObject("TableRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(page, false);
            var rowHlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            rowHlg.spacing = 8f;
            rowHlg.childControlWidth = true;  rowHlg.childControlHeight = true;
            rowHlg.childForceExpandWidth = false; rowHlg.childForceExpandHeight = true;

            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.minHeight = minViewportHeight;
            rowLe.preferredHeight = viewportHeight;
            rowLe.flexibleWidth = 1; rowLe.flexibleHeight = 1;

            // --- finestra ritagliata ---------------------------------------------------------
            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(rowGo.transform, false);
            viewport = vpGo.GetComponent<RectTransform>();
            var vpImg = vpGo.GetComponent<Image>();
            vpImg.color = viewportColor;
            // La finestra prende tutta la larghezza CHE AVANZA nella riga; la barra a destra
            // ha invece una larghezza fissa e non puo' essere compressa. E' l'altezza della
            // RIGA a essere elastica, cosi' il blocco tabella+barra sta sempre nella pagina.
            var vpLe = vpGo.AddComponent<LayoutElement>();
            vpLe.flexibleWidth = 1; vpLe.flexibleHeight = 1;

            // --- contenuto scorrevole, ancorato in alto ---------------------------------------
            var cGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            cGo.transform.SetParent(vpGo.transform, false);
            content = cGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot     = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(4f, 0f);
            content.offsetMax = new Vector2(-4f, 0f);
            content.anchoredPosition = Vector2.zero;

            var vlg = cGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f; vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var fit = cGo.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // --- barra di scorrimento, accanto alla finestra ------------------------------------
            BuildSlider(rowGo.transform, hud);

            built = true;
            MarkDirty();
        }

        /// Barra di scorrimento VERTICALE, a destra della tabella: e' la direzione in cui la
        /// lista scorre davvero, e la posizione dove l'occhio la cerca.
        ///
        /// Costruita a mano perche' servono solo binario e maniglia, ed entrambi devono essere
        /// abbondanti: in visore una maniglia sottile e' impossibile da agganciare col ray.
        private void BuildSlider(Transform parent, VrHud hud)
        {
            var sGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Slider));
            sGo.transform.SetParent(parent, false);
            sGo.GetComponent<Image>().color = sliderTrackColor;

            // Larghezza fissa e incomprimibile; l'altezza la prende dalla riga.
            var sLe = sGo.AddComponent<LayoutElement>();
            sLe.minWidth = sliderWidth; sLe.preferredWidth = sliderWidth;
            sLe.flexibleWidth = 0; sLe.flexibleHeight = 1;

            var area = new GameObject("Handle Slide Area", typeof(RectTransform));
            area.transform.SetParent(sGo.transform, false);
            var areaRt = area.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(0f, sliderEndMargin);
            areaRt.offsetMax = new Vector2(0f, -sliderEndMargin);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            handle.GetComponent<Image>().color = handleColor;
            var hRt = handle.GetComponent<RectTransform>();
            // Barra VERTICALE: lo Slider gestisce da se' gli ancoraggi lungo Y, quindi qui si
            // dichiara solo lo spessore lungo l'altra dimensione — sizeDelta.x = 0 significa
            // "tutta la larghezza disponibile", sizeDelta.y = altezza della maniglia.
            hRt.anchorMin = new Vector2(0f, 0f);
            hRt.anchorMax = new Vector2(1f, 0f);
            hRt.pivot     = new Vector2(0.5f, 0.5f);
            hRt.sizeDelta = new Vector2(0f, handleHeight);

            scrollbar = sGo.GetComponent<Slider>();
            // TopToBottom: valore 0 = in cima alla lista, che e' dove si comincia a leggere.
            scrollbar.direction = Slider.Direction.TopToBottom;
            scrollbar.minValue = 0f; scrollbar.maxValue = 1f; scrollbar.value = 0f;
            scrollbar.handleRect = hRt;
            scrollbar.targetGraphic = handle.GetComponent<Image>();

            // Transizione DISATTIVATA: uno Slider e' un Selectable e con la transizione a
            // colore sovrascrive il colore della targetGraphic con quello di stato (normal,
            // highlighted, e soprattutto DISABLED) — era cio' che rendeva la maniglia un
            // grigio spento indistinguibile dal binario.
            scrollbar.transition = Selectable.Transition.None;

            scrollbar.onValueChanged.AddListener(_ => ApplyScroll());
        }

        // ---- contenuto ---------------------------------------------------------------------------

        /// Firma a buon mercato: cambia quando cambia il numero di alberi o una marcatura, cosi'
        /// la tabella non si ricostruisce a ogni frame ma solo quando c'e' davvero da farlo.
        private int Signature(StemInventory inv)
        {
            int h = inv.Count * 397;
            foreach (var s in inv.Stems) h = h * 31 + s.StemId + (s.Marked ? 1 : 0) * 7919;
            return h;
        }

        private void RefreshIfNeeded()
        {
            var inv = StemInventory.Instance;
            if (inv == null)
            {
                if (header != null) header.text = "inventory unavailable";
                return;
            }

            int sig = Signature(inv);
            if (sig == lastSignature) return;
            lastSignature = sig;
            Rebuild(inv);
        }

        private void Rebuild(StemInventory inv)
        {
            foreach (var go in rows) if (go != null) Destroy(go);
            rows.Clear();

            header.text = inv.Count == 0
                ? "no trees surveyed yet"
                : $"{inv.Count} trees  ·  dg {inv.QuadraticMeanDbh() * 100f:F1} cm";

            var hud = VrHud.Instance;
            foreach (var rec in inv.Stems)
            {
                int id = rec.StemId;                     // copia locale per la closure
                var go = new GameObject($"Row_{id}", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(content, false);

                var img = go.GetComponent<Image>();
                img.color = rec.Marked ? markedRowColor : hud.ButtonColor;

                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = rowHeight; le.flexibleWidth = 1; le.flexibleHeight = 0;

                var tGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                tGo.transform.SetParent(go.transform, false);
                var t = tGo.GetComponent<TextMeshProUGUI>();
                t.text = $"#{id,-3}  d {rec.Dbh * 100f,5:F1} cm   h {rec.Height,4:F1} m" +
                         (rec.Marked ? "   ◆" : "");
                t.fontSize = rowFontSize; t.alignment = TextAlignmentOptions.Left; t.color = Color.white;
                t.enableAutoSizing = true;
                t.fontSizeMax = rowFontSize;
                t.fontSizeMin = Mathf.Max(9f, rowFontSize * 0.6f);
                t.overflowMode = TextOverflowModes.Truncate;
                t.raycastTarget = false;
                var tr = t.rectTransform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(12f, 2f); tr.offsetMax = new Vector2(-12f, -2f);

                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => StemInventory.Instance?.ToggleMark(id));

                rows.Add(go);
            }

            // Il layout si assesta a fine frame: forzarlo ora evita che lo slider calcoli lo
            // scorrimento su un'altezza ancora vecchia.
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            ApplyScroll();
        }

        private void ApplyScroll()
        {
            if (content == null || viewport == null) return;

            float overflow = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            float v = scrollbar != null ? scrollbar.value : 0f;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, overflow * v);

            // Quando tutto ci sta lo slider resta VISIBILE ma inerte, invece di sparire: un
            // comando che appare e scompare confonde piu' di uno spento, e nasconderlo aveva
            // gia' prodotto il sospetto che non esistesse affatto.
            // Lo slider resta SEMPRE interagibile: scorrere una lista che ci sta tutta non fa
            // alcun danno, mentre disabilitarlo faceva scattare il colore "disabled" del
            // Selectable e lo rendeva invisibile. Quando non serve, si limita a sbiadire.
            if (scrollbar != null && scrollbar.handleRect != null)
            {
                bool needed = overflow > 1f;
                var img = scrollbar.handleRect.GetComponent<Image>();
                if (img != null)
                {
                    Color c = handleColor;
                    if (!needed) c.a = handleIdleAlpha;
                    if (img.color != c) img.color = c;
                }
            }
        }
    }
}
