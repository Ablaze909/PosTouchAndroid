using System;
using System.Collections.Generic;
using System.Linq;

namespace PosTouch.Core.Modelli
{
    /// <summary>Cosa un operatore può fare (oltre a vendere). L'amministratore può tutto.</summary>
    public enum Permesso
    {
        ModificaPrezzi,
        Sconti,
        AnnullaScontrino,
        StornoComandati,
        ModalitaModifica,
        BackOffice,
        Impostazioni,
        ChiusuraFiscale
    }

    public static class Permessi
    {
        /// <summary>Tutti i permessi, nell'ordine in cui si mostrano nella scheda operatore.</summary>
        public static readonly Permesso[] Tutti = (Permesso[])Enum.GetValues(typeof(Permesso));

        public static string Nome(Permesso p) => p switch
        {
            Permesso.ModificaPrezzi => "Modificare i prezzi",
            Permesso.Sconti => "Fare sconti",
            Permesso.AnnullaScontrino => "Annullare lo scontrino",
            Permesso.StornoComandati => "Stornare articoli già in cucina",
            Permesso.ModalitaModifica => "Modalità modifica (✏️)",
            Permesso.BackOffice => "Back office (articoli, categorie, varianti)",
            Permesso.Impostazioni => "Impostazioni",
            Permesso.ChiusuraFiscale => "Chiusura fiscale (Z)",
            _ => p.ToString()
        };
    }

    public class Operatore
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        /// <summary>Solo l'amministratore entra nella gestione operatori; ha tutti i permessi.</summary>
        public bool Amministratore { get; set; }
        public bool Attivo { get; set; } = true;
        public HashSet<Permesso> Permessi { get; set; } = new HashSet<Permesso>();

        public bool Puo(Permesso p) => Attivo && (Amministratore || Permessi.Contains(p));

        public Operatore Copia() => new Operatore
        {
            Id = Id, Nome = Nome, Amministratore = Amministratore, Attivo = Attivo, Permessi = new HashSet<Permesso>(Permessi)
        };
    }

    public enum StatoTurno { Aperto = 0, Sospeso = 1, Chiuso = 2 }

    public class Turno
    {
        public long Id { get; set; }
        public int OperatoreId { get; set; }
        public string OperatoreNome { get; set; } = "";
        public DateTime Apertura { get; set; }
        public DateTime? Chiusura { get; set; }
        public StatoTurno Stato { get; set; }
        /// <summary>Contanti contati alla chiusura (null = non contati). Con il fondo cassa: tutto il cassetto, fondo compreso.</summary>
        public int? ContantiContatiCent { get; set; }
        /// <summary>Fondo cassa all'apertura (null = non chiesto).</summary>
        public int? FondoInizialeCent { get; set; }
        /// <summary>Fondo lasciato nel cassetto per il turno dopo (null = non indicato).</summary>
        public int? FondoLasciatoCent { get; set; }
    }

    public enum TipoMovimento { Prelievo = 0, Versamento = 1 }

    /// <summary>Contanti tolti o messi nel cassetto durante il turno (es. 200 € per la banca, spiccioli).</summary>
    public class MovimentoCassa
    {
        public long Id { get; set; }
        public long TurnoId { get; set; }
        public DateTime DataOra { get; set; }
        public TipoMovimento Tipo { get; set; }
        public int ImportoCent { get; set; }
        public string Causale { get; set; } = "";
        public string OperatoreNome { get; set; } = "";
    }

    /// <summary>Riepilogo di un turno: incassi dell'operatore tra apertura e chiusura.</summary>
    public class RiepilogoTurno
    {
        public Turno Turno { get; set; }
        public int Scontrini { get; set; }
        public int TotaleScontriniCent { get; set; }
        public int ContantiCent { get; set; }
        public int CartaCent { get; set; }
        public int Conti { get; set; }
        public int TotaleContiCent { get; set; }
        public List<MovimentoCassa> Movimenti { get; set; } = new List<MovimentoCassa>();
        public int PrelieviCent => Movimenti.Where(m => m.Tipo == TipoMovimento.Prelievo).Sum(m => m.ImportoCent);
        public int VersamentiCent => Movimenti.Where(m => m.Tipo == TipoMovimento.Versamento).Sum(m => m.ImportoCent);
        /// <summary>Contanti che devono esserci nel cassetto: fondo + incassi in contanti + versamenti − prelievi.</summary>
        // i conti non fiscali (tasto CONTO) si incassano in contanti: entrano nel cassetto come gli scontrini in contanti
        public int ContantiAttesiCent => (Turno?.FondoInizialeCent ?? 0) + ContantiCent + TotaleContiCent + VersamentiCent - PrelieviCent;
        /// <summary>Differenza contati − attesi (null se non contati). Positiva = eccedenza, negativa = ammanco.</summary>
        public int? DifferenzaCent => Turno?.ContantiContatiCent is int c ? c - ContantiAttesiCent : (int?)null;
        /// <summary>Contanti da togliere lasciando il fondo per il turno dopo (null se non contati o fondo non indicato).</summary>
        public int? DaVersareCent => Turno?.ContantiContatiCent is int c && Turno.FondoLasciatoCent is int f ? c - f : (int?)null;
    }
}
