using UnityEngine;

namespace Artemis.Regeneration
{
    /// <summary>Tag on a reconstructed tree: maps it back to its StemId and Voronoi cell.</summary>
    public class StandTree : MonoBehaviour
    {
        public int StemId;
        public int CellIndex = -1;
    }
}
