using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Artemis.Vr
{
    /// <summary>
    /// Camera OVERLAY che disegna il layer HUD dopo il pass LCC (URP camera stacking).
    /// Documentazione XGRIDS alla mano: le camere vanno registrate esplicitamente all'SDK
    /// (AddCamera) e su Quest il percorso chunked e' mono-camera — la overlay, mai
    /// registrata, non ricevera' mai splat.
    ///
    /// REVISIONE dopo il collaudo (pulsanti morti, controller invisibili):
    ///  - il layer HUD viene applicato ai visual dei ray SOLO sul GameObject che ospita
    ///    il LineRenderer, SENZA ricorsione: la versione precedente ricorreva dall'oggetto
    ///    dell'XRInteractorLineVisual in giu', e se quel componente sta piu' in alto del
    ///    previsto nel rig si portava dietro anche i MODELLI dei controller;
    ///  - OnDisable ripristina tutto (culling mask, stack, layer della HUD): la checkbox
    ///    del componente diventa un vero interruttore A/B per il collaudo;
    ///  - diagnostica loquace e marcata [OVERLAY] nel logcat: elenca layer, stack della
    ///    Main e OGNI GameObject spostato di layer — cosi' il prossimo sintomo si legge,
    ///    non si indovina.  Filtro:  adb logcat -s Unity | grep OVERLAY
    /// </summary>
    public class VrHudOverlayCamera : MonoBehaviour
    {
        [Tooltip("Nome del layer dedicato alla HUD (Project Settings → Tags and Layers).")]
        [SerializeField] private string hudLayerName = "HUD";

        [Tooltip("Porta sul layer HUD i LineRenderer dei ray XRI, cosi' restano visibili " +
                 "sopra gli splat. SOLO il GameObject del LineRenderer, mai i figli.")]
        [SerializeField] private bool moveRayVisualsToHudLayer = true;

        [Tooltip("Porta sul layer HUD anche i MODELLI dei controller: il pass splat li " +
                 "sovrascrive come faceva col pannello (visibili solo contro il cielo). " +
                 "Si prendono per nome i sottoalberi dei visual, che sono sicuri per intero.")]
        [SerializeField] private bool moveControllerVisualsToHudLayer = true;
        [SerializeField] private string[] controllerVisualNames =
            { "Left Controller Visual", "Right Controller Visual" };

        [Tooltip("Ogni quanto ri-applicare il layer (i widget UI creati a runtime nascono " +
                 "su Default e non ereditano il layer del padre).")]
        [SerializeField] private float relayerInterval = 1f;

        private int hudLayer = -2;              // -2 = da risolvere, -1 = assente
        private Camera boundMain;
        private Camera overlay;
        private float nextRelayer;
        private bool loggedSetup;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            nextRelayer = 0f;
            loggedSetup = false;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            TearDown();
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m) { nextRelayer = 0f; }

        private void Update()
        {
            if (!ResolveLayer()) return;

            var main = Camera.main;
            if (main == null) return;
            if (main != boundMain) Bind(main);

            if (Time.time >= nextRelayer)
            {
                nextRelayer = Time.time + relayerInterval;
                RelayerHud();
                if (moveRayVisualsToHudLayer) RelayerRayVisuals();
                if (moveControllerVisualsToHudLayer) RelayerControllerVisuals();
                if (!loggedSetup) { loggedSetup = true; LogSetup(main); }
            }
        }

        // ------------------------------------------------------------------ setup / teardown

        private bool ResolveLayer()
        {
            if (hudLayer >= 0) return true;
            if (hudLayer == -1) return false;
            hudLayer = LayerMask.NameToLayer(hudLayerName);
            if (hudLayer < 0)
            {
                hudLayer = -1;
                Debug.LogError($"[OVERLAY] layer '{hudLayerName}' inesistente: crealo in Project " +
                               "Settings → Tags and Layers. Componente inerte fino ad allora.");
                return false;
            }
            Debug.Log($"[OVERLAY] layer '{hudLayerName}' = indice {hudLayer}.");
            return true;
        }

        private void Bind(Camera main)
        {
            boundMain = main;
            main.cullingMask &= ~(1 << hudLayer);

            if (overlay == null) CreateOverlay();

            overlay.transform.SetParent(main.transform, false);
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;

            var mainData = main.GetUniversalAdditionalCameraData();
            if (!mainData.cameraStack.Contains(overlay))
                mainData.cameraStack.Add(overlay);

            Debug.Log($"[OVERLAY] agganciata a '{main.name}'. Culling Main dopo il bind: {MaskToString(main.cullingMask)}");
        }

        private void CreateOverlay()
        {
            var go = new GameObject("HudOverlayCamera");
            overlay = go.AddComponent<Camera>();
            overlay.cullingMask = 1 << hudLayer;
            overlay.nearClipPlane = 0.05f;
            // Far largo: sul layer HUD vivono anche le LINEE DEI RAY, che possono estendersi
            // per decine di metri — il far a 10 le tagliava e il ray sembrava "corto".
            overlay.farClipPlane = 150f;

            var data = overlay.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.renderShadows = false;

            if (!data.clearDepth)
            {
                var f = typeof(UniversalAdditionalCameraData).GetField("m_ClearDepth",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f != null) { f.SetValue(data, true); Debug.Log("[OVERLAY] clearDepth forzato a true via reflection."); }
                else Debug.LogWarning("[OVERLAY] clearDepth=false e m_ClearDepth non trovato (versione URP?).");
            }
        }

        /// La checkbox del componente e' un interruttore A/B: spegnendolo si torna ESATTAMENTE
        /// allo stato precedente (HUD su Default, disegnata dalla Main, niente overlay).
        private void TearDown()
        {
            if (boundMain != null && hudLayer >= 0)
            {
                boundMain.cullingMask |= 1 << hudLayer;
                var data = boundMain.GetUniversalAdditionalCameraData();
                if (overlay != null) data.cameraStack.Remove(overlay);
            }
            if (overlay != null) { Destroy(overlay.gameObject); overlay = null; }

            var hud = VrHud.Instance;
            if (hud != null)
            {
                var canvasT = hud.transform.Find("VrHudCanvas");
                if (canvasT != null)
                {
                    SetLayerRecursively(canvasT, 0);
                    var c = canvasT.GetComponent<Canvas>();
                    if (c != null) c.worldCamera = Camera.main;   // torna a com'era: UI di nuovo viva
                }
            }
            boundMain = null;
            loggedSetup = false;
            Debug.Log("[OVERLAY] smontata: HUD restituita alla Main su layer Default.");
        }

        // ------------------------------------------------------------------ layer

        private void RelayerHud()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;
            var canvasT = hud.transform.Find("VrHudCanvas");
            if (canvasT == null) return;
            SetLayerRecursively(canvasT, hudLayer);

            // ESSENZIALE, e il motivo per cui i pulsanti erano morti: un Canvas world-space
            // fa il raycast UI attraverso la sua EVENT CAMERA. Se worldCamera e' null si
            // ripiega su Camera.main — ma la Main ha il layer HUD ESCLUSO dalla culling mask,
            // quindi la camera di riferimento non vede piu' la canvas e il raycaster smette
            // di produrre hit: HUD visibile (la disegna l'overlay) ma completamente sorda.
            var canvas = canvasT.GetComponent<Canvas>();
            if (canvas != null && overlay != null && canvas.worldCamera != overlay)
            {
                canvas.worldCamera = overlay;
                Debug.Log("[OVERLAY] event camera della HUD impostata sulla overlay " +
                          "(senza, il raycast UI muore: pulsanti visibili ma inerti).");
            }
        }

        /// SOLO il GameObject che ospita il LineRenderer del visual, MAI i figli e MAI
        /// una ricorsione dall'alto: la lezione del collaudo e' che non si puo' assumere
        /// dove il rig monti XRInteractorLineVisual, e una ricorsione partita troppo in
        /// alto si mangia i modelli dei controller.
        private void RelayerRayVisuals()
        {
            foreach (var v in FindObjectsByType<XRInteractorLineVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (v == null) continue;
                var lr = v.GetComponent<LineRenderer>();
                var go = lr != null ? lr.gameObject : v.gameObject;
                if (go.layer != hudLayer)
                {
                    Debug.Log($"[OVERLAY] layer HUD -> '{Path(go.transform)}' (visual del ray).");
                    go.layer = hudLayer;
                }
            }
        }

        /// I visual dei controller sono sottoalberi dedicati ("Left/Right Controller
        /// Visual" nel rig XRI standard): prenderli PER INTERO qui e' sicuro — contengono
        /// solo mesh del controller — a differenza della ricorsione cieca che ci e' gia'
        /// costata cara.
        private void RelayerControllerVisuals()
        {
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            foreach (var name in controllerVisualNames)
            {
                var t = FindDeepChild(origin.transform, name);
                if (t == null) continue;
                if (t.gameObject.layer != hudLayer)
                {
                    Debug.Log($"[OVERLAY] layer HUD -> sottoalbero '{Path(t)}' (visual controller).");
                    SetLayerRecursively(t, hudLayer);
                }
            }
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeepChild(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static void SetLayerRecursively(Transform t, int layer)
        {
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
        }

        // ------------------------------------------------------------------ diagnostica

        private void LogSetup(Camera main)
        {
            var sb = new StringBuilder("[OVERLAY] stato:\n");
            sb.AppendLine($"  Main '{main.name}'  culling {MaskToString(main.cullingMask)}");
            var stack = main.GetUniversalAdditionalCameraData().cameraStack;
            sb.AppendLine($"  Stack della Main: {stack.Count} camera(e)");
            foreach (var c in stack)
                sb.AppendLine($"    - {(c == null ? "(null)" : c.name)}  culling {(c == null ? "-" : MaskToString(c.cullingMask))}  " +
                              $"type {(c == null ? "-" : c.GetUniversalAdditionalCameraData().renderType.ToString())}  " +
                              $"clearDepth {(c == null ? "-" : c.GetUniversalAdditionalCameraData().clearDepth.ToString())}");
            var hud = VrHud.Instance;
            var canvas = hud != null ? hud.transform.Find("VrHudCanvas") : null;
            sb.AppendLine($"  Canvas HUD: {(canvas == null ? "ASSENTE" : "layer " + LayerMask.LayerToName(canvas.gameObject.layer))}");
            Debug.Log(sb.ToString());
        }

        private static string MaskToString(int mask)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0)
                { var n = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(n)) sb.Append(n).Append(' '); }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(vuota)";
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
