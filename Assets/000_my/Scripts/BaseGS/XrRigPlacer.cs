using Unity.XR.CoreUtils;
using UnityEngine;

namespace Artemis.Session
{
    /// <summary>
    /// Porta il rig XR PERSISTENTE sul punto di partenza di QUESTA scena, e garantisce che ci sia
    /// del terreno sotto i piedi prima di restituire la gravita'.
    ///
    /// REVISIONE dopo la sessione di debug sulle "cadute nel vuoto". I difetti erano tre, e si
    /// sommavano:
    ///
    ///  1. L'ancora era un oggetto piazzato A MANO in scena. Se lo SpawnPoint di un'area sta
    ///     vicino al bordo della mesh — cosa che non si nota finche' non ci si sposta — basta uno
    ///     scostamento per finire fuori.
    ///
    ///  2. Lo sparpagliamento dipende da LocalClientId, cioe' DALLO STATO DI RETE. Ecco perche'
    ///     la stessa build su due visori con lo stesso ruolo apparente atterrava in due punti
    ///     diversi: un docente host ha id 0 e riceve un offset fisso di oltre un metro, lo stesso
    ///     ruolo fuori sessione riceve zero. Uno dentro il collider, l'altro sistematicamente
    ///     fuori — deterministico, non casuale, ed e' per questo che sembrava inspiegabile.
    ///
    ///  3. La posizione calcolata non veniva MAI verificata prima di sbloccare il
    ///     CharacterController: se la sonda non trovava terreno entro il tempo massimo si
    ///     scriveva un avviso nel log e si lasciava cadere il giocatore comunque.
    ///
    /// Ora: l'ancora e' il CENTRO del collider dell'area (o del quadrato generato in Simulation);
    /// ogni posizione candidata viene VALIDATA con una sonda prima di essere usata, e se non
    /// regge si ripiega verso il centro; e una rete di sicurezza riprende chi sta precipitando.
    ///
    /// Resta la lezione desktop: il rig puo' apparire DOPO Start(), quindi retry paziente, mai
    /// pretendere il primo frame.
    ///
    /// Va sull'oggetto SpawnPoint di ogni scena, come prima.
    /// </summary>
    public class XrRigPlacer : MonoBehaviour
    {
        [Header("Ancora")]
        [Tooltip("Usa il CENTRO del collider dell'area invece del punto di spawn piazzato a mano. " +
                 "E' la scelta robusta: il centro di un'area di saggio ha terreno sotto per " +
                 "definizione, un punto messo a mano puo' finire vicino al bordo senza che si veda.")]
        [SerializeField] private bool useColliderCentre = true;

        [Tooltip("Punto di spawn di ripiego, usato se il collider non si trova o se il centro " +
                 "e' disattivato. Vuoto = il transform di questo oggetto. Da lui si prende " +
                 "comunque sempre la DIREZIONE dello sguardo iniziale.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("In Simulation il suolo e' GENERATO da StandBuilder: centro e QUOTA arrivano da " +
                 "lui e la fisica non viene interrogata affatto. Sul box pieno della soletta la " +
                 "regola del 'punto piu' basso' troverebbe la faccia INFERIORE, posando il rig " +
                 "dentro il pavimento — compenetrato quanto la Skin Width, cioe' espulso o " +
                 "tunnellato a seconda del frame.")]
        [SerializeField] private bool useGeneratedGroundCentre = true;

        [Header("Sparpagliamento in sessione")]
        [Tooltip("Raggio entro cui distanziare i partecipanti attorno all'ancora (m). Tenerlo " +
                 "piccolo: la classe deve restare a portata di voce e di sguardo, e ogni metro " +
                 "in piu' e' un metro piu' vicino al bordo.")]
        [SerializeField] private float scatterRadius = 0.8f;

        [Header("Aggancio al suolo")]
        [SerializeField] private bool snapToGround = true;
        [Tooltip("Layer del collider terreno di questa scena (PlotTerrain nelle aree, SimGround " +
                 "in Simulation).")]
        [SerializeField] private LayerMask groundLayer = ~0;
        [Tooltip("Semilunghezza della sonda verticale: deve abbracciare l'area dal punto piu' " +
                 "basso al piu' alto.")]
        [SerializeField] private float probeHeight = 100f;
        [SerializeField] private float groundOffset = 0.05f;

        [Header("Congelamento")]
        [SerializeField] private bool freezeUntilPlaced = true;
        [Tooltip("Valvola di sicurezza: sblocca comunque dopo questo tempo. Alla scadenza si posa " +
                 "il rig sull'ancora, NON su una quota inventata.")]
        [SerializeField] private float maxFreezeSeconds = 8f;

        [Header("Rete di sicurezza")]
        [Tooltip("Riprende il giocatore se scende sotto la quota di posa di questo margine (m). " +
                 "Serve contro le cause che non conosciamo: qualunque sia il motivo per cui si e' " +
                 "finiti nel vuoto, cadere per sempre e' l'unico esito davvero inaccettabile in " +
                 "aula. 0 = disattivata.")]
        [SerializeField] private float fallRescueDepth = 8f;
        [Tooltip("Quanti recuperi prima di rinunciare, per non entrare in un ciclo infinito se il " +
                 "terreno e' irrecuperabile.")]
        [SerializeField] private int maxRescues = 5;

        [Header("Ritentativi")]
        [SerializeField] private float giveUpAfterSeconds = 10f;
        [SerializeField] private float retryInterval = 0.25f;

        private bool placed;
        private float started, nextTry;
        private CharacterController frozen;
        private Artemis.Regeneration.StandBuilder boundBuilder;

        [Tooltip("Stampa TUTTI gli impatti lungo la verticale nel punto di posa, con quota e " +
                 "normale. E' la misura che dice se la regola del 'punto piu' basso' sta pescando " +
                 "qualcosa sotto il terreno vero — un frammento di scansione o l'altra faccia del " +
                 "guscio — invece del suolo calpestabile. Da spegnere prima del pilot.")]
        [SerializeField] private bool logGroundProbe = true;

        private Vector3 lastGoodPosition;
        private bool hasLastGood;
        private int rescues;

        /// Quante volte si e' gia' provato a posare qui: dopo una caduta si cambia posto, non si
        /// ritenta lo stesso punto — che e' gia' stato smentito dai fatti.
        private int attempt;

        /// Generazione di suolo gia' vista: distingue un pavimento nuovo da un bosco nuovo.
        private int lastGroundGeneration = -1;

        // ---- ciclo di vita ---------------------------------------------------------------------

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
        /// Il suolo della simulazione e' GENERATO e viene ricostruito quando cambia l'inventario —
        /// sullo studente questo accade DOPO che il rig e' gia' stato posato, quando arriva il
        /// soprassuolo del docente. In quel momento centro, lato e quota cambiano sotto i piedi.
        /// </summary>
        private void OnGroundRebuilt()
        {
            // OnRebuilt lo emette anche chi cambia il solo SOPRASSUOLO: il ripristino del bosco e
            // la riapplicazione del clima sulla martellata gia' fatta. In quei casi il pavimento
            // e' rimasto esattamente dov'era, e riposare il rig significherebbe teletrasportare
            // tutti al centro proprio mentre stanno guardando una buca — cioe' rovinare il
            // confronto fra scenari che quell'operazione serviva a mostrare.
            int gen = boundBuilder != null ? boundBuilder.GroundGeneration : -1;
            if (gen >= 0 && gen == lastGroundGeneration) return;
            lastGroundGeneration = gen;

            Debug.Log("[XrRigPlacer] suolo ricostruito: riposiziono il rig.");
            placed = false;
            started = Time.time;
            nextTry = 0f;
            rescues = 0;
            Freeze();
        }

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

        // ---- posa -------------------------------------------------------------------------------

        private void Update()
        {
            if (placed) { WatchForFalls(); return; }
            if (Time.time < nextTry) return;
            nextTry = Time.time + retryInterval;

            Freeze();              // il rig puo' comparire dopo Start

            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                if (Time.time - started > giveUpAfterSeconds)
                {
                    Debug.LogWarning("[XrRigPlacer] nessun XROrigin entro il timeout — rinuncio.");
                    placed = true;
                    Unfreeze();
                }
                return;
            }

            if (boundBuilder == null)
            {
                boundBuilder = FindFirstObjectByType<Artemis.Regeneration.StandBuilder>();
                if (boundBuilder != null) boundBuilder.OnRebuilt += OnGroundRebuilt;
            }

            bool timedOut = Time.time - started > maxFreezeSeconds;

            if (!TryResolveAnchor(out Vector3 anchor, out float extent, out bool generated))
            {
                // Ancora non ancora disponibile: StandBuilder deve costruire, oppure il collider
                // dell'area non e' ancora registrato nella fisica. Si aspetta CONGELATI — e' tutto
                // il senso del congelamento.
                if (!timedOut) return;

                anchor = FallbackSpawn().position;
                extent = 0f; generated = false;
                Debug.LogWarning("[XrRigPlacer] ancora non risolta entro il tempo massimo: uso il " +
                                 "punto di spawn di ripiego. Controlla groundLayer e il collider.");
            }

            Vector3 pos = ChoosePosition(anchor, extent, generated, out string how);
            var spawn = FallbackSpawn();
            origin.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));

            placed = true;
            if (boundBuilder != null) lastGroundGeneration = boundBuilder.GroundGeneration;
            lastGoodPosition = pos;
            hasLastGood = true;
            Unfreeze();

            Debug.Log($"[XrRigPlacer] rig XR posato a {pos} in '{gameObject.scene.name}' ({how}).");
            if (logGroundProbe && !generated) LogVerticalHits(pos.x, pos.z);
        }

        private Transform FallbackSpawn() => spawnPoint != null ? spawnPoint : transform;

        /// <summary>
        /// Da dove si parte: il centro del quadrato generato in Simulation, oppure il centro del
        /// collider dell'area, oppure il punto di spawn. 'extent' e' il semilato utile entro cui
        /// e' lecito sparpagliarsi.
        /// </summary>
        private bool TryResolveAnchor(out Vector3 anchor, out float extent, out bool generated)
        {
            anchor = Vector3.zero; extent = 0f; generated = false;

            // --- suolo GENERATO (Simulation): centro e quota li dichiara chi l'ha costruito ----
            if (useGeneratedGroundCentre && boundBuilder != null)
            {
                if (boundBuilder.SquareSide <= 0.01f) return false;   // non ha ancora costruito
                anchor = new Vector3(boundBuilder.SquareCenter.x,
                                     boundBuilder.GroundY + groundOffset,
                                     boundBuilder.SquareCenter.y);
                extent = boundBuilder.SquareSide * 0.5f;
                generated = true;
                return true;
            }

            // --- area reale: centro del collider del terreno -----------------------------------
            if (useColliderCentre && TryTerrainBounds(out Bounds b))
            {
                extent = Mathf.Min(b.extents.x, b.extents.z);

                // Non si sonda il solo centro: le mesh di rilievo hanno BUCHI, soprattutto verso
                // i margini ma non solo, e un unico raggio che passa per un buco farebbe fallire
                // l'ancora migliore che abbiamo — ripiegando proprio su quel punto piazzato a mano
                // che stiamo cercando di non usare piu'. Si cerca quindi il punto valido PIU'
                // VICINO al centro.
                if (!TrySampleGroundNear(b.center.x, b.center.z, extent * 0.6f, out Vector3 g))
                    return false;

                anchor = new Vector3(g.x, g.y + groundOffset, g.z);
                return true;
            }

            // --- ripiego: il punto piazzato a mano ---------------------------------------------
            var sp = FallbackSpawn();
            if (snapToGround)
            {
                if (!TrySampleGround(sp.position.x, sp.position.z, out float y)) return false;
                anchor = new Vector3(sp.position.x, y + groundOffset, sp.position.z);
            }
            else anchor = sp.position;

            extent = 0f;
            return true;
        }

        /// <summary>
        /// Ingombro complessivo dei collider sul layer del terreno in questa scena. Il centro di
        /// quell'ingombro e' il punto piu' sicuro che esista: per un'area di saggio quadrata e' il
        /// suo centro geometrico, che ha terreno sotto per definizione — a differenza di un
        /// oggetto piazzato a mano, che nessuno rimisura piu' dopo averlo messo.
        /// </summary>
        private bool TryTerrainBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;

            foreach (var c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null || c.isTrigger) continue;
                if (((1 << c.gameObject.layer) & groundLayer.value) == 0) continue;
                if (c is CharacterController) continue;         // il giocatore non e' terreno

                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }
            return any;
        }

        /// <summary>
        /// Posizione definitiva: si parte dallo scostamento pieno e lo si accorcia finche' non si
        /// trova terreno, fino a restare sull'ancora. Il punto e' che NON si posa piu' nessuno
        /// senza aver verificato: la versione precedente calcolava lo scostamento e si fidava, e
        /// bastava uno SpawnPoint vicino al bordo perche' quel metro e venti finisse fuori dalla
        /// mesh — con esito diverso da visore a visore, perche' la direzione dipende dal
        /// LocalClientId.
        /// </summary>
        private Vector3 ChoosePosition(Vector3 anchor, float extent, bool generated, out string how)
        {
            float limit = scatterRadius;
            if (extent > 0.01f) limit = Mathf.Min(limit, extent / 3f);   // mai vicino al bordo

            Vector3 dir = ScatterDirection();
            float[] factors = { 1f, 0.5f, 0.25f, 0f };                   // pieno, meta', un quarto, centro

            foreach (float f in factors)
            {
                Vector3 candidate = anchor + dir * (limit * f);

                if (generated)
                {
                    // Quota nota per costruzione: si verifica solo di restare dentro il quadrato.
                    if (extent > 0.01f &&
                        (Mathf.Abs(candidate.x - anchor.x) > extent ||
                         Mathf.Abs(candidate.z - anchor.z) > extent)) continue;
                    how = f > 0f ? $"suolo generato, scostamento {limit * f:F2} m"
                                 : "suolo generato, centro";
                    return candidate;
                }

                if (!snapToGround) { how = "senza aggancio al suolo"; return candidate; }

                // Anche qui si tollera un buco: si accetta un punto vicino, purche' entro mezzo
                // metro, altrimenti si passa allo scostamento piu' corto.
                if (TrySampleGroundNear(candidate.x, candidate.z, 0.5f, out Vector3 g))
                {
                    how = f > 0f ? $"verificato, scostamento {limit * f:F2} m"
                                 : "verificato, centro dell'area";
                    return new Vector3(g.x, g.y + groundOffset, g.z);
                }
            }

            how = "NESSUN terreno trovato: ancora cosi' com'e'";
            Debug.LogWarning($"[XrRigPlacer] nessuna posizione con terreno attorno a {anchor} — " +
                             "controlla groundLayer e il collider dell'area.");
            return anchor;
        }

        /// <summary>
        /// Direzione dello scostamento, una diversa per partecipante. Angolo aureo: indici
        /// successivi si distribuiscono uniformemente sul cerchio senza mai ripetersi, quindi due
        /// persone non si materializzano mai nello stesso punto — e ognuno sa in anticipo dove
        /// comparira', il che rende le prove ripetibili.
        /// </summary>
        private Vector3 ScatterDirection()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            ulong id = (nm != null && nm.IsListening) ? nm.LocalClientId : 0UL;

            // Ogni tentativo ruota di 90 gradi: se il primo punto si e' rivelato sfondabile, non
            // ha senso riprovarlo — si va a cercare terreno da un'altra parte.
            float a = (id * 137.508f + attempt * 90f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        // ---- rete di sicurezza ---------------------------------------------------------------

        /// <summary>
        /// Se il giocatore sta precipitando lo si riprende e si rifa' la posa. Non e' una diagnosi,
        /// ed e' bene ricordarlo: le cause vanno trovate e corrette. Ma fra "cade per sempre e la
        /// lezione si interrompe" e "torna al centro con un avviso nel log", in aula non c'e'
        /// partita — e l'avviso, con la quota da cui e' partita la caduta, e' anche il modo per
        /// accorgersi che il problema esiste ancora invece di scoprirlo dai racconti.
        /// </summary>
        private void WatchForFalls()
        {
            if (fallRescueDepth <= 0.01f || !hasLastGood || rescues >= maxRescues) return;

            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null) return;

            float y = origin.transform.position.y;
            if (y > lastGoodPosition.y - fallRescueDepth) return;

            rescues++;
            attempt++;                      // il punto di prima e' smentito dai fatti: se ne cerca un altro

            Debug.LogWarning($"[XrRigPlacer] CADUTA rilevata (y={y:F1}, posato a {lastGoodPosition.y:F1}): " +
                             $"quel punto NON regge il giocatore anche se la sonda ci trovava una " +
                             $"superficie. Cerco un'altra posizione — recupero {rescues}/{maxRescues}.");
            if (logGroundProbe) LogVerticalHits(lastGoodPosition.x, lastGoodPosition.z);

            // Si riparte dalla posa da capo, ma sollevati: restare alla quota della caduta
            // significherebbe ricadere prima ancora che il nuovo punto venga scelto.
            placed = false;
            started = Time.time;
            nextTry = 0f;
            Freeze();
            origin.transform.position = lastGoodPosition + Vector3.up * 0.5f;
        }

        // ---- sonda del suolo ---------------------------------------------------------------------

        /// Suolo = superficie PIU' BASSA lungo la verticale (lezione canopy del desktop), sondata
        /// in ENTRAMBE le direzioni perche' i raycast non colpiscono le backface dei MeshCollider:
        /// sui terreni GS specchiati (scala -1, winding misto) un raggio che sale vede solo le
        /// facce rivolte in giu', su un piano ordinario e' cieco. Salendo E scendendo si coprono
        /// entrambi i winding, e il punto piu' basso resta il suolo vero anche sotto una chioma.
        ///
        /// NON si usa in Simulation: sul box pieno del suolo generato "il piu' basso" e' la faccia
        /// inferiore della soletta. La' la quota la dichiara StandBuilder.
        /// <summary>
        /// Punto di terreno valido PIU' VICINO a (x, z): prima il punto stesso, poi anelli via via
        /// piu' larghi, otto direzioni per anello. Si ferma al primo che regge, quindi il
        /// risultato e' sempre il piu' centrale possibile.
        ///
        /// Esiste per i BUCHI nelle mesh di rilievo: una superficie ricostruita da scansione non
        /// e' continua, e un singolo raggio che capita in una lacuna non dice "qui non c'e'
        /// l'area", dice solo "qui non c'e' un triangolo". Trattare le due cose come la stessa
        /// e' cio' che mandava il giocatore a cadere.
        /// </summary>
        /// <summary>
        /// Elenca TUTTE le superfici incontrate lungo la verticale in (x, z), dal basso in alto,
        /// con quota e inclinazione della normale. Serve a capire cosa sta scegliendo la regola
        /// del "punto piu' basso" quando il giocatore cade: se sotto al suolo vero c'e' un
        /// frammento di scansione, qui si vede — e la quota giusta si legge dall'elenco.
        ///
        /// La normale distingue una superficie calpestabile (rivolta in su) da una faccia
        /// interna del guscio o da un residuo (rivolta in giu' o di taglio).
        /// </summary>
        private void LogVerticalHits(float x, float z)
        {
            var all = new System.Collections.Generic.List<RaycastHit>();
            all.AddRange(Physics.RaycastAll(new Vector3(x, -probeHeight, z), Vector3.up,
                                            probeHeight * 2f, groundLayer));
            all.AddRange(Physics.RaycastAll(new Vector3(x,  probeHeight, z), Vector3.down,
                                            probeHeight * 2f, groundLayer));
            all.Sort((a, b) => a.point.y.CompareTo(b.point.y));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[XrRigPlacer] sonda verticale in ({x:F2}, {z:F2}) — {all.Count} superfici:");
            foreach (var h in all)
                sb.AppendLine($"    y={h.point.y,8:F2}   normale·su={Vector3.Dot(h.normal, Vector3.up),6:F2}   " +
                              $"'{h.collider.name}'");
            if (all.Count == 0) sb.AppendLine("    (nessuna: qui non c'e' proprio niente)");
            Debug.LogWarning(sb.ToString());
        }

        private bool TrySampleGroundNear(float x, float z, float maxRadius, out Vector3 point)
        {
            point = Vector3.zero;

            if (TrySampleGround(x, z, out float y0))
            {
                point = new Vector3(x, y0, z);
                return true;
            }

            const int dirs = 8;
            float step = Mathf.Max(0.5f, maxRadius / 6f);
            for (float r = step; r <= Mathf.Max(step, maxRadius); r += step)
            {
                for (int i = 0; i < dirs; i++)
                {
                    float a = Mathf.PI * 2f * i / dirs;
                    float px = x + Mathf.Cos(a) * r;
                    float pz = z + Mathf.Sin(a) * r;
                    if (!TrySampleGround(px, pz, out float y)) continue;

                    point = new Vector3(px, y, pz);
                    Debug.Log($"[XrRigPlacer] centro dell'area senza terreno (buco nella mesh): " +
                              $"uso il punto valido piu' vicino, a {r:F1} m.");
                    return true;
                }
            }
            return false;
        }

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
