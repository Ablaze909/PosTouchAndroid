using System;
using System.IO;
using System.Text.Json;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;

namespace PosTouch.Core.Configurazione
{
    /// <summary>
    /// Impostazioni della cassa Android (impostazioni.json). Stessi nomi e default di Config.cs del PC
    /// per le voci già portate su Android.
    /// </summary>
    public class Impostazioni
    {
        // ── 🖨️ CONNESSIONI: registratore telematico ──
        public TipoCassaFiscale TipoCassa { get; set; } = TipoCassaFiscale.RT3i;
        public string IP_RT { get; set; } = "192.168.1.100";
        public int Porta_RT { get; set; } = 1723;
        /// <summary>Apri cassetto dopo ogni vendita.</summary>
        public bool ApriCassetto { get; set; } = false;
        /// <summary>Numero operatore selezionato sull'RT a inizio scontrino.</summary>
        public int OperatoreRT { get; set; } = 1;

        // ── 📋 Identità punto vendita (schermata iniziale) ──
        public string NomePuntoVendita { get; set; } = "POS Touch";
        public string SottotitoloPuntoVendita { get; set; } = "Punto Vendita";

        // ── 💰 VENDITA: regole ──
        public bool AbilitaSconti { get; set; } = true;
        public bool AbilitaCambioPrezzo { get; set; } = true;
        public bool AbilitaModificaVendita { get; set; } = true;
        public bool MostraStrumentiVendita { get; set; } = true;
        public bool RichiestaPrezzoArticoliZero { get; set; } = true;
        public bool BloccoModificaQuantita { get; set; } = false;
        /// <summary>Blocco riga singola: un solo articolo per scontrino (come sul PC).</summary>
        public bool BloccoRigaSingola { get; set; } = false;
        /// <summary>Riconosce le etichette EAN-13 della bilancia (prefisso 2, prezzo nel codice), come sul PC.</summary>
        public bool UsaBarcodeBilancia { get; set; } = false;

        // ── Stampante termica (CONTO non fiscale), come sul PC ──
        public bool AbilitaStampanteTermica { get; set; } = true;
        public string IP_Termica { get; set; } = "192.168.1.200";
        public int Porta_Termica { get; set; } = 9100;
        public string IntestazioneConto1 { get; set; } = "RISTORANTE";
        public string IntestazioneConto2 { get; set; } = "";
        public string IntestazioneConto3 { get; set; } = "";
        public bool AccorpaRigheConto { get; set; } = true;
        /// <summary>Stampa "Conto N. x" in fondo al conto (sul PC non c'era: predefinito spento).</summary>
        public bool StampaNumeroConto { get; set; } = false;

        /// <summary>Sale, tavoli, centri di produzione, stampanti comande e portate.</summary>
        public ConfigSale Sale { get; set; } = new ConfigSale();
        /// <summary>Mostra la riga ＋/﹣ sotto la tastiera (MostraRigaVariantiVendita del PC).</summary>
        public bool MostraRigaPiuMeno { get; set; } = true;
        /// <summary>Accorpa righe uguali a video (2× caffè → una riga).</summary>
        public bool AccorpaRigheVideo { get; set; } = true;
        /// <summary>Raggruppa righe identiche (reparto+descrizione+prezzo) prima dell'invio all'RT.</summary>
        public bool AccorpaRigheScontrino { get; set; } = true;

        // ── 🖥️ VENDITA: interfaccia ──
        /// <summary>"Piccola" (38), "Media" (52), "Grande" (68): altezza tasti categorie.</summary>
        public string DimensioniTastiCategorie { get; set; } = "Media";
        public int FontSizeCategorie { get; set; } = 10;

        // ── 🔒 SICUREZZA ──
        /// <summary>Password amministratore (solo cifre): richiesta per Strumenti, Back office e Impostazioni. Vuota = nessuna.</summary>
        public string PinImpostazioni { get; set; } = "";

        // ── ⚙️ AVANZATE ──
        public int TimeoutConnessioneRT_ms { get; set; } = 5000;
        public int TimeoutRispostaRT_ms { get; set; } = 10000;
        /// <summary>DEBUG, INFO, WARN, ERROR.</summary>
        public string LivelloLog { get; set; } = "DEBUG";

        public int AltezzaCategoria => DimensioniTastiCategorie switch
        {
            "Piccola" => 38,
            "Grande" => 68,
            _ => 52
        };

        public ImpostazioniFiscali ComeImpostazioniFiscali() => new ImpostazioniFiscali
        {
            TimeoutConnessioneRT_ms = TimeoutConnessioneRT_ms,
            TimeoutRispostaRT_ms = TimeoutRispostaRT_ms
        };

        public Impostazioni Copia()
        {
            var c = (Impostazioni)MemberwiseClone();
            c.Sale = (Sale ?? new ConfigSale()).Copia();
            return c;
        }
    }

    /// <summary>Lettura/scrittura di impostazioni.json.</summary>
    public class ArchivioImpostazioni
    {
        private readonly string _percorso;

        public ArchivioImpostazioni(string cartellaDati)
        {
            _percorso = Path.Combine(cartellaDati, "impostazioni.json");
        }

        public Impostazioni Carica()
        {
            try
            {
                if (File.Exists(_percorso))
                    return JsonSerializer.Deserialize(File.ReadAllText(_percorso), JsonContesto.Default.Impostazioni) ?? new Impostazioni();
            }
            catch (Exception ex)
            {
                LogManager.Error($"Impostazioni non leggibili, uso i valori predefiniti: {ex.Message}");
            }
            return new Impostazioni();
        }

        public void Salva(Impostazioni imp)
        {
            // Scrittura atomica: file temporaneo + sostituzione
            string tmp = _percorso + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(imp, JsonContesto.Default.Impostazioni));
            File.Move(tmp, _percorso, overwrite: true);
            ImpostazioniFiscali.Correnti = imp.ComeImpostazioniFiscali();
            LogManager.SetLivelloMinimo(imp.LivelloLog);
        }
    }
}
