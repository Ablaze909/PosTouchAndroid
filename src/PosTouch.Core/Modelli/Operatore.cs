using System;
using System.Collections.Generic;

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
        /// <summary>Contanti contati alla chiusura (null = non contati).</summary>
        public int? ContantiContatiCent { get; set; }
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
        /// <summary>Differenza contati − contanti incassati (null se non contati). Positiva = eccedenza.</summary>
        public int? DifferenzaCent => Turno?.ContantiContatiCent is int c ? c - ContantiCent : (int?)null;
    }
}
