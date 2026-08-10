using System.Collections.Generic;
using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// 2.5D triangulated irregular network from scattered points (x,z,height). Delaunay in plan
    /// (Bowyer-Watson) over the XZ positions, keeping each point's Y as terrain elevation. Provides
    /// height sampling at any (x,z) by barycentric interpolation on the containing triangle; outside
    /// the convex hull it falls back to the nearest sample (flat extension), as agreed.
    /// Dependency-free; sized for tens of points.
    /// </summary>
    public sealed class Tin2D
    {
        public struct Tri { public int A, B, C; }

        private readonly List<Vector3> _pts = new List<Vector3>();   // x, y(=height), z
        private readonly List<Tri> _tris = new List<Tri>();

        public IReadOnlyList<Vector3> Points => _pts;
        public IReadOnlyList<Tri> Triangles => _tris;

        /// Build from world points (uses .x, .z for plan; .y for elevation).
        public void Build(IReadOnlyList<Vector3> worldPoints)
        {
            _pts.Clear(); _tris.Clear();
            if (worldPoints == null || worldPoints.Count < 3) { _pts.AddRange(worldPoints ?? new List<Vector3>()); return; }
            foreach (var p in worldPoints) _pts.Add(p);

            // Super-triangle enclosing all points (in XZ).
            Vector2 min = P2(_pts[0]), max = P2(_pts[0]);
            foreach (var p in _pts) { var q = P2(p); min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
            float dx = max.x - min.x, dz = max.y - min.y, d = Mathf.Max(dx, dz) * 10f + 10f;
            Vector2 c = (min + max) * 0.5f;
            int s0 = _pts.Count, s1 = s0 + 1, s2 = s0 + 2;
            _pts.Add(new Vector3(c.x - d, 0, c.y - d));
            _pts.Add(new Vector3(c.x,      0, c.y + d));
            _pts.Add(new Vector3(c.x + d, 0, c.y - d));

            var tris = new List<Tri> { new Tri { A = s0, B = s1, C = s2 } };

            for (int ip = 0; ip < s0; ip++)
            {
                Vector2 p = P2(_pts[ip]);
                var bad = new List<int>();
                for (int t = 0; t < tris.Count; t++)
                    if (InCircumcircle(p, tris[t])) bad.Add(t);

                // boundary of the polygonal hole
                var edges = new List<(int a, int b)>();
                foreach (int t in bad)
                {
                    var tr = tris[t];
                    AddEdge(edges, tr.A, tr.B); AddEdge(edges, tr.B, tr.C); AddEdge(edges, tr.C, tr.A);
                }
                for (int i = bad.Count - 1; i >= 0; i--) tris.RemoveAt(bad[i]);

                var counts = new Dictionary<(int, int), int>();
                foreach (var e in edges) { var k = Key(e.a, e.b); counts[k] = counts.TryGetValue(k, out var n) ? n + 1 : 1; }
                foreach (var e in edges)
                    if (counts[Key(e.a, e.b)] == 1)
                        tris.Add(new Tri { A = e.a, B = e.b, C = ip });
            }

            // drop triangles touching the super-triangle
            foreach (var t in tris)
                if (t.A < s0 && t.B < s0 && t.C < s0) _tris.Add(t);

            _pts.RemoveRange(s0, 3);
        }

        /// Interpolated terrain height at (x,z). Outside the hull: nearest point's height.
        public float SampleHeight(float x, float z)
        {
            if (_pts.Count == 0) return 0f;
            var p = new Vector2(x, z);
            foreach (var t in _tris)
            {
                Vector2 a = P2(_pts[t.A]), b = P2(_pts[t.B]), c = P2(_pts[t.C]);
                if (Bary(p, a, b, c, out float u, out float v, out float w))
                    return u * _pts[t.A].y + v * _pts[t.B].y + w * _pts[t.C].y;
            }
            // fallback: nearest sample
            float best = float.MaxValue; float h = _pts[0].y;
            foreach (var pt in _pts)
            {
                float dd = (pt.x - x) * (pt.x - x) + (pt.z - z) * (pt.z - z);
                if (dd < best) { best = dd; h = pt.y; }
            }
            return h;
        }

        // ---------- helpers ----------
        private static Vector2 P2(Vector3 v) => new Vector2(v.x, v.z);

        private bool InCircumcircle(Vector2 p, Tri t)
        {
            Vector2 a = P2(_pts[t.A]), b = P2(_pts[t.B]), c = P2(_pts[t.C]);
            float ax = a.x - p.x, ay = a.y - p.y;
            float bx = b.x - p.x, by = b.y - p.y;
            float cx = c.x - p.x, cy = c.y - p.y;
            float ab = ax * ax + ay * ay, bb = bx * bx + by * by, cb = cx * cx + cy * cy;
            float det = ax * (by * cb - bb * cy) - ay * (bx * cb - bb * cx) + ab * (bx * cy - by * cx);
            // det > 0 => inside, for CCW triangles; make orientation-independent
            float orient = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return orient < 0 ? det < 0 : det > 0;
        }

        private static bool Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-9f) { u = v = w = 0; return false; }
            u = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
            v = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
            w = 1f - u - v;
            return u >= -1e-4f && v >= -1e-4f && w >= -1e-4f;
        }

        private static void AddEdge(List<(int, int)> edges, int a, int b) => edges.Add((a, b));
        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
    }
}
