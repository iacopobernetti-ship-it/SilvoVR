using UnityEngine;

namespace Artemis.Vr
{
    /// <summary>
    /// Imposta lo sfondo della camera per QUESTA scena.
    ///
    /// Perche' non si tocca la camera direttamente: la Main Camera vive dentro il prefab VrApp,
    /// quindi il suo Background Type e' un valore del PREFAB, identico in tutte e sei le scene.
    /// Cambiarlo dove serve significherebbe sei override serializzati, cioe' sei posti dove una
    /// scena puo' restare indietro senza che nessuno se ne accorga finche' non la si apre in
    /// visore. Qui invece la scelta e' un oggetto della scena: si vede nella Hierarchy, si legge
    /// nell'Inspector, e chi apre la scena capisce subito com'e' configurata.
    ///
    /// Uso previsto:
    ///   Base            → Skybox, con la foto sferica dell'abbazia (ambientazione);
    ///   Silvo01..04     → Solid Color nero, perche' il bosco e' il Gaussian Splatting e tutto
    ///                     cio' che non e' splat deve sparire nel nero;
    ///   Simulation      → Solid Color nero, per coerenza visiva con le aree.
    ///
    /// Non e' solo estetica: in visore lo skybox e' fill rate speso su ogni pixel non coperto, e
    /// nelle aree quei pixel sono tanti. Il nero pieno e' la scelta piu' economica che ci sia, e
    /// dopo la giornata passata a recuperare millisecondi vale la pena non regalarli indietro.
    ///
    /// Va su un oggetto qualsiasi della scena (per esempio lo stesso della Directional Light).
    /// </summary>
    public class SceneBackground : MonoBehaviour
    {
        public enum Mode { SolidColor, Skybox }

        [Tooltip("Solid Color = tinta piena (nero nelle aree e in Simulation). " +
                 "Skybox = il materiale qui sotto, o quello gia' impostato nel Lighting.")]
        [SerializeField] private Mode mode = Mode.SolidColor;

        [Tooltip("Tinta di sfondo quando il modo e' Solid Color.")]
        [SerializeField] private Color color = Color.black;

        [Tooltip("Materiale di skybox da usare in questa scena. Vuoto = si lascia quello gia' " +
                 "impostato in Window → Rendering → Lighting → Environment.")]
        [SerializeField] private Material skybox;

        [Tooltip("Ogni quanto ricontrollare che la camera sia ancora configurata. La camera " +
                 "appartiene al prefab e puo' comparire DOPO questo componente, quindi non si " +
                 "pretende il primo frame — e' la stessa pazienza che serve al rig.")]
        [SerializeField] private float recheckInterval = 0.5f;

        private Camera bound;
        private float nextCheck;

        private void Start() { Apply(); }

        private void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + recheckInterval;

            // Ci si riaggancia se la camera cambia: succede a ogni ricostruzione del prefab.
            if (Camera.main != bound) Apply();
        }

        private void Apply()
        {
            if (mode == Mode.Skybox && skybox != null && RenderSettings.skybox != skybox)
            {
                RenderSettings.skybox = skybox;
                // Senza questo l'illuminazione ambientale resta quella dello skybox precedente:
                // in Base si vedrebbe la luce di un cielo che non c'e' piu'.
                DynamicGI.UpdateEnvironment();
            }

            var cam = Camera.main;
            if (cam == null) return;
            bound = cam;

            if (mode == Mode.Skybox)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = color;
            }

            Debug.Log($"[SceneBackground] '{gameObject.scene.name}': sfondo {mode}" +
                      (mode == Mode.Skybox
                          ? $" (materiale '{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "nessuno")}')."
                          : $" {color}."));
        }
    }
}
