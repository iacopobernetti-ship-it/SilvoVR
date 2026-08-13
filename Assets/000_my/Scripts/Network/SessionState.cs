using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Artemis.Inventory;

namespace Artemis.Session
{
    /// <summary>Una proposta di uno studente: quale albero, e quale colore lo rappresenta.</summary>
    public struct NetCandidacy : INetworkSerializable, IEquatable<NetCandidacy>
    {
        public ulong ClientId;
        public int StemId;
        public int ColorIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref ClientId); s.SerializeValue(ref StemId); s.SerializeValue(ref ColorIndex); }

        public bool Equals(NetCandidacy o) => ClientId == o.ClientId && StemId == o.StemId && ColorIndex == o.ColorIndex;
        public override bool Equals(object o) => o is NetCandidacy c && Equals(c);
        public override int GetHashCode() => (int)ClientId * 397 ^ StemId;
    }

    /// <summary>Un albero abbattuto in un dato turno (i turni si riapplicano in ordine).</summary>
    public struct NetFelled : INetworkSerializable, IEquatable<NetFelled>
    {
        public int StemId;
        public int Round;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref StemId); s.SerializeValue(ref Round); }

        public bool Equals(NetFelled o) => StemId == o.StemId && Round == o.Round;
        public override bool Equals(object o) => o is NetFelled f && Equals(f);
        public override int GetHashCode() => StemId * 397 ^ Round;
    }

    /// <summary>
    /// L'unica verita' condivisa: chi e' il docente, quale soprassuolo si sta simulando, quale
    /// scenario climatico, e la martellata in corso.
    ///
    /// Topologia HOST: il docente ospita la sessione, quindi e' il server. Ne discendono
    /// gratuitamente tre cose che altrimenti andrebbero difese a mano — solo lui puo' scrivere
    /// le NetworkVariable (permesso di scrittura Server, che e' il default), solo lui puo'
    /// cambiare scena (NetworkManager.SceneManager.LoadScene e' server-only), e gli studenti
    /// devono passare da RPC per proporre qualcosa. La regola "solo il docente decide" e'
    /// insomma imposta dal trasporto, non dalla nostra buona volonta'.
    ///
    /// Cosa NON sta qui, di proposito: il rilievo. Ogni partecipante misura per conto suo e vede
    /// solo i propri segni — e' un esercizio individuale, e replicarlo confonderebbe le acque.
    /// In rete viaggia solo l'inventario che il DOCENTE pubblica per costruire la simulazione.
    /// </summary>
    public class SessionState : NetworkBehaviour
    {
        public static SessionState Instance { get; private set; }
        public static event Action OnReady;

        // ---- ruolo ---------------------------------------------------------------------------
        public readonly NetworkVariable<ulong> TeacherClientId = new NetworkVariable<ulong>(ulong.MaxValue);

        public bool TeacherAssigned => TeacherClientId.Value != ulong.MaxValue;
        public bool IAmTeacher => IsSpawned && NetworkManager != null &&
                                  TeacherClientId.Value == NetworkManager.LocalClientId;

        // ---- area corrente -------------------------------------------------------------------
        public readonly NetworkVariable<FixedString64Bytes> PlotId = new NetworkVariable<FixedString64Bytes>("");
        /// Area nominale in m²: guida gli indici per ettaro e varia con l'area.
        public readonly NetworkVariable<float> PlotAreaM2 = new NetworkVariable<float>(400f);

        // ---- inventario del docente (CONTENUTO, non il nome del file) --------------------------
        public NetworkList<StemRecord> Inventory;

        // ---- clima (lo sceglie il docente, tutti lo vedono) -------------------------------------
        public readonly NetworkVariable<FixedString32Bytes> Scenario = new NetworkVariable<FixedString32Bytes>("ssp245");
        public readonly NetworkVariable<int> StartYear = new NetworkVariable<int>(2041);
        public readonly NetworkVariable<int> EndYear = new NetworkVariable<int>(2060);
        public readonly NetworkVariable<float> Aridity = new NetworkVariable<float>(0.4f);

        // ---- martellata --------------------------------------------------------------------------
        public NetworkList<NetCandidacy> Candidacies;   // proposte degli studenti
        public NetworkList<int> TeacherMarks;           // segni del docente, non ancora abbattuti
        public NetworkList<NetFelled> Felled;           // abbattuti, raggruppati per turno
        public NetworkList<int> RoundSeeds;             // un seme per turno -> rinnovazione identica

        // ---- ciclo di vita -------------------------------------------------------------------------

        private void Awake()
        {
            Inventory    = new NetworkList<StemRecord>();
            Candidacies  = new NetworkList<NetCandidacy>();
            TeacherMarks = new NetworkList<int>();
            Felled       = new NetworkList<NetFelled>();
            RoundSeeds   = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            // Chi ospita e' il docente. Lo si scrive una volta sola, dal server.
            if (IsServer && !TeacherAssigned) TeacherClientId.Value = NetworkManager.LocalClientId;
            OnReady?.Invoke();
        }

        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        // ---- scritture del docente -------------------------------------------------------------------

        public void SetPlot(string plotId, float areaM2)
        {
            if (!IsServer) return;
            PlotId.Value = plotId ?? "";
            PlotAreaM2.Value = Mathf.Max(1f, areaM2);
        }

        /// <summary>Pubblica il CONTENUTO dell'inventario: i nomi non basterebbero, ogni visore ha
        /// i suoi file e quelli degli studenti non c'entrano nulla con la lezione.</summary>
        public void PublishInventory(IReadOnlyList<StemRecord> stems)
        {
            if (!IsServer) return;
            Inventory.Clear();
            if (stems != null) foreach (var s in stems) Inventory.Add(s);
            ClearMarkingInternal();
        }

        public void SetClimate(string scenario, int startYear, int endYear, float aridity01)
        {
            if (!IsServer) return;
            Scenario.Value = scenario ?? "ssp245";
            StartYear.Value = startYear; EndYear.Value = endYear;
            Aridity.Value = Mathf.Clamp01(aridity01);
        }

        public List<StemRecord> ReadInventory()
        {
            var list = new List<StemRecord>();
            if (Inventory != null) foreach (var s in Inventory) list.Add(s);
            return list;
        }

        // ---- proposte degli studenti (passano dal server, che le arbitra) --------------------------

        [Rpc(SendTo.Server)]
        public void RequestCandidacyRpc(int stemId, int colorIndex, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < Candidacies.Count; i++)
            {
                if (Candidacies[i].ClientId != sender) continue;
                bool sameTree = Candidacies[i].StemId == stemId;
                Candidacies.RemoveAt(i);
                if (sameTree) return;                    // ri-puntare il proprio albero = ritirare
                break;                                   // uno studente propone UN albero alla volta
            }
            Candidacies.Add(new NetCandidacy { ClientId = sender, StemId = stemId, ColorIndex = colorIndex });
        }

        [Rpc(SendTo.Server)]
        public void RequestClearCandidacyRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = Candidacies.Count - 1; i >= 0; i--)
                if (Candidacies[i].ClientId == sender) Candidacies.RemoveAt(i);
        }

        // ---- martellata del docente ----------------------------------------------------------------

        public void ToggleTeacherMark(int stemId)
        {
            if (!IsServer) return;
            for (int i = 0; i < TeacherMarks.Count; i++)
                if (TeacherMarks[i] == stemId) { TeacherMarks.RemoveAt(i); return; }
            TeacherMarks.Add(stemId);
        }

        /// <summary>
        /// Esegue la martellata: gli alberi segnati DAL DOCENTE diventano un turno di
        /// abbattimento con un seme condiviso, cosi' la rinnovazione nasce identica su ogni
        /// visore. Poi si azzera tutto — anche le proposte degli studenti, che si riferivano a
        /// un bosco che non esiste piu'.
        /// </summary>
        public void CommitMarking()
        {
            if (!IsServer || TeacherMarks.Count == 0) return;

            int round = RoundSeeds.Count;
            RoundSeeds.Add(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            foreach (var id in TeacherMarks) Felled.Add(new NetFelled { StemId = id, Round = round });

            TeacherMarks.Clear();
            Candidacies.Clear();
        }

        public void ClearMarking() { if (IsServer) ClearMarkingInternal(); }

        private void ClearMarkingInternal()
        {
            Candidacies.Clear(); TeacherMarks.Clear(); Felled.Clear(); RoundSeeds.Clear();
        }

        // ---- letture per la vista ----------------------------------------------------------------------

        public int RoundCount => RoundSeeds != null ? RoundSeeds.Count : 0;

        public List<int> StemsOfRound(int round)
        {
            var l = new List<int>();
            if (Felled == null) return l;
            foreach (var f in Felled) if (f.Round == round) l.Add(f.StemId);
            return l;
        }

        public int SeedOfRound(int round) =>
            (RoundSeeds != null && round >= 0 && round < RoundSeeds.Count) ? RoundSeeds[round] : 0;

        public List<int> ReadTeacherMarks()
        {
            var l = new List<int>();
            if (TeacherMarks != null) foreach (var m in TeacherMarks) l.Add(m);
            return l;
        }
    }
}
