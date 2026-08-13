using UnityEngine;
using UnityEngine.UI;
using Artemis.Vr;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Scheda "Map": vista dall'alto del soprassuolo, resa da una camera ortografica in una
    /// RenderTexture mostrata dentro la HUD. Erede della MiniMap desktop, con una differenza
    /// sostanziale: li' era un riquadro fisso nell'angolo dello schermo — concetto che in VR non
    /// esiste — qui e' una scheda che si apre quando serve, e occupa tutto il pannello.
    ///
    /// Mostra il suolo reale della simulazione, le celle di Voronoi (su un layer dedicato che la
    /// camera principale non vede) e un puntino per il giocatore, cosi' ci si orienta in un
    /// popolamento dove tutti gli alberi si somigliano.
    ///
    /// Richiede il layer "MiniMap" in Tags and Layers.
    /// </summary>
    public class MapPanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Map";
        [SerializeField] private string miniMapLayerName = "MiniMap";
        [Tooltip("Layer che la mappa deve vedere OLTRE al proprio (il suolo della simulazione).")]
        [SerializeField] private LayerMask alsoRender = ~0;

        [Header("Resa")]
        [SerializeField] private int textureSize = 512;
        [SerializeField] private float cameraHeight = 200f;
        [SerializeField] private Color background = new Color(0.10f, 0.12f, 0.10f);
        [SerializeField] private Color cellColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private float cellLineWidth = 0.12f;
        [SerializeField] private Color playerBlip = new Color(1f, 0.95f, 0.30f);
        [SerializeField] private float blipSize = 1.5f;
        [Tooltip("Capovolge la mappa in verticale: su alcune API grafiche la RenderTexture arriva " +
                 "rovesciata, il che scambia nord e sud lasciando corretti est e ovest.")]
        [SerializeField] private bool flipNorthSouth = true;

        private bool built;
        private StandBuilder builder;
        private Camera mapCam;
        private RenderTexture rt;
        private Transform cellRoot;
        private GameObject blip;
        private int miniLayer = -2;
        private Material lineMat, blipMat;
        private Transform head;

        // ---- ciclo di vita ---------------------------------------------------------------------

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            FrameCamera();
            UpdateBlip();
        }

        private void OnDestroy()
        {
            if (builder != null) builder.OnRebuilt -= RebuildCells;
            if (rt != null) rt.Release();
        }

        private void TryBuild()
        {
            var hud = VrHud.Instance;
            if (hud == null) return;

            builder = FindFirstObjectByType<StandBuilder>();
            if (builder == null) { enabled = false; return; }    // solo nella simulazione
            if (!Artemis.Session.VrSession.WorkAllowed) return;  // attende la sessione

            miniLayer = LayerMask.NameToLayer(miniMapLayerName);
            if (miniLayer < 0)
            {
                Debug.LogError($"[MapPanel] layer '{miniMapLayerName}' inesistente: crealo in " +
                               "Project Settings → Tags and Layers.");
                enabled = false; return;
            }

            lineMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(cellColor, "M_MapCell");
            blipMat = Artemis.Inventory.StemMarkerSpawner.MakeUnlit(playerBlip, "M_MapBlip");

            // La camera principale non deve mai vedere la geometria di servizio della mappa.
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << miniLayer);

            BuildCamera();
            BuildTab(hud);

            builder.OnRebuilt += RebuildCells;
            RebuildCells();
            built = true;
        }

        private void BuildCamera()
        {
            rt = new RenderTexture(textureSize, textureSize, 16) { name = "MapRT" };

            var go = new GameObject("MapCamera");
            go.transform.SetParent(transform, false);
            mapCam = go.AddComponent<Camera>();
            mapCam.orthographic = true;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = background;
            mapCam.cullingMask = (1 << miniLayer) | alsoRender.value;
            mapCam.targetTexture = rt;
            mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // dritta verso il basso
        }

        private void BuildTab(VrHud hud)
        {
            var page = hud.CreateTab(tabTitle);

            var holder = new GameObject("MapImage", typeof(RectTransform), typeof(RawImage));
            holder.transform.SetParent(page, false);
            var raw = holder.GetComponent<RawImage>();
            raw.texture = rt;
            raw.raycastTarget = false;
            raw.uvRect = flipNorthSouth ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);

            var le = holder.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.flexibleHeight = 1;
            le.minHeight = 160f;

            hud.MakeLabel(page, "white outlines = Voronoi cells  ·  yellow dot = you", 14);
        }

        // ---- inquadratura e puntino -----------------------------------------------------------------

        private void FrameCamera()
        {
            if (mapCam == null || builder == null) return;
            Vector2 c = builder.SquareCenter;
            float side = Mathf.Max(builder.SquareSide, 1f);
            mapCam.transform.position = new Vector3(c.x, builder.GroundY + cameraHeight, c.y);
            mapCam.orthographicSize = side * 0.5f;
            mapCam.nearClipPlane = 0.1f;
            mapCam.farClipPlane = cameraHeight + 50f;
        }

        private void UpdateBlip()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                head = cam.transform;
            }

            if (blip == null)
            {
                blip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                blip.name = "MapBlip";
                var col = blip.GetComponent<Collider>(); if (col != null) Destroy(col);
                blip.transform.SetParent(transform, false);
                blip.layer = miniLayer;
                var r = blip.GetComponent<Renderer>();
                if (r != null && blipMat != null) r.sharedMaterial = blipMat;
            }

            float y = builder != null ? builder.GroundY + 1f : 1f;
            blip.transform.position = new Vector3(head.position.x, y, head.position.z);
            blip.transform.localScale = Vector3.one * blipSize;
        }

        // ---- celle di Voronoi -------------------------------------------------------------------------

        private void RebuildCells()
        {
            if (cellRoot != null) Destroy(cellRoot.gameObject);
            if (builder == null || builder.Cells == null) return;

            var rootGo = new GameObject("MapCells");
            rootGo.transform.SetParent(transform, false);
            cellRoot = rootGo.transform;

            float y = builder.GroundY + 0.10f;
            foreach (var cell in builder.Cells)
            {
                if (cell == null || cell.Count < 2) continue;
                var go = new GameObject("CellLine");
                go.transform.SetParent(cellRoot, false);
                go.layer = miniLayer;

                var lr = go.AddComponent<LineRenderer>();
                lr.material = lineMat;
                lr.startColor = lr.endColor = cellColor;
                lr.widthMultiplier = cellLineWidth;
                lr.loop = true; lr.useWorldSpace = true;
                lr.positionCount = cell.Count;
                for (int i = 0; i < cell.Count; i++)
                    lr.SetPosition(i, new Vector3(cell[i].x, y, cell[i].y));
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }
        }
    }
}
