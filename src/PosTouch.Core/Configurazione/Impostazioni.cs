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
        /// <summary>Virgola implicita: i prezzi si battono in centesimi (350 = € 3,50), in vendita e nei tastierini prezzo.</summary>
        public bool VirgolaImplicita { get; set; } = false;
        /// <summary>Blocco riga singola: un solo articolo per scontrino (come sul PC).</summary>
        public bool BloccoRigaSingola { get; set; } = false;
        /// <summary>Riconosce le etichette EAN-13 della bilancia (prefisso 2, prezzo nel codice), come sul PC.</summary>
        public bool UsaBarcodeBilancia { get; set; } = false;
        /// <summary>Stampa varianti e gusti sotto l'articolo sullo scontrino fiscale (VariantiSuScontrino del PC).</summary>
        public bool VariantiSuScontrino { get; set; } = false;
        /// <summary>In vendita le varianti si mostrano divise per GRUPPO (false = tutte insieme in ordine alfabetico).</summary>
        public bool VariantiPerGruppo { get; set; } = true;
        /// <summary>Stampa varianti e gusti sotto l'articolo su conto e preconto.</summary>
        public bool VariantiSuConto { get; set; } = false;

        // ── Stampante termica (CONTO non fiscale), come sul PC ──
        public bool AbilitaStampanteTermica { get; set; } = true;
        /// <summary>Vecchia stampante dei conti: usata solo per passare alle nuove <see cref="Stampe"/> (ora è la stampa PRECONTI).</summary>
        public string IP_Termica { get; set; } = "";
        public int Porta_Termica { get; set; } = 9100;
        public string IntestazioneConto1 { get; set; } = "RISTORANTE";
        public string IntestazioneConto2 { get; set; } = "";
        public string IntestazioneConto3 { get; set; } = "";
        public bool AccorpaRigheConto { get; set; } = true;
        /// <summary>Stampa "Conto N. x" in fondo al conto (sul PC non c'era: predefinito spento).</summary>
        public bool StampaNumeroConto { get; set; } = false;

        /// <summary>Sale, tavoli, centri di produzione, stampanti comande e portate.</summary>
        public ConfigSale Sale { get; set; } = new ConfigSale();
        /// <summary>Opzioni di stampa come POSBY (stampanti, uscite, preconti, per sala, comande da banco). null = da creare dalle vecchie.</summary>
        public ConfigStampe Stampe { get; set; }

        /// <summary>Stampante di preconti e conti per una sala (Banco = <see cref="ConfigStampe.SalaBanco"/>).</summary>
        public ParametriStampa StampantePreconti(int salaId) => (Stampe ?? new ConfigStampe()).Di(salaId).Preconto;

        /// <summary>Crea le opzioni di stampa la prima volta (dai vecchi centri e dalla vecchia termica) e le rende sempre valide.</summary>
        public void PreparaStampe()
        {
            Sale ??= new ConfigSale();
            Sale.Normalizza();
            if (Stampe == null)
            {
                Stampe = ConfigStampe.DaVecchia(Sale, IP_Termica, Porta_Termica, Sale.CicalinoComande > 0);
                // passaggio fatto: le vecchie voci non servono più (restano solo per leggere i file delle versioni vecchie)
                Sale.Stampanti.Clear();
                Sale.Centri.Clear();
                Sale.StampantiSala.Clear();
                IP_Termica = "";
            }
            Stampe.Normalizza();
        }
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
        /// <summary>Gestione operatori e turni (PIN all'apertura del turno, permessi). Spenta = come prima, password amministratore.</summary>
        public bool AbilitaGestioneOperatori { get; set; }
        /// <summary>Fondo cassa chiesto all'apertura del turno (proposto quello del turno precedente).</summary>
        public bool ChiediFondoCassa { get; set; }
        /// <summary>Prelievi e versamenti di contanti durante il turno (menu 👤).</summary>
        public bool AbilitaMovimentiCassa { get; set; }
        /// <summary>Alla chiusura si indica il fondo lasciato per il turno dopo.</summary>
        public bool ChiediFondoLasciato { get; set; }
        /// <summary>Il turno non si chiude senza il conteggio dei contanti (0 = cassetto vuoto).</summary>
        public bool ConteggioObbligatorio { get; set; }

        // ── ⚙️ AVANZATE ──
        public int TimeoutConnessioneRT_ms { get; set; } = 5000;
        public int TimeoutRispostaRT_ms { get; set; } = 10000;
        /// <summary>DEBUG, INFO, WARN, ERROR.</summary>
        public string LivelloLog { get; set; } = "INFO";

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
            c.Stampe = Stampe?.Copia();
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

        private static readonly object _lockSalva = new object();

        /// <summary>Legge le impostazioni; se il file è vuoto o rovinato usa la copia .bak (come il PC), mai i predefiniti in silenzio.</summary>
        public Impostazioni Carica()
        {
            foreach (var file in new[] { _percorso, _percorso + ".bak" })
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    var imp = JsonSerializer.Deserialize(File.ReadAllText(file), JsonContesto.Default.Impostazioni);
                    if (imp == null) continue;
                    imp.PreparaStampe();
                    if (file != _percorso) LogManager.Warning("Impostazioni ripristinate dalla copia di sicurezza (.bak)");
                    return imp;
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Impostazioni non leggibili ({Path.GetFileName(file)}): {ex.Message}");
                }
            }
            var nuove = new Impostazioni();
            nuove.PreparaStampe();
            return nuove;
        }

        /// <summary>Scrittura sicura: file temporaneo scritto su disco e riletto, copia .bak della versione precedente, poi sostituzione.</summary>
        public void Salva(Impostazioni imp)
        {
            lock (_lockSalva)
            {
                string json = JsonSerializer.Serialize(imp, JsonContesto.Default.Impostazioni);
                string tmp = _percorso + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new StreamWriter(fs))
                {
                    w.Write(json);
                    w.Flush();
                    fs.Flush(true);
                }
                // verifica: il file scritto deve rileggersi uguale
                if (JsonSerializer.Deserialize(File.ReadAllText(tmp), JsonContesto.Default.Impostazioni) == null)
                    throw new IOException("Salvataggio impostazioni non verificato.");
                if (File.Exists(_percorso)) File.Copy(_percorso, _percorso + ".bak", overwrite: true);
                File.Move(tmp, _percorso, overwrite: true);
            }
            ImpostazioniFiscali.Correnti = imp.ComeImpostazioniFiscali();
            LogManager.SetLivelloMinimo(imp.LivelloLog);
        }
    }
}
