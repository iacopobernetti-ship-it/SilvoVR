using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Artemis.EditorTools
{
    /// <summary>
    /// SONDA TEMPORANEA — da rimuovere prima del pilot (aggiungila alla lista del §6).
    ///
    /// Fotografa ogni TELETRASPORTO del rig XR (salto orizzontale oltre jumpThreshold in un
    /// frame): posizione prima/dopo, stato del CharacterController, stato dello StandBuilder
    /// (centro/lato del quadrato, se il punto d'arrivo e' DENTRO il piano) e se sotto il punto
    /// d'arrivo esiste davvero un suolo sul layer indicato. Fotografa anche l'INIZIO di una
    /// caduta, per datare l'evento accanto ai log dei traslocatori.
    ///
    /// Perche' esiste: il censimento del codice dice che il rig lo muovono solo XrRigPlacer,
    /// StandBuilder e (in Editor, su input) EditorMousePointer — i primi due LOGGANO la
    /// destinazione. Un TELETRASPORTO fotografato qui SENZA la riga gemella di uno dei due
    /// = traslocatore fuori censimento (XRI, provider di locomozione, altro).
    ///
    /// Va su SimTools nella scena Simulation, sull'istanza STUDENTE.
    /// Filtro logcat:  adb logcat -s Unity | grep RIGPROBE
    /// </summary>
    public class RigMoveProbe : MonoBehaviour
    {
        [Tooltip("Salto orizzontale (m) in un frame oltre il quale si fotografa. I movimenti di " +
                 "locomozione stanno sotto: a 90 fps anche correndo si resta sotto i 10 cm/frame.")]
        [SerializeField] private float jumpThreshold = 1.0f;

        [Tooltip("Layer del suolo da sondare nel punto d'arrivo (in Simulation: SimGround).")]
        [SerializeField] private LayerMask groundLayer = ~0;

        [Tooltip("Logga anche l'inizio di una CADUTA (Y che scende oltre questa soglia in un " +
                 "frame senza salto orizzontale): dice QUANDO si e' cominciato a precipitare.")]
        [SerializeField] private float fallThreshold = 0.5f;

        private XROrigin origin;
        private Vector3 lastPos;
        private bool hasLast;
        private bool fallLogged;

        private void LateUpdate()   // dopo tutti gli Update: vede la posizione di fine frame
        {
            if (origin == null)
            {
                origin = FindFirstObjectByType<XROrigin>();
                if (origin == null) return;
                lastPos = origin.transform.position;
                hasLast = true;
                Debug.Log($"[RIGPROBE] aggancio al rig '{origin.name}' a {Fmt(lastPos)}.");
                return;
            }

            Vector3 now = origin.transform.position;
            if (!hasLast) { lastPos = now; hasLast = true; return; }

            Vector2 dh = new Vector2(now.x - lastPos.x, now.z - lastPos.z);
            float dy = now.y - lastPos.y;

            if (dh.magnitude >= jumpThreshold)
            {
                Snapshot("TELETRASPORTO", lastPos, now);
                fallLogged = false;      // un nuovo salto riarma il rilevatore di caduta
            }
            else if (dy <= -fallThreshold && !fallLogged)
            {
                fallLogged = true;       // una sola foto per caduta, non una per frame
                Snapshot("INIZIO CADUTA", lastPos, now);
            }
            else if (dy > 0.01f)
            {
                fallLogged = false;      // risalito (riposato sul suolo): riarma
            }

            lastPos = now;
        }

        private void Snapshot(string evento, Vector3 from, Vector3 to)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[RIGPROBE] {evento}  frame {Time.frameCount}  t={Time.time:F2}s");
            sb.AppendLine($"  da {Fmt(from)}  a {Fmt(to)}  (salto orizz. " +
                          $"{Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z)):F2} m)");

            // CharacterController: chi era al comando della gravita' in quel momento.
            var cc = origin.GetComponentInChildren<CharacterController>();
            sb.AppendLine($"  CharacterController: {(cc == null ? "ASSENTE" : cc.enabled ? "ATTIVO (gravita' in corso)" : "congelato")}");

            // Stato dello StandBuilder: il punto d'arrivo e' dentro il piano costruito?
            var b = FindFirstObjectByType<Artemis.Regeneration.StandBuilder>();
            if (b == null) sb.AppendLine("  StandBuilder: assente in scena");
            else if (b.SquareSide <= 0.01f) sb.AppendLine("  StandBuilder: presente ma NESSUN piano costruito");
            else
            {
                float half = b.SquareSide * 0.5f;
                bool inside = Mathf.Abs(to.x - b.SquareCenter.x) <= half &&
                              Mathf.Abs(to.z - b.SquareCenter.y) <= half;
                sb.AppendLine($"  Piano: centro ({b.SquareCenter.x:F1}, {b.SquareCenter.y:F1})  lato {b.SquareSide:F1}  " +
                              $"groundY {b.GroundY:F2}  ->  punto d'arrivo {(inside ? "DENTRO" : "FUORI DAL PIANO")}");
                // Quanti alberi ha il soprassuolo IN QUESTO ISTANTE: se il numero e' piu' basso
                // di quello atteso, il piano e' stato costruito da un inventario di rete ancora
                // in corso di replica — e il suo centro e' quello di un bounding box parziale.
                sb.AppendLine($"  Alberi nel soprassuolo al momento della foto: " +
                              $"{(b.OriginalStems != null ? b.OriginalStems.Count : 0)}");
            }

            // C'e' un suolo FISICO sotto il punto d'arrivo?
            bool hit = Physics.Raycast(new Vector3(to.x, to.y + 2f, to.z), Vector3.down,
                                       out var h, 200f, groundLayer);
            sb.AppendLine(hit
                ? $"  Suolo fisico sotto l'arrivo: SI a y={h.point.y:F2} ('{h.collider.name}', layer {LayerMask.LayerToName(h.collider.gameObject.layer)})"
                : "  Suolo fisico sotto l'arrivo: NO sul layer indicato");

            var nm = Unity.Netcode.NetworkManager.Singleton;
            sb.AppendLine($"  Ruolo: {(nm == null || !nm.IsListening ? "offline" : nm.IsServer ? "docente" : "studente")}");

            Debug.LogWarning(sb.ToString());
        }

        private static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    }
}
