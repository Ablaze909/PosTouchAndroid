using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>Vendita da registrare nell'archivio locale.</summary>
    public class VenditaRegistrata
    {
        public DateTime DataOra { get; set; } = DateTime.Now;
        public int NumeroScontrino { get; set; }
        public int NumeroAzzeramento { get; set; }
        public int TotaleCentesimi { get; set; }
        public string MetodoPagamento { get; set; } = MetodiPagamento.Contanti;
        public int ContantiCent { get; set; }
        public int CartaCent { get; set; }
        public bool StampatoSuRT { get; set; } = true;
        /// <summary>Data/ora emissione letta dall'RT (yyyy-MM-dd HH:mm), se disponibile.</summary>
        public string DataEmissioneRT { get; set; }
        public List<RigaScontrino> Righe { get; set; } = new List<RigaScontrino>();
        /// <summary>Operatore del turno (0 = gestione operatori spenta: si registra come 1, come il PC).</summary>
        public int OperatoreId { get; set; }
        public string OperatoreNome { get; set; } = "";
    }

    public class TotaliGiorno
    {
        public int NumeroScontrini { get; set; }
        public int TotaleCent { get; set; }
        public int ContantiCent { get; set; }
        public int CartaCent { get; set; }
    }
}
