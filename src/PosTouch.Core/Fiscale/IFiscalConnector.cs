using System;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Core.Fiscale
{
    /// <summary>
    /// Interfaccia comune per tutti i connettori a registratori telematici fiscali.
    /// Implementata da RT3iConnector (protocollo 3i XON/XOFF) e EpsonConnector (protocollo Epson Communication).
    /// Consente al POS di lavorare con qualsiasi cassa fiscale senza conoscere il protocollo sottostante.
    /// </summary>
    public interface IFiscalConnector : IDisposable
    {
        // ========== ULTIMO ERRORE (per messaggi UI) ==========
        
        /// <summary>
        /// Ultimo messaggio di errore ricevuto dalla stampante.
        /// Viene aggiornato ad ogni comando che fallisce.
        /// </summary>
        string UltimoErrore { get; }
        
        // ========== CONNESSIONE ==========
        
        Task<bool> ConnectAsync(CancellationToken ct = default);
        void Disconnect();

        // ========== VENDITA ==========
        
        Task<bool> SelezionaOperatoreAsync(int numero);
        Task<bool> VendiRepartoAsync(int reparto, string descrizione, int prezzoCent, decimal qta = 1);
        Task<bool> SubtotaleAsync();
        Task<bool> StampaMessaggioAsync(string messaggio);

        // ========== SCONTI ==========
        
        Task<bool> ScontoPercentualeRigaAsync(decimal percentuale);
        Task<bool> ScontoPercentualeTotaleAsync(decimal percentuale);
        Task<bool> ScontoValoreRigaAsync(int importoCent);
        Task<bool> ScontovaloreTotaleAsync(int importoCent);

        // ========== PAGAMENTI / CHIUSURA SCONTRINO ==========
        
        Task<bool> ChiudiContantiAsync(int importoCent);
        Task<bool> ChiudiContantiAutoAsync();
        Task<bool> ChiudiCartaCreditoAsync();
        
        /// <summary>
        /// Pagamento misto: prima contanti (parziale), poi carta (chiude scontrino).
        /// Su 3i: due comandi T sequenziali (8000H1T + 2000H3T).
        /// Su Epson: due chiamate 1-084 con L/R=0 (parziale) e L/R=1 (finale).
        /// </summary>
        Task<bool> ChiudiMistoAsync(int contantiCent, int cartaCent);

        // ========== CASSETTO ==========
        
        Task<bool> ApriCassettoAsync();

        // ========== FUNZIONI FISCALI ==========
        
        Task<bool> ChiusuraFiscaleAsync();
        Task<bool> LetturaGiornalieraAsync();
        Task<bool> RapportoRepartiLetturaAsync();
        Task<bool> RapportoRepartiAzzeramentoAsync();

        // ========== STATO / DIAGNOSTICA ==========
        
        Task<string> LeggiStatoRegistratoreAsync();
        Task<string> LeggiStatoRTAsync();
        Task<(int Azzeramento, int Scontrino)?> LeggiProgressiviRTAsync();
        Task<bool> HaScontrinoApertoAsync();

        // ========== RECOVERY ==========
        
        Task<bool> ClearAsync();
        Task<bool> AnnullaScontrinoCorrenteAsync();
        Task<bool> RecuperaScontrinoApertoAsync();

        // ========== ANNULLO / RESO ==========
        
        /// <summary>
        /// Verifica se uno scontrino può essere annullato/reso.
        /// Su 3i: comando 50F. Su Epson: comando 9-205.
        /// La data è necessaria per Epson; su 3i viene ignorata.
        /// </summary>
        Task<Risultato50F> VerificaScontrinoCompletoAsync(int azzeramento, int numeroScontrino, DateTime? dataScontrino = null);
        
        /// <summary>
        /// Annulla intero scontrino. Su 3i: 52F. Su Epson: 1-078 con stringa VOID.
        /// Su Epson richiede anche la data dello scontrino (presa dal DB).
        /// </summary>
        Task<bool> AnnullaScontrinoAsync(int azzeramento, int numeroScontrino);
        
        /// <summary>
        /// Versione estesa per Epson che richiede la data.
        /// Su 3i la data viene ignorata.
        /// </summary>
        Task<bool> AnnullaScontrinoAsync(int azzeramento, int numeroScontrino, DateTime dataScontrino);

        Task<bool> IniziaResoAsync(int azzeramento, int numeroScontrino, DateTime? dataScontrino = null);

        // ========== RISTAMPA ==========
        
        Task<bool> StampaUltimoScontrinoAsync();
        Task<bool> StampaPenultimoScontrinoAsync();
        // FIX BUG-8: aggiunto parametro dataChiusuraZ (necessario per Epson 3-098)
        // RT3i non usa la data (usa il descrittore nel protocollo), quindi è opzionale
        Task<bool> StampaCopiaFiscaleAsync(int azzeramento, int numeroScontrino, DateTime? dataChiusuraZ = null);

        // ========== TX_FOOTER ==========
        
        /// <summary>
        /// Configura il footer di risposta. 
        /// Su 3i: configura TX_FOOTER JSON ("3"901F).
        /// Su Epson: no-op (la risposta è già strutturata nel PDU).
        /// </summary>
        Task<bool> ConfiguraTxFooterJsonAsync();
        
        /// <summary>
        /// Legge conferma chiusura scontrino con numero documento.
        /// Su 3i: legge TX_FOOTER JSON dallo stream.
        /// Su Epson: il numero doc è già nella risposta della END COMMERCIAL DOC.
        /// </summary>
        Task<RisultatoChiusuraRT> LeggiTxFooterAsync();

        /// <summary>
        /// Prepara l'appendice eliminacode PRIMA della chiusura scontrino.
        /// RT3i: invia comando 40F (segnala appendice).
        /// Epson: invia messaggio cortesia @40F con numero eliminacode.
        /// </summary>
        Task<bool> PreannunciaAppendiceAsync(int eliminacode);

        /// <summary>
        /// Stampa l'appendice eliminacode DOPO la chiusura scontrino.
        /// RT3i: invia 41F + righe testo + 42F.
        /// Epson: no-op (già gestito in PreannunciaAppendice).
        /// </summary>
        Task<bool> StampaAppendiceAsync(int eliminacode);

        /// <summary>
        /// Prepara un'appendice a testo libero PRIMA della chiusura scontrino (es. ricevuta POS).
        /// RT3i: invia 40F.
        /// Epson: invia le righe come testo promozionale H2=078 TYPE=6.
        /// </summary>
        Task<bool> PreannunciaAppendiceTestoLiberoAsync(string[] righe);

        /// <summary>
        /// Stampa un'appendice a testo libero DOPO la chiusura scontrino.
        /// RT3i: invia 41F + righe + 42F.
        /// Epson: no-op (già gestito in PreannunciaAppendiceTestoLiberoAsync).
        /// </summary>
        Task<bool> StampaAppendiceTestoLiberoAsync(string[] righe);
    }
}
