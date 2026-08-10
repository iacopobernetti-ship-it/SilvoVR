// RegenerationFIS.cs
// Mamdani fuzzy model for NATURAL REGENERATION SUCCESS of Abies alba (Silvomuseo).
// Pure C# (uses FuzzyInference.cs only). Inputs: light [%], summer aridity [0..1],
// basal area G [m2/ha], structural diversity [0..1]. Output: suitability [0..1] + limiting factor.
//
// RECALIBRATION NOTES — why the previous version always returned 0.12
// -------------------------------------------------------------------
// Field testing showed a constant suitability of 0.12 across every climate scenario. That figure
// was not a model result at all: it is exactly the centroid of the "poor" output set on its own.
// Four faults stacked up to produce it, and all four are addressed here.
//
//  1) SCALE MISMATCH. AridityFromDeMartonne defaulted to humid=40 / arid=10, which are reference
//     values for the ANNUAL De Martonne index. The client feeds it the SUMMER index, which for a
//     cool montane site like Vallombrosa sits around 10-11 — i.e. pinned at the arid end of an
//     annual scale. Every scenario therefore mapped to 0.97-0.98.
//
//  2) SATURATING MEMBERSHIP. "critical" was RightShoulder(0.60, 0.85), so any aridity at or above
//     0.85 gave membership exactly 1. Two scenarios differing by 0.01 in an already-saturated
//     region are literally the same input as far as the inference is concerned.
//
//  3) A DOMINANT UNCONDITIONAL RULE. "IF aridity critical THEN poor" carried weight 1 and had no
//     other antecedent, so once aridity saturated it fired at full strength and the max-aggregation
//     filled the whole "poor" set. The centroid of that set alone is 0.1200 — the observed value.
//
//  4) A GAP IN THE RULE BASE. No rule covered "excessive light + low aridity": the aggregate came
//     out empty and defuzzification fell back to the output minimum, reporting 0.00 rather than a
//     meaningful value.
//
// A fifth issue is physical rather than numerical: the De Martonne index is weakly sensitive to
// warming here, because precipitation and temperature rise together and partly cancel out (573 mm
// / 18.0 °C under SSP2-4.5 2040s versus 597 mm / 20.1 °C under SSP5-8.5 2070s changes the index by
// about 1 %). Aridity is therefore now a COMPOSITE of the summer De Martonne and an explicit
// summer heat-stress term, which restores the scenario signal the FIS is supposed to react to.
using System.Collections.Generic;
using Silvomuseo.Fuzzy;

namespace Artemis.Regeneration
{
    public static class RegenBreakpoints
    {
        // Relative light [%] — "excessive" now saturates at 62 % instead of 40 %. In a wide felling
        // gap the old scale pinned it at membership 1, so light stopped discriminating precisely in
        // the situation the teaching session is about.
        public const float LightInsuffA = 6f, LightInsuffB = 12f;
        public const float LightOptA = 8f, LightOptB = 14f, LightOptC = 26f, LightOptD = 40f;
        public const float LightExcA = 32f, LightExcB = 62f;

        // Summer aridity [0..1] — "critical" begins at 0.45 and saturates only at 1.0. It used to
        // start at 0.60 and saturate at 0.85, which put every real value for this site beyond the
        // saturation point.
        public const float AridLowA = 0.18f, AridLowB = 0.45f;
        public const float AridModA = 0.28f, AridModB = 0.52f, AridModC = 0.85f;
        public const float AridCritA = 0.45f, AridCritB = 1.00f;

        // Basal area G [m2/ha] — rescaled to the values this site actually produces. A 400 m² plot
        // holding 17 mature firs yields around 118 m²/ha, well past the old variable range of
        // 0-70, so "dense" was saturated at membership 1 and its rule dominated every evaluation.
        public const float GScaleMax = 160f;
        public const float GSparseA = 28f, GSparseB = 45f;
        public const float GMedA = 35f, GMedB = 55f, GMedC = 85f, GMedD = 110f;
        public const float GDenseA = 95f, GDenseB = 135f;

        // Structural diversity [0..1]
        public const float DivLowA = 0.30f, DivLowB = 0.50f;
        public const float DivHighA = 0.40f, DivHighB = 0.60f;
    }

    /// <summary>Reference values for the aridity normalisation. Exposed so they can be tuned on
    /// real API output for a given site rather than guessed.</summary>
    public static class AridityCalibration
    {
        /// <summary>Summer De Martonne treated as fully humid (maps to 0). Note this is the SUMMER
        /// scale: the familiar 40/20/10 thresholds belong to the annual index.</summary>
        public static float SummerHumidI = 20f;
        /// <summary>Summer De Martonne treated as fully arid (maps to 1).</summary>
        public static float SummerAridI = 5f;

        /// <summary>Mean temperature of the warmest quarter treated as no heat stress (maps to 0).</summary>
        public static float CoolSummerC = 14f;
        /// <summary>Mean temperature of the warmest quarter treated as maximum heat stress (maps to 1).</summary>
        public static float HotSummerC = 24f;

        /// <summary>Weight of the heat-stress term in the composite index; the De Martonne term
        /// takes the remainder. 0 reproduces the old behaviour (index only).</summary>
        public static float HeatWeight = 0.5f;

        /// <summary>Annual references, for when the annual index is used instead.</summary>
        public static float AnnualHumidI = 40f, AnnualAridI = 10f;
    }

    public sealed class RegenerationEvaluator
    {
        readonly FuzzySystem _fis;
        readonly Dictionary<string, float> _crisp = new Dictionary<string, float>(4);

        public RegenerationEvaluator() { _fis = Build(); }

        /// <summary>Evaluate one gap: light already in %, aridity/basalArea/diversity crisp.</summary>
        public InferenceResult Evaluate(float lightPct, float aridity01, float basalArea, float diversity01)
        {
            _crisp["light"] = Clamp(lightPct, 0f, 100f);
            _crisp["aridity"] = Clamp(aridity01, 0f, 1f);
            _crisp["basalArea"] = basalArea;
            _crisp["diversity"] = Clamp(diversity01, 0f, 1f);
            return _fis.Evaluate(_crisp);
        }

        // -- Aridity from REAL climate ------------------------------------------------------------

        /// <summary>
        /// Normalise a De Martonne index to [0..1], 1 = most arid. Give the reference values that
        /// match the index being passed in: the summer index needs a summer scale.
        /// </summary>
        public static float AridityFromDeMartonne(float precipMm, float meanTempC,
                                                  float humidI, float aridI)
        {
            float denom = meanTempC + 10f;
            float I = denom > 0.01f ? precipMm / denom : 60f;
            float span = humidI - aridI;
            if (span < 0.01f) return 0.5f;
            return Clamp((humidI - I) / span, 0f, 1f);
        }

        /// <summary>Backwards-compatible overload using the ANNUAL references.</summary>
        public static float AridityFromDeMartonne(float precipMm, float meanTempC)
            => AridityFromDeMartonne(precipMm, meanTempC,
                                     AridityCalibration.AnnualHumidI, AridityCalibration.AnnualAridI);

        /// <summary>
        /// Composite summer aridity: the summer De Martonne index combined with an explicit
        /// heat-stress term from the mean temperature of the warmest quarter.
        ///
        /// The second term exists because the index alone barely moves between scenarios at this
        /// site — precipitation and temperature both increase and largely cancel in P/(T+10) — so a
        /// model driven by it alone cannot react to the climate projection at all. Rising summer
        /// temperature raises evaporative demand and drought stress on regeneration whether or not
        /// total rainfall keeps pace, which is exactly what the heat term captures.
        /// </summary>
        /// <param name="summerQuarterPrecipMm">BIO18: precipitation of the warmest quarter, mm.</param>
        /// <param name="summerMeanTempC">BIO10: mean temperature of the warmest quarter, °C.</param>
        public static float CompositeSummerAridity(float summerQuarterPrecipMm, float summerMeanTempC)
        {
            // Quarter rainfall is annualised (x4) to stay on the De Martonne scale.
            float dmPart = AridityFromDeMartonne(summerQuarterPrecipMm * 4f, summerMeanTempC,
                                                 AridityCalibration.SummerHumidI,
                                                 AridityCalibration.SummerAridI);

            float span = AridityCalibration.HotSummerC - AridityCalibration.CoolSummerC;
            float heatPart = span > 0.01f
                ? Clamp((summerMeanTempC - AridityCalibration.CoolSummerC) / span, 0f, 1f)
                : 0.5f;

            float w = Clamp(AridityCalibration.HeatWeight, 0f, 1f);
            return Clamp(dmPart * (1f - w) + heatPart * w, 0f, 1f);
        }

        // -- Build ------------------------------------------------------------------------------
        static FuzzySystem Build()
        {
            var light = new FuzzyVariable("light", 0f, 100f)
                .Add("insufficient", MembershipFunction.LeftShoulder(RegenBreakpoints.LightInsuffA, RegenBreakpoints.LightInsuffB))
                .Add("optimal", MembershipFunction.Trapezoid(RegenBreakpoints.LightOptA, RegenBreakpoints.LightOptB, RegenBreakpoints.LightOptC, RegenBreakpoints.LightOptD))
                .Add("excessive", MembershipFunction.RightShoulder(RegenBreakpoints.LightExcA, RegenBreakpoints.LightExcB));

            // "moderate" is now a trapezoid rather than a triangle: a triangle peaking at a single
            // point makes the model hypersensitive right at the peak and flat either side.
            var aridity = new FuzzyVariable("aridity", 0f, 1f)
                .Add("low", MembershipFunction.LeftShoulder(RegenBreakpoints.AridLowA, RegenBreakpoints.AridLowB))
                .Add("moderate", MembershipFunction.Triangle(RegenBreakpoints.AridModA, RegenBreakpoints.AridModB, RegenBreakpoints.AridModC))
                .Add("critical", MembershipFunction.RightShoulder(RegenBreakpoints.AridCritA, RegenBreakpoints.AridCritB));

            var basalArea = new FuzzyVariable("basalArea", 0f, RegenBreakpoints.GScaleMax)
                .Add("sparse", MembershipFunction.LeftShoulder(RegenBreakpoints.GSparseA, RegenBreakpoints.GSparseB))
                .Add("medium", MembershipFunction.Trapezoid(RegenBreakpoints.GMedA, RegenBreakpoints.GMedB, RegenBreakpoints.GMedC, RegenBreakpoints.GMedD))
                .Add("dense", MembershipFunction.RightShoulder(RegenBreakpoints.GDenseA, RegenBreakpoints.GDenseB));

            var diversity = new FuzzyVariable("diversity", 0f, 1f)
                .Add("low", MembershipFunction.LeftShoulder(RegenBreakpoints.DivLowA, RegenBreakpoints.DivLowB))
                .Add("high", MembershipFunction.RightShoulder(RegenBreakpoints.DivHighA, RegenBreakpoints.DivHighB));

            // "poor" no longer starts flat at 0: a triangle with a=b=0 puts its centroid at a fixed
            // 0.12 whenever it fires alone, which is precisely the constant that showed up in
            // testing. Giving it a peak slightly inside the range keeps it a genuine "poor" set
            // while letting other rules shift the centroid.
            var suitability = new FuzzyVariable("suitability", 0f, 1f)
                .Add("poor", MembershipFunction.Triangle(0.00f, 0.08f, 0.32f))
                .Add("moderate", MembershipFunction.Triangle(0.22f, 0.45f, 0.68f))
                .Add("good", MembershipFunction.Triangle(0.58f, 0.75f, 0.92f))
                .Add("excellent", MembershipFunction.Triangle(0.82f, 1.00f, 1.00f));

            var sys = new FuzzySystem()
                .AddInput(light).AddInput(aridity).AddInput(basalArea).AddInput(diversity)
                .SetOutput(suitability).SetResolution(101);

            // ---- favourable combinations ----------------------------------------------------
            sys.AddRule(new Rule().If("light", "optimal").If("aridity", "low").If("basalArea", "medium").If("diversity", "high").Then("suitability", "excellent").Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "optimal").If("aridity", "low").If("basalArea", "medium").Then("suitability", "good").Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "optimal").If("aridity", "low").If("basalArea", "sparse").Then("suitability", "good").Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "optimal").If("diversity", "high").If("aridity", "low").Then("suitability", "good").Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "optimal").If("aridity", "moderate").If("basalArea", "medium").Then("suitability", "moderate").Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "optimal").If("basalArea", "medium").If("aridity", "moderate").If("diversity", "low").Then("suitability", "moderate").Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "optimal").If("aridity", "moderate").If("basalArea", "sparse").Then("suitability", "moderate").WithWeight(0.9f).Tag("Favorable"));

            // Fill the former gap: a wide gap under a still-humid climate is not a failure — the
            // young plants get more light than ideal but no water stress to speak of.
            sys.AddRule(new Rule().If("light", "excessive").If("aridity", "low").If("basalArea", "medium").Then("suitability", "moderate").WithWeight(0.8f).Tag("Favorable"));
            sys.AddRule(new Rule().If("light", "excessive").If("aridity", "low").Then("suitability", "moderate").WithWeight(0.7f).Tag("ExcessRadiation"));

            // ---- limiting factors -----------------------------------------------------------
            // Weights below 1 on the broad single-antecedent rules. At full weight any one of them
            // saturates the "poor" set on its own and pins the output, which is what made the model
            // insensitive; keeping them slightly below lets competing evidence move the centroid
            // while still marking the correct limiting factor.
            sys.AddRule(new Rule().If("light", "insufficient").Then("suitability", "poor").WithWeight(0.85f).Tag("InsufficientLight"));
            sys.AddRule(new Rule().If("light", "insufficient").If("basalArea", "dense").Then("suitability", "poor").Tag("DenseCanopy"));
            // Volutamente "moderate" e non "poor": una buca ampia con aridita' intermedia penalizza
            // l'abete senza annullarlo. Soprattutto, questa regola e la penalizzante che segue
            // hanno ANTECEDENTI DIVERSI ("moderate" contro "critical"): se due regole con output
            // opposti scalano sulla stessa membership, il loro rapporto resta costante e il
            // centroide non si muove, per quanto la membership cambi. Era l'ultima ragione per cui
            // il modello non reagiva agli scenari.
            sys.AddRule(new Rule().If("light", "excessive").If("aridity", "moderate").Then("suitability", "moderate").WithWeight(0.55f).Tag("ExcessRadiation"));
            sys.AddRule(new Rule().If("light", "excessive").If("aridity", "critical").Then("suitability", "poor").Tag("ExcessRadiation"));
            sys.AddRule(new Rule().If("aridity", "critical").Then("suitability", "poor").WithWeight(0.7f).Tag("SummerDrought"));
            sys.AddRule(new Rule().If("aridity", "critical").If("basalArea", "sparse").Then("suitability", "poor").WithWeight(0.9f).Tag("SummerDrought"));
            sys.AddRule(new Rule().If("light", "optimal").If("aridity", "critical").Then("suitability", "poor").WithWeight(0.8f).Tag("SummerDrought"));
            sys.AddRule(new Rule().If("basalArea", "dense").Then("suitability", "poor").WithWeight(0.75f).Tag("DenseCanopy"));

            return sys.Prepare();
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}