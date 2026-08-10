using Unity.XR.CoreUtils;
using UnityEngine;

namespace Artemis.Session
{
    /// <summary>
    /// Scene-local, uno per scena-area, sull'oggetto SpawnPoint: porta il rig XR PERSISTENTE
    /// (vive nella scena Base, sopravvive ai cambi) sullo spawn di QUESTA area.
    ///
    /// E' il discendente magro di SpawnPointBinder: niente CharacterController da congelare,
    /// niente attesa del terreno — il collider dell'area e' un asset di scena, esiste dal
    /// primo frame. Restano due lezioni desktop che valgono ancora:
    ///  - il rig puo' apparire DOPO Start() (persistente, ordine di caricamento non garantito):
    ///    retry paziente, mai pretendere il primo frame;
    ///  - "suolo" = il punto PIU' BASSO lungo la verticale (RaycastAll dal basso verso l'alto):
    ///    se lo spawn finisse sotto una chioma con collider, il primo hit sarebbe la cima
    ///    degli alberi e il player partirebbe appollaiato lassu'.
    /// </summary>
    public class XrRigPlacer : MonoBehaviour
    {
        [Tooltip("Punto di spawn. Vuoto = il transform di questo oggetto.")]
        [SerializeField] private Transform spawnPoint;

        [Header("Aggancio al suolo")]
        [SerializeField] private bool snapToGround = true;
        [Tooltip("Layer del collider terreno di questa scena.")]
        [SerializeField] private LayerMask groundLayer = ~0;
        [Tooltip("Semilunghezza della sonda verticale: deve abbracciare l'area dal punto piu' " +
                 "basso al piu' alto.")]
        [SerializeField] private float probeHeight = 100f;
        [SerializeField] private float groundOffset = 0.05f;

        [Header("Congelamento")]
        [Tooltip("Blocca il movimento del giocatore finche' non e' stato posato sul suolo. " +
                 "Serve arrivando da un'altra scena: il rig si ricostruisce insieme al resto e " +
                 "c'e' una finestra di qualche frame in cui il giocatore esiste ma il collider " +
                 "del terreno non e' ancora registrato nella fisica — e in quella finestra la " +
                 "gravita' lavora indisturbata. Partendo dalla scena il problema non si vede, " +
                 "perche' tutto e' gia' pronto al primo frame.")]
        [SerializeField] private bool freezeUntilPlaced = true;
        [Tooltip("Valvola di sicurezza: sblocca comunque dopo questo tempo, cosi' un suolo che " +
                 "non arriva mai non puo' intrappolare nessuno.")]
        [SerializeField] private float maxFreezeSeconds = 8f;

        [Header("Ritentativi")]
        [Tooltip("Per quanto insistere a cercare il rig (in build puo' arrivare tardi).")]
        [SerializeField] private float giveUpAfterSeconds = 10f;
        [SerializeField] private float retryInterval = 0.25f;

        private bool placed;
        private float started, nextTry;
        private CharacterController frozen;

        private void Start()
        {
            started = Time.time;
            Freeze();              // prima del primo passo di fisica: nessuna caduta visibile
        }

        /// Il CharacterController e' cio' che applica la gravita': disattivarlo congela il
        /// giocatore dov'e', invece di lasciarlo precipitare mentre il terreno si registra.
        private void Freeze()
        {
            if (!freezeUntilPlaced || frozen != null) return;
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null) return;
            var cc = origin.GetComponentInChildren<CharacterController>();
            if (cc == null || !cc.enabled) return;
            cc.enabled = false;
            frozen = cc;
        }

        private void Unfreeze()
        {
            if (frozen != null) { frozen.enabled = true; frozen = null; }
        }

        private void Update()
        {
            if (placed || Time.time < nextTry) return;
            nextTry = Time.time + retryInterval;

            Freeze();              // il rig puo' comparire dopo Start

            if (Time.time - started > giveUpAfterSeconds)
            {
                Debug.LogWarning("[XrRigPlacer] nessun XROrigin trovato entro il timeout — rinuncio.");
                placed = true;
                Unfreeze();
                return;
            }

            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null) return;

            var spawn = spawnPoint != null ? spawnPoint : transform;
            Vector3 pos = spawn.position;

            if (snapToGround)
            {
                if (TrySampleGround(pos.x, pos.z, out float y)) pos.y = y + groundOffset;
                else if (Time.time - started < maxFreezeSeconds)
                {
                    // Il collider puo' non essere ancora registrato nella fisica: si riprova al
                    // tick successivo tenendo il giocatore congelato, invece di posarlo su una
                    // quota inventata e lasciarlo cadere.
                    return;
                }
                else
                {
                    Debug.LogWarning("[XrRigPlacer] nessun suolo sotto lo spawn entro il tempo " +
                                     "massimo: uso la Y dello spawn cosi' com'e' — controlla " +
                                     "groundLayer, la posizione e che il collider esista.");
                }
            }

            // Si muove la RADICE del rig: il giocatore restera' fisicamente dov'e' nel suo
            // spazio di gioco (offset camera dentro il play space), che su un'area di 400 m²
            // e' irrilevante. Se un giorno servisse precisione al centimetro, il punto di
            // raffinamento e' XROrigin.MoveCameraToWorldLocation + MatchOriginUpCameraForward.
            origin.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));

            placed = true;
            Unfreeze();
            Debug.Log($"[XrRigPlacer] rig XR posato a {pos} in '{gameObject.scene.name}'.");
        }

        /// Suolo = superficie PIU' BASSA lungo la verticale (lezione canopy del desktop),
        /// ma sondata in ENTRAMBE le direzioni. Il motivo: i raycast di Unity non colpiscono
        /// le backface dei MeshCollider, quindi un raggio che SALE vede solo le facce rivolte
        /// in giu' — sui terreni GS specchiati (scala -1, winding misto) funzionava, su un
        /// piano ordinario con le normali in su la sonda era cieca. Salendo E scendendo si
        /// coprono entrambi i winding, e il punto piu' basso resta il suolo vero anche sotto
        /// una chioma con collider.
        private bool TrySampleGround(float x, float z, out float y)
        {
            y = 0f;
            var up   = Physics.RaycastAll(new Vector3(x, -probeHeight, z), Vector3.up,
                                          probeHeight * 2f, groundLayer);
            var down = Physics.RaycastAll(new Vector3(x,  probeHeight, z), Vector3.down,
                                          probeHeight * 2f, groundLayer);
            float lowest = float.MaxValue;
            for (int i = 0; i < up.Length;   i++) if (up[i].point.y   < lowest) lowest = up[i].point.y;
            for (int i = 0; i < down.Length; i++) if (down[i].point.y < lowest) lowest = down[i].point.y;
            if (lowest == float.MaxValue) return false;
            y = lowest;
            return true;
        }
    }
}
