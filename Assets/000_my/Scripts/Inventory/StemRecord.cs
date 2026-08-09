using System;
using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>
    /// Un albero rilevato. Versione VR: struct semplice, SENZA INetworkSerializable — la rete
    /// entra in Fase 4 e allora questa struttura verra' resa serializzabile per NetworkList
    /// (PlotId dovra' diventare FixedString64Bytes, che e' l'unico cambiamento previsto).
    ///
    /// PlotId e' una STRINGA: il nome della scena-area (Silvo01…). Nel desktop era un intero con
    /// tutta una catena di compatibilita' per gli inventari vecchi; qui i file nascono adesso,
    /// non c'e' legacy da preservare, e l'id numerico era gia' dichiarato in via di dismissione.
    ///
    /// Cosa e' MISURATO e cosa e' DERIVATO: si misurano la posizione in pianta (Base.x, Base.z)
    /// e il diametro a 1.30 m (fit di cerchio sulla mesh). L'altezza NON si misura: viene dalla
    /// curva ipsometrica al momento del rilievo ed e' conservata qui per non ricalcolarla.
    /// </summary>
    [Serializable]
    public struct StemRecord
    {
        public int     StemId;
        public string  PlotId;      // nome della scena-area
        public Vector3 Base;        // posizione mondo del punto cliccato (SUPERFICIE del fusto)
        public Vector3 Axis;        // asse del fusto a quota base, dal fit di cerchio
        public float   Dbh;         // metri (dal fit di cerchio)
        public float   Height;      // metri (dalla curva ipsometrica)
        public bool    Marked;      // marcatura di rilievo, on/off

        public Vector2 PlanXY => new Vector2(Base.x, Base.z);

        /// <summary>
        /// Dove disegnare il segno di misura. E' l'ASSE del fusto, non il punto cliccato: quello
        /// sta sulla superficie della corteccia e una fascia centrata li' risulterebbe sbilenca.
        /// I file scritti prima che Axis esistesse hanno il campo a zero: in quel caso si ripiega
        /// sul punto cliccato, che e' comunque meglio di un segno all'origine del mondo.
        /// </summary>
        public Vector3 MarkAnchor => Axis.sqrMagnitude > 0.0001f ? Axis : Base;

        /// <summary>Area basimetrica del singolo fusto in m² (πd²/4). Sommata da' G.</summary>
        public float BasalArea => Mathf.PI * Dbh * Dbh * 0.25f;
    }
}
