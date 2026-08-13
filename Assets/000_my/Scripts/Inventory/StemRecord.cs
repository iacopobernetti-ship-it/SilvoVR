using System;
using Unity.Netcode;
using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>
    /// Un albero rilevato. Da Fase 4 e' anche SERIALIZZABILE IN RETE: il docente pubblica il suo
    /// inventario e la simulazione lo ricostruisce identica su ogni visore.
    ///
    /// L'AREA NON STA QUI, ed e' una scelta. Nel desktop ogni record portava il proprio plotId
    /// perche' un inventario poteva mescolare piu' aree; qui non puo': un file per area, e in
    /// rete l'area e' in SessionState.PlotId. Ripeterla per stelo sarebbe ridondante e — dettaglio
    /// pratico che ha deciso la questione — costringerebbe a un FixedString64Bytes, l'unico tipo
    /// stringa ammesso da NetworkList, che pero' JsonUtility non serializza in modo leggibile:
    /// avremmo sistemato la rete rompendo i file su disco.
    ///
    /// Tutti i campi sono unmanaged, requisito di NetworkList: int, Vector3, float, bool.
    ///
    /// Cosa e' MISURATO e cosa e' DERIVATO: si misurano posizione in pianta e diametro a 1.30 m.
    /// L'altezza viene dalla curva ipsometrica al momento del rilievo ed e' conservata qui.
    /// </summary>
    [Serializable]
    public struct StemRecord : INetworkSerializable, IEquatable<StemRecord>
    {
        public int     StemId;
        public Vector3 Base;        // punto cliccato (superficie del fusto)
        public Vector3 Axis;        // asse del fusto, dal fit di cerchio
        public float   Dbh;         // metri
        public float   Height;      // metri
        public bool    Marked;

        public Vector2 PlanXY => new Vector2(Base.x, Base.z);

        /// <summary>Area basimetrica del singolo fusto in m² (πd²/4). Sommata da' G.</summary>
        public float BasalArea => Mathf.PI * Dbh * Dbh * 0.25f;

        /// <summary>Dove disegnare il segno di misura: l'ASSE, non il punto cliccato, che sta
        /// sulla corteccia e darebbe un anello sbilenco. Ripiega sul punto per i dati vecchi.</summary>
        public Vector3 MarkAnchor => Axis.sqrMagnitude > 0.0001f ? Axis : Base;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref StemId);
            s.SerializeValue(ref Base);
            s.SerializeValue(ref Axis);
            s.SerializeValue(ref Dbh);
            s.SerializeValue(ref Height);
            s.SerializeValue(ref Marked);
        }

        public bool Equals(StemRecord o) =>
            StemId == o.StemId && Base.Equals(o.Base) && Axis.Equals(o.Axis) &&
            Dbh.Equals(o.Dbh) && Height.Equals(o.Height) && Marked == o.Marked;

        public override bool Equals(object obj) => obj is StemRecord o && Equals(o);
        public override int GetHashCode() => StemId;
    }
}
