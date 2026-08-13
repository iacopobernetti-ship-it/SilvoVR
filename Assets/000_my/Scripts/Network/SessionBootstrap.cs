using Unity.Netcode;
using UnityEngine;

namespace Artemis.Session
{
    /// <summary>
    /// Fa comparire l'unico SessionState della sessione. Lo spawna il SERVER, cioe' il docente:
    /// gli studenti se lo vedono arrivare.
    ///
    /// Vive accanto al NetworkManager nella scena Base, e come lui NON sta nel prefab VrApp —
    /// altrimenti se ne creerebbe uno per scena, e ciascuno proverebbe a spawnare il suo.
    /// </summary>
    public class SessionBootstrap : MonoBehaviour
    {
        [Tooltip("Prefab con NetworkObject + SessionState. Deve stare nella Network Prefabs list.")]
        [SerializeField] private GameObject sessionStatePrefab;

        private bool spawnAttempted;

        private void Update()
        {
            if (spawnAttempted || SessionState.Instance != null) return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer) return;

            if (sessionStatePrefab == null)
            {
                Debug.LogError("[SessionBootstrap] Session State Prefab non assegnato.");
                spawnAttempted = true; return;
            }

            spawnAttempted = true;
            var go = Instantiate(sessionStatePrefab);
            var no = go.GetComponent<NetworkObject>();
            if (no == null) { Debug.LogError("[SessionBootstrap] il prefab non ha un NetworkObject."); Destroy(go); return; }
            no.Spawn();
            Debug.Log("[SessionBootstrap] SessionState spawnato — questo client e' il docente.");
        }
    }
}
