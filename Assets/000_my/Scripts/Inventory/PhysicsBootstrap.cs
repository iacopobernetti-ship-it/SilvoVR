using UnityEngine;

namespace Artemis.Vr
{
    /// <summary>
    /// Un solo flag, ma indispensabile: senza, TUTTO il rilievo non funziona.
    ///
    /// La mesh di collisione delle aree e' esportata con scala X = -1 (specchiata), quindi il
    /// winding dei triangoli e' invertito e i raggi di Unity — che di default ignorano le facce
    /// posteriori — la attraversano senza colpirla. TrunkSampler spara raggi orizzontali contro
    /// il fusto e XrRigPlacer sonda il suolo: entrambi restano ciechi senza questo.
    ///
    /// Va sull'oggetto App del prefab VrApp, cosi' e' attivo in ogni scena.
    /// </summary>
    public class PhysicsBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            Physics.queriesHitBackfaces = true;
            Debug.Log("[PhysicsBootstrap] queriesHitBackfaces = true (mesh specchiata: senza, i raggi non colpiscono).");
        }
    }
}
