using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Diagnostica;

namespace PosTouch.Core.Fiscale
{
    /// <summary>
    /// Risultato restituito dopo la chiusura di uno scontrino fiscale.
    /// Contiene i dati del TX_FOOTER inviato dall'RT.
    /// </summary>
    public class RisultatoChiusuraRT
    {
        /// <summary>True se l'RT ha confermato la chiusura con TX_FOOTER</summary>
        public bool Confermato { get; set; }
        
        /// <summary>Numero documento fiscale assegnato dall'RT</summary>
        public int NumeroDocumento { get; set; }
        
        /// <summary>Numero azzeramento corrente dell'RT</summary>
        public int NumeroAzzeramento { get; set; }
        
        /// <summary>Tipo documento: "F" = Fiscale, "G" = Gestionale</summary>
        public string TipoDocumento { get; set; }
        
        /// <summary>Data emissione (formato aammgg)</summary>
        public string DataEmissione { get; set; }
        
        /// <summary>Ora emissione (formato hhmm)</summary>
        public string OraEmissione { get; set; }
        
        /// <summary>Risposta raw dell'RT (per debug)</summary>
        public string RispostaRaw { get; set; }
        
        /// <summary>Eventuale errore</summary>
        public string Errore { get; set; }
    }

    /// <summary>
    /// Risultato del comando 50F: verifica stato scontrino per reso/annullo.
    /// Contiene esito, aliquote IVA e importi per IVA dello scontrino originale.
    /// Manuale Xon/Xoff pag. 20: risposta formato <esito><aliquota IVA 8x><importi IVA 8x>@@
    /// </summary>
    public class Risultato50F
    {
        /// <summary>
        /// Codice esito numerico (0-10).
        /// Manuale 3i pag 22:
        ///   0 = annullabile/rendibile (dati nel DGFE locale)
        ///   1 = non esistente
        ///   2 = non è doc di vendita
        ///   3 = già annullato/reso
        ///   4 = aliquote IVA cambiate
        ///   5 = aliquota IVA non associata a reparto
        ///   8 = già annullato su altro DGFE/RT
        ///   9 = da altro RT, non annullato/reso → annullo/reso OK (battute dall'operatore)
        ///  10 = già reso parzialmente → solo reso OK, annullo NO
        /// </summary>
        public int Esito { get; set; } = -1;
        
        /// <summary>True se lo scontrino è annullabile (esito 0 o 9)</summary>
        public bool Annullabile => Esito == 0 || Esito == 9;
        
        /// <summary>True se lo scontrino è rendibile (esito 0, 9, o 10)</summary>
        public bool Rendibile => Esito == 0 || Esito == 9 || Esito == 10;
        
        /// <summary>Descrizione leggibile dell'esito</summary>
        public string EsitoDescrizione { get; set; }
        
        /// <summary>Aliquote IVA con relativi importi (solo quelle > 0)</summary>
        public List<AliquotaIVA> Aliquote { get; set; } = new List<AliquotaIVA>();
        
        /// <summary>Totale scontrino originale (somma di tutti gli importi IVA)</summary>
        public decimal TotaleEuro { get; set; }
        
        /// <summary>Risposta raw dall'RT (per debug/log)</summary>
        public string RispostaRaw { get; set; }
    }
    
    public class AliquotaIVA
    {
        /// <summary>Codice aliquota: "22%", "10%", "04%", "ES", "EN", ecc.</summary>
        public string Codice { get; set; }
        
        /// <summary>Importo in euro per questa aliquota</summary>
        public decimal ImportoEuro { get; set; }
    }
}
