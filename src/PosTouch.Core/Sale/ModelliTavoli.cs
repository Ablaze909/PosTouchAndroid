using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    public class TavoloAperto
    {
        public long Id { get; set; }
        public int SalaId { get; set; }
        public int Numero { get; set; }
        /// <summary>Conti clienti (sala 0): nome del cliente.</summary>
        public string NomeCliente { get; set; } = "";
        public DateTime Apertura { get; set; }
        public bool Preconto { get; set; }
        /// <summary>Ultima portata mandata con VAI (AVANZA).</summary>
        public int PortataCorrente { get; set; }
        // riepilogo per la mappa tavoli
        public int TotaleCent { get; set; }
        public int Articoli { get; set; }
        public bool DaComandare { get; set; }
        /// <summary>Storni non ancora stampati (app chiusa prima di uscire): il tavolo va mostrato anche se vuoto.</summary>
        public bool StorniDaInviare { get; set; }
        /// <summary>Da mostrare in mappa come occupato: ha articoli o storni da inviare alla cucina.</summary>
        public bool Occupato => Articoli > 0 || StorniDaInviare;
        /// <summary>Portata più alta presente (0 = senza portate).</summary>
        public int PortataMassima { get; set; }
    }

    public enum TipoJob { Comanda = 0, Storno = 1, Vai = 2, Riepilogo = 3 }
    public enum StatoJob { InAttesa = 0, Errore = 1, Fatto = 2 }

    /// <summary>Stampa in coda (byte ESC/POS già pronti), salvata nel database: non si perde se la stampante è spenta.</summary>
    public class JobStampa
    {
        public long Id { get; set; }
        public DateTime Creato { get; set; }
        public TipoJob Tipo { get; set; }
        public string Descrizione { get; set; } = "";
        public string Stampante { get; set; } = "";
        public string IP { get; set; } = "";
        public int Porta { get; set; } = 9100;
        public byte[] Dati { get; set; }
        public StatoJob Stato { get; set; }
        public int Tentativi { get; set; }
        public string UltimoErrore { get; set; }
        /// <summary>Tavolo da cui viene la stampa (per segnare il tavolo NERO "ERRORE STAMPA" come sul PC).</summary>
        public int SalaId { get; set; }
        public int NumeroTavolo { get; set; }
        /// <summary>Controllo di stato della stampante prima di stampare (parametro dell'uscita).</summary>
        public bool ControlloStato { get; set; }
    }

    /// <summary>Deviazione forzata (DeviazioniAttive del PC): tutto ciò che va alla stampante Origine esce sulla Destinazione.</summary>
    public class DeviazioneStampa
    {
        public string NomeOrigine { get; set; } = "";
        public string IPOrigine { get; set; } = "";
        public int PortaOrigine { get; set; }
        public string NomeDestinazione { get; set; } = "";
        public string IPDestinazione { get; set; } = "";
        public int PortaDestinazione { get; set; }
        public DateTime Attivata { get; set; }
    }

    /// <summary>Registro storni (per le statistiche), come StornoComande del PC.</summary>
    public class StornoRegistrato
    {
        public string Sala { get; set; } = "";
        public string Tavolo { get; set; } = "";
        public string Centro { get; set; } = "";
        public string Descrizione { get; set; } = "";
        public decimal Quantita { get; set; }
        public int PrezzoCentesimi { get; set; }
        public bool Stampato { get; set; }
    }
}
