using System.Collections.Generic;
using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>
    /// Estimates DBH by sampling the trunk cross-section on the fused mesh at breast height.
    /// Rays are cast INWARD from a ring around the estimated axis (they strike outer front
    /// faces reliably), then a circle is fitted to the hit points.
    /// </summary>
    public static class TrunkSampler
    {
        public struct DbhResult
        {
            public bool          Ok;
            public float         Dbh;
            public float         Radius;
            public Vector3       Center;
            public List<Vector3> Samples;
        }

        public static DbhResult MeasureDbh(
            Vector3 stemBase, LayerMask meshLayer,
            float breastHeight = 1.30f, int rayCount = 24, float ringRadius = 0.75f,
            float minDbh = 0.02f, float maxDbh = 1.50f)
        {
            var result = new DbhResult { Ok = false, Samples = new List<Vector3>() };
            float y = stemBase.y + breastHeight;
            Vector3 center = new Vector3(stemBase.x, y, stemBase.z);
            var pts2d = new List<Vector2>(rayCount);

            for (int i = 0; i < rayCount; i++)
            {
                float a = Mathf.PI * 2f * i / rayCount;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 origin = center + dir * ringRadius;
                if (Physics.Raycast(origin, -dir, out var hit, ringRadius * 1.2f, meshLayer))
                {
                    Vector2 rel = new Vector2(hit.point.x - stemBase.x, hit.point.z - stemBase.z);
                    if (rel.magnitude <= maxDbh)
                    {
                        pts2d.Add(rel);
                        result.Samples.Add(new Vector3(hit.point.x, y, hit.point.z));
                    }
                }
            }

            if (pts2d.Count < 5) return result;
            var fit = CircleFit.Fit(pts2d);
            if (!fit.Ok) return result;

            float dbh = fit.Radius * 2f;
            if (dbh < minDbh || dbh > maxDbh) return result;

            result.Ok=true; result.Dbh=dbh; result.Radius=fit.Radius;
            result.Center=new Vector3(stemBase.x+fit.Center.x, y, stemBase.z+fit.Center.y);
            return result;
        }
    }
}
