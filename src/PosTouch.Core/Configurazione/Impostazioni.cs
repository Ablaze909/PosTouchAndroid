using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;

namespace PosTouch.Core.Configurazione
{
    /// <summary>Impostazioni della cassa Android, salvate in impostazioni.json nella cartella dati.</summary>
    public class Impostazioni
    {
        // ── Registratore telematico ──
        public TipoCassaFiscale TipoCassa { get; set; } = TipoCassaFiscale.RT3i;
        public string IP_RT { get; set; } = "192.168.1.100";
        public int Porta_RT { get; set; } = 1723;
        public int TimeoutConnessioneRT_ms { get; set; } = 5000;
        public int TimeoutRispostaRT_ms { get; set; } = 10000;

        // ── Comportamento vendita ──
        /// <summary>Raggruppa righe identiche (reparto+descrizione+prezzo) prima dell'invio all'RT.</summary>
        public bool AccorpaRigheScontrino { get; set; } = true;
        /// <summary>Apre il cassetto dopo ogni scontrino.</summary>
        public bool ApriCassetto { get; set; } = false;
        /// <summary>Numero operatore selezionato sull'RT a inizio scontrino.</summary>
        public int OperatoreRT { get; set; } = 1;

        /// <summary>
        /// Tasti reparto nella barra sotto, formato "numero=nome" separati da ";".
        /// Esempio: "1=BAR;2=PASTICCERIA;3=VARIE".
        /// </summary>
        public string TastiReparto { get; set; } = "1=REP 1;2=REP 2;3=REP 3;4=REP 4";

        public List<TastoReparto> ElencoTastiReparto()
        {
            var lista = new List<TastoReparto>();
            foreach (var parte in (TastiReparto ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = parte.Split('=', 2);
                if (!int.TryParse(kv[0].Trim(), out int n) || n < 1 || n > 99) continue;
                string nome = kv.Length > 1 && !string.IsNullOrWhiteSpace(kv[1]) ? kv[1].Trim() : $"REP {n}";
                lista.Add(new TastoReparto { Numero = n, Nome = nome });
            }
            return lista;
        }

        // ── Sicurezza ──
        /// <summary>PIN per entrare nelle impostazioni (vuoto = nessun PIN).</summary>
        public string PinImpostazioni { get; set; } = "";

        public ImpostazioniFiscali ComeImpostazioniFiscali() => new ImpostazioniFiscali
        {
            TimeoutConnessioneRT_ms = TimeoutConnessioneRT_ms,
            TimeoutRispostaRT_ms = TimeoutRispostaRT_ms
        };
    }

    public class TastoReparto
    {
        public int Numero { get; set; }
        public string Nome { get; set; } = "";
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
        }
    }
}
