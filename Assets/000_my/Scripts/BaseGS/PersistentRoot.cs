using UnityEngine;

namespace Artemis.Session
{
    /// <summary>
    /// Keeps a Bootstrap object alive across network scene loads and prevents duplicates when a
    /// scene that also contains it is loaded again. Put it on every Bootstrap object that must
    /// survive the Inventory -> Simulation switch (session browser, services, session flow…).
    /// NetworkManager already persists on its own and does NOT need this.
    /// </summary>
    [DisallowMultipleComponent]
    public class PersistentRoot : MonoBehaviour
    {
        [Tooltip("Unique id. A second object with the same id destroys itself.")]
        [SerializeField] private string id = "";

        private static readonly System.Collections.Generic.HashSet<string> live =
            new System.Collections.Generic.HashSet<string>();

        private void Awake()
        {
            string key = string.IsNullOrEmpty(id) ? gameObject.name : id;
            if (live.Contains(key)) { Destroy(gameObject); return; }
            live.Add(key);
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            string key = string.IsNullOrEmpty(id) ? gameObject.name : id;
            live.Remove(key);
        }
    }
}
