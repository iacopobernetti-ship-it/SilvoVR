using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Artemis.Session
{
    /// <summary>
    /// Avatar di rete. Lo script fa DUE cose sole: copia le pose del rig locale sui tre
    /// transform replicati, e sceglie quale materiale colorato indossare.
    ///
    /// NON costruisce geometria e NON crea materiali, e questa e' la differenza sostanziale
    /// rispetto alla versione precedente. Costruire a runtime accumulava tre fragilita'
    /// indipendenti, ognuna capace da sola di rendere l'avatar invisibile: mesh generate che
    /// potevano mancare, materiali presi dal default dei primitive, e soprattutto Shader.Find —
    /// che in Editor trova sempre tutto, mentre in una build Android gli shader non referenziati
    /// da alcun asset VENGONO RIMOSSI e la ricerca torna null. Da cui il classico "in Editor si
    /// vede, sul visore no", impossibile da diagnosticare dall'interno.
    ///
    /// Con mesh e materiali creati in editor il problema non esiste: sono asset, entrano nella
    /// build per costruzione, e quello che vedi nella Scene view e' esattamente quello che vedrai
    /// nel visore. Si verifica senza compilare.
    ///
    /// Separazione invariata: il rig XR e' locale e si ricostruisce a ogni scena, l'avatar e' un
    /// NetworkObject che sopravvive. Il proprietario copia le pose, gli altri le ricevono.
    /// </summary>
    public class VrPlayerAvatar : NetworkBehaviour
    {
        [Header("Parti replicate (con NetworkTransform, autorita' Owner)")]
        [SerializeField] private Transform head;
        [SerializeField] private Transform handLeft;
        [SerializeField] private Transform handRight;

        [Header("Busto (dedotto dalla testa, niente NetworkTransform)")]
        [Tooltip("Il busto non e' tracciato: viene appeso sotto la testa, che e' gia' replicata. " +
                 "Un NetworkTransform in meno per partecipante.")]
        [SerializeField] private Transform body;
        [Tooltip("Distanza fra il centro della testa e il centro del busto (m).")]
        [SerializeField] private float neckToTorsoCentre = 0.42f;
        [Tooltip("Velocita' con cui il busto ruota verso la direzione dello sguardo. Bassa = le " +
                 "spalle seguono la testa, come in un corpo vero.")]
        [SerializeField] private float bodyTurnSpeed = 6f;
        [Tooltip("Altezza degli occhi usata finche' la testa non e' tracciata, per non far " +
                 "nascere la figura sprofondata nel terreno.")]
        [SerializeField] private float fallbackEyeHeight = 1.65f;

        [Header("Colori: materiali REALI, uno per partecipante")]
        [Tooltip("Un materiale per colore, creati in editor. L'indice arriva dalla rete e sceglie " +
                 "quale indossare: nessun materiale istanziato, nessuno shader cercato a runtime.")]
        [SerializeField] private Material[] palette = new Material[10];
        [Tooltip("Renderer da colorare (busto, testa, mani).")]
        [SerializeField] private Renderer[] tinted;
        [Tooltip("Renderer da nascondere al PROPRIETARIO: busto e testa, che altrimenti si " +
                 "troverebbe davanti agli occhi. Le mani restano visibili — in visore sono " +
                 "l'unico riferimento del proprio corpo.")]
        [SerializeField] private Renderer[] hiddenForOwner;

        [Header("Nomi nel rig locale")]
        [SerializeField] private string leftControllerName = "Left Controller";
        [SerializeField] private string rightControllerName = "Right Controller";

        /// <summary>Indice colore, assegnato dal server: uguale su tutti gli schermi.</summary>
        public readonly NetworkVariable<int> ColorIndex = new NetworkVariable<int>(0);

        private Transform srcHead, srcLeft, srcRight;
        private float nextRigSearch;

        // ---- ciclo di vita ---------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            if (IsServer) ColorIndex.Value = OwnerIndex();
            ColorIndex.OnValueChanged += (_, __) => ApplyColour();
            ApplyColour();

            if (IsOwner && hiddenForOwner != null)
                foreach (var r in hiddenForOwner) if (r != null) r.enabled = false;

            Debug.Log($"[VrPlayerAvatar] spawn · owner {OwnerClientId} · mio {IsOwner} · " +
                      $"colore {ColorIndex.Value} · parti {(head != null)}/{(handLeft != null)}/" +
                      $"{(handRight != null)}/{(body != null)}");
        }

        private void Update()
        {
            if (IsOwner)
            {
                if ((srcHead == null || srcLeft == null || srcRight == null) && Time.time >= nextRigSearch)
                {
                    nextRigSearch = Time.time + 0.5f;
                    FindLocalRig();
                }
                Mirror(srcHead, head);
                Mirror(srcLeft, handLeft);
                Mirror(srcRight, handRight);
            }

            PlaceBody();   // su tutti: si deduce dalla testa, che e' gia' replicata
        }

        // ---- pose -----------------------------------------------------------------------------

        private static void Mirror(Transform src, Transform dst)
        {
            if (src == null || dst == null) return;
            dst.SetPositionAndRotation(src.position, src.rotation);
        }

        /// Il busto pende sotto la testa e ne segue l'IMBARDATA soltanto: un corpo non si inclina
        /// perche' si abbassa lo sguardo.
        private void PlaceBody()
        {
            if (body == null || head == null) return;

            Vector3 headPos = head.position;
            if (head.localPosition.sqrMagnitude < 0.0001f)      // testa non ancora tracciata
                headPos = transform.position + Vector3.up * fallbackEyeHeight;

            body.position = headPos + Vector3.down * neckToTorsoCentre;

            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.001f) return;
            body.rotation = Quaternion.Slerp(body.rotation,
                                             Quaternion.LookRotation(fwd.normalized, Vector3.up),
                                             Time.deltaTime * bodyTurnSpeed);
        }

        private void FindLocalRig()
        {
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null) return;

            if (srcHead == null)
                srcHead = origin.Camera != null ? origin.Camera.transform
                        : (Camera.main != null ? Camera.main.transform : null);

            if (srcLeft == null || srcRight == null)
                foreach (var t in origin.GetComponentsInChildren<Transform>(true))
                {
                    if (srcLeft == null && t.name == leftControllerName) srcLeft = t;
                    else if (srcRight == null && t.name == rightControllerName) srcRight = t;
                }
        }

        // ---- colore -------------------------------------------------------------------------------

        private int OwnerIndex() =>
            palette != null && palette.Length > 0 ? (int)(OwnerClientId % (ulong)palette.Length) : 0;

        /// Si SCEGLIE un materiale gia' esistente, non se ne crea uno: sharedMaterial e non
        /// material, cosi' non si istanzia nulla e dieci avatar dello stesso colore condividono
        /// lo stesso asset.
        private void ApplyColour()
        {
            if (palette == null || palette.Length == 0 || tinted == null) return;

            var m = palette[Mathf.Abs(ColorIndex.Value) % palette.Length];
            if (m == null)
            {
                Debug.LogWarning($"[VrPlayerAvatar] materiale {ColorIndex.Value} non assegnato " +
                                 "nella palette del prefab: l'avatar restera' del colore di base.");
                return;
            }

            foreach (var r in tinted) if (r != null) r.sharedMaterial = m;
        }

        /// <summary>Colore di un partecipante, per chi deve restare coerente con l'avatar
        /// (bandierine di proposta, elenchi nella HUD).</summary>
        public Color ColourOf(int index)
        {
            if (palette == null || palette.Length == 0) return Color.white;
            var m = palette[Mathf.Abs(index) % palette.Length];
            return m != null ? m.color : Color.white;
        }
    }

    /// <summary>
    /// Indici e colori dei partecipanti. I COLORI restano qui per chi non ha un materiale sotto
    /// mano (le bandierine di proposta, che sono generate a runtime), ma la fonte visiva degli
    /// avatar sono ora i materiali del prefab: se li cambi, allinea questa tavolozza.
    /// </summary>
    public static class PlayerPalette
    {
        public static readonly Color[] Colors =
        {
            new Color(0.90f, 0.20f, 0.20f), new Color(0.20f, 0.80f, 0.30f),
            new Color(0.25f, 0.50f, 1.00f), new Color(0.95f, 0.80f, 0.20f),
            new Color(0.85f, 0.40f, 0.90f), new Color(0.20f, 0.85f, 0.85f),
            new Color(1.00f, 0.55f, 0.10f), new Color(0.60f, 0.75f, 0.30f),
            new Color(0.95f, 0.45f, 0.60f), new Color(0.55f, 0.55f, 0.95f)
        };

        public static int IndexFor(ulong clientId) => (int)(clientId % (ulong)Colors.Length);
        public static Color Color(int index) => Colors[Mathf.Abs(index) % Colors.Length];
        public static Color ColorFor(ulong clientId) => Color(IndexFor(clientId));
    }
}
