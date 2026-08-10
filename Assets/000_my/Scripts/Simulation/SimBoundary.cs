using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Muri invisibili attorno al suolo della simulazione: impediscono di uscire dal quadrato
    /// generato e cadere nel vuoto. Nella simulazione il terreno e' un piano finito costruito a
    /// runtime, quindi il bordo e' un burrone — e la locomozione continua non si ferma da sola.
    ///
    /// Si dimensionano da soli su StandBuilder (centro e lato del quadrato) e si rifanno a ogni
    /// ricostruzione, perche' il lato cambia con il numero di alberi dell'inventario.
    ///
    /// Da mettere su un GameObject della scena Simulation (per esempio lo stesso di SimMarkTool).
    /// Nelle aree gaussiane i muri si mettono a mano in scena: li' il terreno e' un asset e il
    /// suo bordo non cambia mai.
    /// </summary>
    public class SimBoundary : MonoBehaviour
    {
        [SerializeField] private StandBuilder builder;
        [Tooltip("Altezza dei muri (m). Devono superare qualunque salto o teletrasporto verticale.")]
        [SerializeField] private float wallHeight = 6f;
        [Tooltip("Spessore (m): generoso, cosi' nessuno puo' attraversarli in un solo passo di fisica.")]
        [SerializeField] private float wallThickness = 1f;
        [Tooltip("Quanto rientrare rispetto al bordo del quadrato (m). Un piccolo margine evita " +
                 "di restare in equilibrio sull'orlo con mezzo piede nel vuoto.")]
        [SerializeField] private float inset = 0.25f;
        [Tooltip("Rende i muri visibili: utile solo per capire dove sono durante la messa a punto.")]
        [SerializeField] private bool visibleForDebug = false;

        private Transform root;

        private void Start()
        {
            if (builder == null) builder = FindFirstObjectByType<StandBuilder>();
            if (builder == null) { enabled = false; return; }
            builder.OnRebuilt += Rebuild;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (builder != null) builder.OnRebuilt -= Rebuild;
        }

        private void Rebuild()
        {
            if (root != null) Destroy(root.gameObject);
            if (builder == null) return;

            var rootGo = new GameObject("SimBoundary");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root = rootGo.transform;

            Vector2 c = builder.SquareCenter;
            float side = Mathf.Max(builder.SquareSide - inset * 2f, 1f);
            float h = side * 0.5f;
            float y = builder.GroundY + wallHeight * 0.5f;

            // Quattro pareti: nord, sud, est, ovest.
            Wall(new Vector3(c.x, y, c.y + h), new Vector3(side + wallThickness, wallHeight, wallThickness), "N");
            Wall(new Vector3(c.x, y, c.y - h), new Vector3(side + wallThickness, wallHeight, wallThickness), "S");
            Wall(new Vector3(c.x + h, y, c.y), new Vector3(wallThickness, wallHeight, side + wallThickness), "E");
            Wall(new Vector3(c.x - h, y, c.y), new Vector3(wallThickness, wallHeight, side + wallThickness), "W");
        }

        private void Wall(Vector3 center, Vector3 size, string name)
        {
            var go = new GameObject($"Wall_{name}");
            go.transform.SetParent(root, false);
            go.transform.position = center;

            var box = go.AddComponent<BoxCollider>();
            box.size = size;

            if (!visibleForDebug) return;

            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = vis.GetComponent<Collider>(); if (col != null) Destroy(col);
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = size;
            var r = vis.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial =
                Artemis.Inventory.StemMarkerSpawner.MakeUnlit(new Color(1f, 0.3f, 0.3f, 1f), "M_Wall");
        }
    }
}
