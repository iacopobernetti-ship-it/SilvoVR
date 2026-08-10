using System.Collections.Generic;
using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Voronoi cells in 2D (plan, XZ) by half-plane clipping (Sutherland-Hodgman). O(n^2) but
    /// trivial for tens of stems, dependency-free, always clipped to the plot rectangle so
    /// perimeter cells stay finite. Sites and polygons use Vector2 = (x, z).
    /// </summary>
    public static class Voronoi2D
    {
        public static List<List<Vector2>> Compute(IReadOnlyList<Vector2> sites, Vector2 center, float size)
        {
            var rect = RectPolygon(center, size);
            var cells = new List<List<Vector2>>(sites.Count);
            for (int i = 0; i < sites.Count; i++)
            {
                var poly = new List<Vector2>(rect);
                for (int j = 0; j < sites.Count && poly.Count > 0; j++)
                {
                    if (i == j) continue;
                    poly = ClipByBisector(poly, sites[i], sites[j]);
                }
                cells.Add(poly);
            }
            return cells;
        }

        private static List<Vector2> RectPolygon(Vector2 c, float s)
        {
            float h = s * 0.5f;
            return new List<Vector2> {
                new Vector2(c.x - h, c.y - h), new Vector2(c.x + h, c.y - h),
                new Vector2(c.x + h, c.y + h), new Vector2(c.x - h, c.y + h)
            };
        }

        private static List<Vector2> ClipByBisector(List<Vector2> poly, Vector2 keep, Vector2 other)
        {
            Vector2 dir = other - keep;
            Vector2 mid = (keep + other) * 0.5f;
            float d0 = Vector2.Dot(mid, dir);
            var outp = new List<Vector2>();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                float da = Vector2.Dot(a, dir) - d0;
                float db = Vector2.Dot(b, dir) - d0;
                bool ain = da <= 0f, bin = db <= 0f;
                if (ain) outp.Add(a);
                if (ain != bin) { float t = da / (da - db); outp.Add(a + (b - a) * t); }
            }
            return outp;
        }

        public static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
        {
            bool inside = false; int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            return inside;
        }

        public static float Area(IReadOnlyList<Vector2> poly)
        {
            float a = 0f; int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                a += (poly[j].x + poly[i].x) * (poly[j].y - poly[i].y);
            return Mathf.Abs(a) * 0.5f;
        }

        /// Minimum distance from p to the polygon edges (used to keep young off the borders).
        public static float DistanceToEdges(Vector2 p, IReadOnlyList<Vector2> poly)
        {
            float min = float.MaxValue; int n = poly.Count;
            for (int i = 0; i < n; i++)
                min = Mathf.Min(min, DistPointSegment(p, poly[i], poly[(i + 1) % n]));
            return min;
        }

        private static float DistPointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Vector2.Dot(p - a, ab) / Mathf.Max(Vector2.Dot(ab, ab), 1e-6f);
            t = Mathf.Clamp01(t);
            return Vector2.Distance(p, a + ab * t);
        }

        public static Vector2 Centroid(IReadOnlyList<Vector2> poly)
        {
            Vector2 c = Vector2.zero; int n = poly.Count;
            if (n == 0) return c;
            foreach (var v in poly) c += v;
            return c / n;
        }

        /// <summary>
        /// Adjacency between cells: two cells are neighbours if they share a border segment.
        /// Approximated robustly by testing whether any edge of A lies (collinear + overlapping)
        /// on any edge of B, within a small tolerance. Returns, for each cell index, the set of
        /// neighbouring cell indices.
        /// </summary>
        public static List<HashSet<int>> Adjacency(IReadOnlyList<List<Vector2>> cells, float tol = 0.05f)
        {
            int n = cells.Count;
            var adj = new List<HashSet<int>>(n);
            for (int i = 0; i < n; i++) adj.Add(new HashSet<int>());

            for (int i = 0; i < n; i++)
            {
                var ci = cells[i]; if (ci == null || ci.Count < 2) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var cj = cells[j]; if (cj == null || cj.Count < 2) continue;
                    if (SharesEdge(ci, cj, tol)) { adj[i].Add(j); adj[j].Add(i); }
                }
            }
            return adj;
        }

        private static bool SharesEdge(List<Vector2> a, List<Vector2> b, float tol)
        {
            int na = a.Count, nb = b.Count;
            for (int i = 0; i < na; i++)
            {
                Vector2 a0 = a[i], a1 = a[(i + 1) % na];
                for (int j = 0; j < nb; j++)
                {
                    Vector2 b0 = b[j], b1 = b[(j + 1) % nb];
                    // shared border: endpoints coincide with the opposite edge (either orientation)
                    if ((Close(a0, b1, tol) && Close(a1, b0, tol)) ||
                        (Close(a0, b0, tol) && Close(a1, b1, tol)))
                        return true;
                }
            }
            return false;
        }

        private static bool Close(Vector2 p, Vector2 q, float tol) => (p - q).sqrMagnitude <= tol * tol;
    }
}
