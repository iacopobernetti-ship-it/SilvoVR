// FuzzyInference.cs
// Mamdani fuzzy inference engine, pure C# (no UnityEngine dependency).
// Membership: Triangle, Trapezoid, Gaussian, Left/Right shoulder. AND = min,
// aggregation = max, centroid defuzzification. Returns the dominant limiting factor.
using System;
using System.Collections.Generic;

namespace Silvomuseo.Fuzzy
{
    public enum MfType { Triangle, Trapezoid, Gaussian, LeftShoulder, RightShoulder }

    public sealed class MembershipFunction
    {
        public MfType Type;
        public float A, B, C, D;
        public float Mean, Sigma;
        const float Eps = 1e-6f;

        public static MembershipFunction Triangle(float a, float b, float c)
            => new MembershipFunction { Type = MfType.Triangle, A = a, B = b, C = c };
        public static MembershipFunction Trapezoid(float a, float b, float c, float d)
            => new MembershipFunction { Type = MfType.Trapezoid, A = a, B = b, C = c, D = d };
        public static MembershipFunction Gaussian(float mean, float sigma)
            => new MembershipFunction { Type = MfType.Gaussian, Mean = mean, Sigma = sigma };
        public static MembershipFunction LeftShoulder(float a, float b)
            => new MembershipFunction { Type = MfType.LeftShoulder, A = a, B = b };
        public static MembershipFunction RightShoulder(float a, float b)
            => new MembershipFunction { Type = MfType.RightShoulder, A = a, B = b };

        public float Evaluate(float x)
        {
            switch (Type)
            {
                case MfType.Triangle:
                    if (x <= A || x >= C) return 0f;
                    if (x <= B) return (B - A) < Eps ? 1f : (x - A) / (B - A);
                    return (C - B) < Eps ? 1f : (C - x) / (C - B);
                case MfType.Trapezoid:
                    if (x <= A || x >= D) return 0f;
                    if (x >= B && x <= C) return 1f;
                    if (x < B) return (B - A) < Eps ? 1f : (x - A) / (B - A);
                    return (D - C) < Eps ? 1f : (D - x) / (D - C);
                case MfType.Gaussian:
                    {
                        float d = x - Mean; float s = Sigma < Eps ? Eps : Sigma;
                        return (float)Math.Exp(-(d * d) / (2f * s * s));
                    }
                case MfType.LeftShoulder:
                    if (x <= A) return 1f;
                    if (x >= B) return 0f;
                    return (B - A) < Eps ? 1f : (B - x) / (B - A);
                case MfType.RightShoulder:
                    if (x <= A) return 0f;
                    if (x >= B) return 1f;
                    return (B - A) < Eps ? 1f : (x - A) / (B - A);
                default: return 0f;
            }
        }
    }

    public sealed class FuzzySet
    {
        public readonly string Term;
        public readonly MembershipFunction Mf;
        public float Cache;
        public FuzzySet(string term, MembershipFunction mf) { Term = term; Mf = mf; }
    }

    public sealed class FuzzyVariable
    {
        public readonly string Name;
        public readonly float Min, Max;
        readonly List<FuzzySet> _sets = new List<FuzzySet>();
        readonly Dictionary<string, FuzzySet> _index = new Dictionary<string, FuzzySet>();

        public FuzzyVariable(string name, float min, float max) { Name = name; Min = min; Max = max; }
        public FuzzyVariable Add(string term, MembershipFunction mf)
        {
            var s = new FuzzySet(term, mf); _sets.Add(s); _index[term] = s; return this;
        }
        public IReadOnlyList<FuzzySet> Sets => _sets;
        public void Refresh(float x) { for (int i = 0; i < _sets.Count; i++) _sets[i].Cache = _sets[i].Mf.Evaluate(x); }
        public float Mu(string term) => _index[term].Cache;
        public MembershipFunction Mf(string term) => _index[term].Mf;
    }

    public sealed class Rule
    {
        public readonly List<(string var, string term)> Antecedents = new List<(string, string)>();
        public string OutVar, OutTerm;
        public float Weight = 1f;
        public string Category = "";
        public Rule If(string var, string term) { Antecedents.Add((var, term)); return this; }
        public Rule Then(string outVar, string outTerm) { OutVar = outVar; OutTerm = outTerm; return this; }
        public Rule WithWeight(float w) { Weight = w; return this; }
        public Rule Tag(string category) { Category = category; return this; }
    }

    public struct InferenceResult
    {
        public float Value;
        public string Limiting;
        public float LimitingStrength;
        public string DominantTerm;
    }

    public sealed class FuzzySystem
    {
        readonly Dictionary<string, FuzzyVariable> _inputs = new Dictionary<string, FuzzyVariable>();
        FuzzyVariable _output;
        readonly List<Rule> _rules = new List<Rule>();
        int _resolution = 101;
        float[] _agg;
        readonly Dictionary<string, float[]> _outSamples = new Dictionary<string, float[]>();
        float _lo, _hi, _step;

        public FuzzySystem AddInput(FuzzyVariable v) { _inputs[v.Name] = v; return this; }
        public FuzzySystem SetOutput(FuzzyVariable v) { _output = v; return this; }
        public FuzzySystem AddRule(Rule r) { _rules.Add(r); return this; }
        public FuzzySystem SetResolution(int n) { _resolution = Math.Max(11, n); return this; }

        public FuzzySystem Prepare()
        {
            _lo = _output.Min; _hi = _output.Max;
            _step = (_hi - _lo) / (_resolution - 1);
            _agg = new float[_resolution];
            _outSamples.Clear();
            foreach (var s in _output.Sets)
            {
                var arr = new float[_resolution];
                for (int i = 0; i < _resolution; i++) arr[i] = s.Mf.Evaluate(_lo + _step * i);
                _outSamples[s.Term] = arr;
            }
            return this;
        }

        public InferenceResult Evaluate(IDictionary<string, float> crisp)
        {
            if (_agg == null) Prepare();
            Array.Clear(_agg, 0, _resolution);
            foreach (var kv in _inputs)
                kv.Value.Refresh(crisp.TryGetValue(kv.Key, out var x) ? x : 0f);

            float bestCauseStrength = 0f; string bestCause = "None";
            float bestAny = 0f; string bestTerm = null;

            for (int r = 0; r < _rules.Count; r++)
            {
                var rule = _rules[r];
                var ante = rule.Antecedents;

                // AND = min over the antecedents, THEN scaled by the rule weight.
                //
                // The weight used to be folded into the min (strength = min(weight, mu...)), which
                // makes it a CEILING rather than a scale factor: with weight 0.6 and membership
                // 0.85 the firing strength stayed at 0.6 however the membership moved, so the rule
                // lost all gradation exactly where it was supposed to provide it. That is one of
                // the reasons the regeneration model returned a constant suitability regardless of
                // climate scenario. Multiplying instead preserves the shape of the membership and
                // is the usual convention for weighted Mamdani rules.
                float mu = 1f;
                for (int a = 0; a < ante.Count; a++)
                {
                    float m = _inputs[ante[a].var].Mu(ante[a].term);
                    if (m < mu) mu = m;
                    if (mu <= 0f) break;
                }
                if (mu <= 0f) continue;

                float strength = mu * rule.Weight;
                if (strength <= 0f) continue;

                var samples = _outSamples[rule.OutTerm];
                for (int i = 0; i < _resolution; i++)
                {
                    float clipped = samples[i] < strength ? samples[i] : strength;
                    if (clipped > _agg[i]) _agg[i] = clipped;
                }
                if (strength > bestAny) { bestAny = strength; bestTerm = rule.OutTerm; }
                if (!string.IsNullOrEmpty(rule.Category) && rule.Category != "Favorable" && strength > bestCauseStrength)
                { bestCauseStrength = strength; bestCause = rule.Category; }
            }

            float num = 0f, den = 0f;
            for (int i = 0; i < _resolution; i++)
            {
                float w = _agg[i]; if (w <= 0f) continue;
                num += (_lo + _step * i) * w; den += w;
            }
            return new InferenceResult
            {
                Value = den > 1e-6f ? num / den : _lo,
                Limiting = bestCauseStrength > 0.05f ? bestCause : "None",
                LimitingStrength = bestCauseStrength,
                DominantTerm = bestTerm ?? "poor"
            };
        }
    }
}