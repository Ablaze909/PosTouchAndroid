using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>Come vuole pagare il cliente.</summary>
    public class Pagamento
    {
        public string Metodo { get; set; } = MetodiPagamento.Contanti;
        /// <summary>Contanti: importo consegnato dal cliente (0 = importo esatto).</summary>
        public int VersatoCent { get; set; }
        /// <summary>Misto: parte in contanti (il resto va su carta).</summary>
        public int MistoContantiCent { get; set; }

        public static Pagamento Contanti(int versatoCent = 0) => new Pagamento { Metodo = MetodiPagamento.Contanti, VersatoCent = versatoCent };
        public static Pagamento Carta() => new Pagamento { Metodo = MetodiPagamento.Carta };
        public static Pagamento Misto(int contantiCent) => new Pagamento { Metodo = MetodiPagamento.Misto, MistoContantiCent = contantiCent };
    }

    public enum StatoEsito
    {
        /// <summary>Scontrino emesso e vendita salvata.</summary>
        Riuscito,
        /// <summary>Scontrino emesso ma salvataggio locale fallito (l'incasso c'è, manca il dato).</summary>
        RiuscitoSenzaArchivio,
        /// <summary>Niente è stato emesso: il carrello va lasciato com'è.</summary>
        Fallito,
        /// <summary>
        /// La comunicazione si è interrotta DOPO l'invio della chiusura: lo scontrino potrebbe
        /// essere uscito. L'operatore deve controllare prima di riprovare (evita doppi scontrini).
        /// </summary>
        Incerto
    }

    public class EsitoVendita
    {
        public StatoEsito Stato { get; set; }
        public string Titolo { get; set; } = "";
        public string Messaggio { get; set; } = "";
        public int RestoCent { get; set; }
        public int NumeroDocumento { get; set; }
        public int NumeroAzzeramento { get; set; }

        public bool Emesso => Stato == StatoEsito.Riuscito || Stato == StatoEsito.RiuscitoSenzaArchivio;

        /// <summary>Dati della vendita da registrare se l'operatore conferma che lo scontrino è uscito (stato Incerto).</summary>
        public VenditaRegistrata DaRegistrare { get; set; }

        public static EsitoVendita Errore(string titolo, string messaggio) =>
            new EsitoVendita { Stato = StatoEsito.Fallito, Titolo = titolo, Messaggio = messaggio };
    }
}
