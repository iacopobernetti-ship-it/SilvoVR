using System;
using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>
    /// Catalog of the 10 silver-fir models, each with an adult and a young (regeneration)
    /// variant. Selection is deterministic by StemId so reconstructions are repeatable.
    ///
    /// Three per-model values live here rather than in StandBuilder, because they describe the
    /// PREFAB, not the simulation: how deep each variant sits in the soil, and how far the trunk
    /// of that particular model wanders from vertical.
    /// </summary>
    [CreateAssetMenu(menuName = "Artemis/Plant Catalog", fileName = "PlantCatalog")]
    public class PlantCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string name = "Abies alba";
            public GameObject adult;   // mature tree
            public GameObject young;   // young regeneration plant (used in the felling step)

            [Tooltip("How deep the root collar of the ADULT sits BELOW the soil surface, in metres " +
                     "of the model at native scale. It is scaled with the tree, so it stays " +
                     "proportional.")]
            public float sinkDepth = 0f;

            [Tooltip("Same, for the YOUNG plant. Kept separate because a seedling's root system is " +
                     "far shallower than a mature tree's: sinking it as deep as an adult buries it " +
                     "in the ground. Leave at -1 to fall back on the adult value scaled by " +
                     "StandBuilder's youngSinkFallbackFactor — useful before the field has been " +
                     "filled in for every species.")]
            public float youngSinkDepth = -1f;

            [Tooltip("Radius of the invisible selection capsule, as a multiple of the tree's own " +
                     "radius (DBH/2). At 1 the target is the real trunk — 25-35 cm on a 38 m tree, " +
                     "very hard to hit from a distance. It also absorbs any LEAN baked into this " +
                     "particular mesh: a trunk modelled off-vertical drifts out of a strictly " +
                     "upright capsule, and widening it for that model brings it back within reach. " +
                     "Leave at -1 to use StandBuilder's default factor.")]
            public float selectionRadiusFactor = -1f;

            /// <summary>Sink depth of the young plant, falling back to the adult value scaled by
            /// <paramref name="fallbackFactor"/> when this species has no explicit value yet.</summary>
            public float YoungSink(float fallbackFactor)
                => youngSinkDepth >= 0f ? youngSinkDepth : sinkDepth * fallbackFactor;

            /// <summary>Selection radius factor, falling back to the builder's default when this
            /// species has no explicit value.</summary>
            public float SelectionFactor(float fallback)
                => selectionRadiusFactor > 0f ? selectionRadiusFactor : fallback;
        }

        [Tooltip("Ideally 10 entries, each with an adult and a young prefab.")]
        public Entry[] species = new Entry[10];

        public int Count => species != null ? species.Length : 0;

        public Entry Pick(int seed)
        {
            if (Count == 0) return null;
            int idx = ((seed % Count) + Count) % Count;
            return species[idx];
        }
    }
}
