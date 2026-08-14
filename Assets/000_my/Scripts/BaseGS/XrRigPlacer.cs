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
    ///
    /// REVISIONE (bug della caduta intermittente in Simulation): la regola del "punto piu'
    /// basso" vale per i MESH COLLIDER delle aree GS, dove serve a bucare le chiome. Sul
    /// BOX COLLIDER pieno del suolo generato trovava invece la faccia INFERIORE della soletta
    /// (il raggio che sale entra da sotto, quello che scende da sopra, e il piu' basso vince):
    /// il rig veniva posato 15 cm DENTRO il pavimento, con la capsula del CharacterController
    /// compenetrata di ~8 cm — cioe' al limite della Skin Width (0.08). Da li' l'intermittenza:
    /// a volte il CC si faceva espellere, a volte il primo Move tunnellava attraverso la
    /// soletta e il player precipitava nel vuoto. La correzione: quando il suolo e' GENERATO
    /// la sua quota non si sonda, SI CONOSCE — e' StandBuilder.GroundY. La sonda fisica resta
    /// per le aree GS, dove e' nata e ha ragione lei.
    /// </summary>
    public class XrRigPlacer : MonoBehaviour
    {
        [Tooltip("Punto di spawn. Vuoto = il transform di questo oggetto.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Nella scena Simulation il suolo e' GENERATO da StandBuilder, con centro e lato " +
                 "che dipendono dagli alberi dell'inventario: un punto di spawn fisso puo' " +
                 "cadere vicino al bordo o fuori dal piano. Con questo attivo il rig viene posato " +
                 "al centro del quadrato effettivamente costruito, qualunque esso sia — e alla " +
                 "QUOTA dichiarata dal builder, senza interrogare la fisica.")]
        [SerializeField] private bool useGeneratedGroundCentre = true;

        [Header("Sparpagliamento in sessione")]
        [Tooltip("Raggio entro cui distanziare i partecipanti attorno allo spawn (m). 0 = tutti " +
                 "sullo stesso punto. Serve in multiplayer: senza, la classe si materializza " +
                 "compenetrata nello stesso metro quadrato.")]
        [SerializeField] private float scatterRadius = 1.2f;

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

        private Artemis.Regeneration.StandBuilder boundBuilder;

        private void Start()
        {
            started = Time.time;
            Freeze();              // prima del primo passo di fisica: nessuna caduta visibile
        }

        private void OnDestroy()
        {
            if (boundBuilder != null) boundBuilder.OnRebuilt -= OnGroundRebuilt;
        }

        /// <summary>
        /// Il suolo della simulazione e' GENERATO, e viene ricostruito ogni volta che cambia
        /// l'inventario — sullo studente questo accade DOPO che il rig e' gia' stato posato,
        /// quando arriva il soprassuolo del docente. In quel momento centro, lato e quota del
        /// piano cambiano sotto i piedi: se non ci si riposa, si resta appesi nel vuoto o
        /// sepolti. Ecco perche' la caduta capitava "non sempre, ma capitava": dipendeva da
        /// quale dei due eventi arrivava prima.
        /// </summary>
        private void OnGroundRebuilt()
        {
            Debug.Log("[XrRigPlacer] suolo ricostruito: riposiziono il rig.");
            placed = false;
            started = Time.time;      // riparte anche la finestra di attesa
            nextTry = 0f;
            Freeze();
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

            // Ci si aggancia allo StandBuilder appena esiste, per essere avvisati delle
            // ricostruzioni successive del suolo.
            if (boundBuilder == null)
            {
                boundBuilder = FindFirstObjectByType<Artemis.Regeneration.StandBuilder>();
                if (boundBuilder != null) boundBuilder.OnRebuilt += OnGroundRebuilt;
            }

            var spawn = spawnPoint != null ? spawnPoint : transform;
            Vector3 pos = spawn.position;
            float scatterLimit = scatterRadius;

            // In una scena che HA uno StandBuilder, il suolo e' generato: non si posa nulla
            // finche' non e' costruito, altrimenti si atterra su un piano che non esiste ancora.
            if (useGeneratedGroundCentre && boundBuilder != null &&
                boundBuilder.SquareSide <= 0.01f && Time.time - started < maxFreezeSeconds)
                return;

            // SUOLO GENERATO: centro E QUOTA arrivano dal builder, e la fisica non viene
            // interrogata affatto. Il motivo e' il bug della caduta intermittente: la sonda
            // "punto piu' basso" sul BoxCollider pieno della soletta trovava la faccia
            // INFERIORE e posava il rig 15 cm dentro il pavimento — compenetrazione della
            // capsula ~ skin width, cioe' espulsione o tunneling a seconda del frame. La quota
            // del piano la dichiara chi il piano l'ha costruito; chiederla a un raycast e'
            // chiedere a un testimone meno affidabile del progettista.
            // (l'uso degli out sta DENTRO l'if: con il && in corto circuito il compilatore non
            // puo' garantirne l'assegnazione fuori, ed e' un errore CS0165/CS0170)
            bool onGeneratedGround = false;
            if (useGeneratedGroundCentre &&
                TryGeneratedGround(out Vector2 c, out float side, out float gy))
            {
                onGeneratedGround = true;
                pos = new Vector3(c.x, gy + groundOffset, c.y);
                // Lo sparpagliamento non deve poter spingere nessuno oltre il bordo: si limita
                // a un terzo del semilato, che su un quadrato piccolo tiene tutti ben dentro.
                scatterLimit = Mathf.Min(scatterRadius, side * 0.5f / 3f);
            }

            pos += ScatterOffset(scatterLimit);

            if (snapToGround && !onGeneratedGround)
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
            Debug.Log($"[XrRigPlacer] rig XR posato a {pos} in '{gameObject.scene.name}'" +
                      (onGeneratedGround ? " (quota dal suolo generato)." : " (quota dalla sonda fisica)."));
        }

        /// <summary>
        /// Scostamento dallo spawn, uno diverso per ciascun partecipante.
        ///
        /// DETERMINISTICO e non casuale: l'angolo aureo (137.5 gradi) distribuisce gli indici
        /// successivi in modo uniforme attorno al cerchio senza mai ripetersi, quindi due
        /// partecipanti non finiscono mai nello stesso punto — cosa che con il caso puro
        /// succederebbe eccome, e proprio in aula davanti a tutti. In piu' ognuno sa in anticipo
        /// dove si materializzera', il che rende le prove ripetibili.
        ///
        /// Si sposta il RIG, non l'avatar: spostare l'avatar separerebbe la figura dalla persona,
        /// e si vedrebbe il compagno mezzo metro a lato di dove punta davvero il suo raggio.
        /// </summary>
        private Vector3 ScatterOffset(float radius)
        {
            if (radius <= 0.01f) return Vector3.zero;

            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return Vector3.zero;   // da soli, nessuno da evitare

            float a = nm.LocalClientId * 137.508f * Mathf.Deg2Rad;    // angolo aureo
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
        }

        /// Centro, lato E QUOTA del suolo generato, quando in scena c'e' uno StandBuilder che ha
        /// gia' costruito. La quota e' la novita' della revisione: e' il dato che evita di
        /// sondare con la fisica un pavimento la cui altezza e' gia' nota per costruzione.
        /// Nelle scene-area lo StandBuilder non esiste affatto ed e' giusto che questo
        /// componente continui a funzionare come prima (sonda fisica sul mesh reale).
        private bool TryGeneratedGround(out Vector2 centre, out float side, out float groundY)
        {
            centre = Vector2.zero; side = 0f; groundY = 0f;
            var b = boundBuilder != null ? boundBuilder
                                         : FindFirstObjectByType<Artemis.Regeneration.StandBuilder>();
            if (b == null) return false;
            if (b.SquareSide <= 0.01f) return false;    // non ha ancora costruito nulla
            centre = b.SquareCenter;
            side = b.SquareSide;
            groundY = b.GroundY;
            return true;
        }

        /// Suolo = superficie PIU' BASSA lungo la verticale (lezione canopy del desktop),
        /// ma sondata in ENTRAMBE le direzioni. Il motivo: i raycast di Unity non colpiscono
        /// le backface dei MeshCollider, quindi un raggio che SALE vede solo le facce rivolte
        /// in giu' — sui terreni GS specchiati (scala -1, winding misto) funzionava, su un
        /// piano ordinario con le normali in su la sonda era cieca. Salendo E scendendo si
        /// coprono entrambi i winding, e il punto piu' basso resta il suolo vero anche sotto
        /// una chioma con collider.
        ///
        /// ATTENZIONE: questa euristica vale SOLO per i mesh collider delle aree GS. Sul box
        /// del suolo generato "il punto piu' basso" e' la faccia inferiore della soletta — e'
        /// il bug della caduta intermittente — quindi in Simulation questa funzione non viene
        /// piu' chiamata (vedi onGeneratedGround in Update).
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
