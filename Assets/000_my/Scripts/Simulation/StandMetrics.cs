using System.Collections.Generic;
using UnityEngine;
using Artemis.Inventory;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Dendrometric indices for a set of stems, expanded to the hectare on a nominal sample-area
    /// (400 m² for a 20x20 plot -> factor 25). DBH is stored in metres; basal area in m².
    /// </summary>
    public struct StandMetrics
    {
        public int   N;             // number of trees
        public float BasalArea;     // G, m²
        public float BasalAreaHa;   // G/ha, m²/ha
        public float DensityHa;     // N/ha
        public float MeanDbhCm;     // cm
        public float MeanHeightM;   // m

        public static StandMetrics Compute(IReadOnlyList<StemRecord> stems, float plotAreaM2)
        {
            var m = new StandMetrics();
            if (stems == null || stems.Count == 0) return m;

            float g = 0f, sumDbh = 0f, sumH = 0f;
            foreach (var s in stems)
            {
                float r = s.Dbh * 0.5f;             // metres
                g += Mathf.PI * r * r;               // basal area of this stem
                sumDbh += s.Dbh * 100f;              // cm
                sumH += s.Height;                    // m
            }

            m.N = stems.Count;
            m.BasalArea = g;
            float factor = plotAreaM2 > 0.001f ? 10000f / plotAreaM2 : 0f;   // per-hectare expansion
            m.BasalAreaHa = g * factor;
            m.DensityHa = m.N * factor;
            m.MeanDbhCm = sumDbh / m.N;
            m.MeanHeightM = sumH / m.N;
            return m;
        }
    }
}
