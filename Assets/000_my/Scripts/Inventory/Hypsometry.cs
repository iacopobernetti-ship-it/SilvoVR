using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>
    /// Height-diameter (hypsometric) curve. Height is DERIVED from DBH, not measured.
    /// Petterson form:  H = 1.30 + ( D / (a + b*D) )^2 = 1.30 + D^2 / (a + b*D)^2
    /// with a = 3.02, b = 0.1172, D in centimetres, H in metres.
    /// </summary>
    public static class Hypsometry
    {
        private const float A = 3.02f;
        private const float B = 0.1172f;

        /// <param name="dbhMeters">DBH in metres (as stored on StemRecord).</param>
        /// <returns>Estimated total height in metres.</returns>
        public static float Height(float dbhMeters)
        {
            float d = dbhMeters * 100f;             // -> cm
            float denom = A + B * d;
            if (denom <= 0f) return 1.30f;
            return 1.30f + (d * d) / (denom * denom);
        }
    }
}
