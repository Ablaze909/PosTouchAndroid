using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// Scontrino in corso, con le stesse regole di MainWindow (PC):
    /// - righe articolo; uno sconto alla volta, mostrato come ═══ / SUBTOTALE / ─── / SCONTO
    /// - il totale conta solo articoli e sconto
    /// Nessuna dipendenza dalla UI: la pagina mostra <see cref="Righe"/>.
    /// </summary>
    public class Carrello
    {
        private const string Riga1 = "═══════════════════════════";
        private const string Riga2 = "───────────────────────────";

        public ObservableCollection<RigaScontrino> Righe { get; } = new ObservableCollection<RigaScontrino>();

        /// <summary>Notifica ogni modifica (per aggiornare totale e pezzi).</summary>
        public event Action Cambiato;

        /// <summary>Quantità massima per riga (come la modifica quantità del PC: 1–999).</summary>
        public const decimal MaxQuantita = 999;
        /// <summary>Importo massimo di una riga: € 99.999,99.</summary>
        public const int MaxImportoRigaCent = 9_999_999;

        /// <summary>Motivo dell'ultimo rifiuto di Aggiungi/ImpostaQuantita/CambiaPrezzo (null = nessun errore).</summary>
        public string UltimoErrore { get; private set; }

        /// <summary>Controlla quantità e importo di una riga prima di accettarla.</summary>
        public bool RigaAmmessa(int prezzoCent, decimal quantita)
        {
            UltimoErrore = null;
            if (quantita <= 0 || quantita > MaxQuantita)
                UltimoErrore = $"La quantità deve essere tra 1 e {MaxQuantita:0}.";
            else if (prezzoCent <= 0)
                UltimoErrore = "Il prezzo deve essere maggiore di zero.";
            else if (prezzoCent * quantita > MaxImportoRigaCent)
                UltimoErrore = "Importo della riga troppo alto (massimo € 99.999,99).";
            else if (Math.Round(prezzoCent * quantita, MidpointRounding.AwayFromZero) < 1)
                UltimoErrore = "Importo della riga inferiore a un centesimo.";
            return UltimoErrore == null;
        }

        public bool Vuoto => Righe.Count == 0;
        public bool HaArticoli => Righe.Any(r => r.Tipo == TipoRiga.Prodotto);
        public bool HaSconto => Righe.Any(r => r.Tipo == TipoRiga.Sconto);

        public int SubtotaleProdottiCent => Righe.Where(r => r.Tipo == TipoRiga.Prodotto).Sum(r => r.ImportoCentesimi);
        public int TotaleCent => Righe.Where(r => r.ContaNelTotale).Sum(r => r.ImportoCentesimi);
        public decimal TotaleEuro => TotaleCent / 100m;

        /// <summary>Pezzi come sul PC: somma delle quantità intere degli articoli.</summary>
        public int Pezzi => Righe.Where(r => r.Tipo == TipoRiga.Prodotto).Sum(r => (int)r.Quantita);

        public RigaScontrino Sconto => Righe.FirstOrDefault(r => r.Tipo == TipoRiga.Sconto);
        public RigaScontrino UltimaRigaProdotto => Righe.LastOrDefault(r => r.Tipo == TipoRiga.Prodotto);

        /// <param name="prezzoCent">prezzo battuto (tasto €), null = prezzo di listino</param>
        /// <param name="accorpa">true = se c'è già lo stesso articolo allo stesso prezzo aumenta la quantità</param>
        public RigaScontrino Aggiungi(Prodotto p, decimal quantita, int? prezzoCent, bool accorpa)
        {
            if (p == null) return null;
            int prezzo = prezzoCent ?? p.PrezzoCentesimi;
            if (!RigaAmmessa(prezzo, quantita)) return null;

            if (accorpa)
            {
                var esistente = Righe.FirstOrDefault(r => r.Tipo == TipoRiga.Prodotto
                                                       && r.ProdottoId == p.Id
                                                       && r.PrezzoCentesimi == prezzo);
                if (esistente != null)
                {
                    if (!RigaAmmessa(prezzo, esistente.Quantita + quantita)) return null;
                    esistente.Quantita += quantita;
                    Aggiornato();
                    return esistente;
                }
            }

            var riga = new RigaScontrino
            {
                Tipo = TipoRiga.Prodotto,
                ProdottoId = p.Id,
                Descrizione = p.Descrizione,
                Barcode = string.IsNullOrEmpty(p.Barcode) ? p.Codice : p.Barcode,
                RepartoFiscale = p.RepartoFiscale,
                CategoriaId = p.CategoriaId,
                PrezzoCentesimi = prezzo,
                PrezzoOriginaleCentesimi = p.PrezzoCentesimi,
                Quantita = quantita
            };
            InserisciArticolo(riga);
            Aggiornato();
            return riga;
        }

        public bool ImpostaQuantita(RigaScontrino riga, decimal quantita)
        {
            if (riga == null || riga.Tipo != TipoRiga.Prodotto || !RigaAmmessa(riga.PrezzoCentesimi, quantita)) return false;
            riga.Quantita = quantita;
            Aggiornato();
            return true;
        }

        public bool CambiaPrezzo(RigaScontrino riga, int prezzoCent)
        {
            if (riga == null || riga.Tipo != TipoRiga.Prodotto || !RigaAmmessa(prezzoCent, riga.Quantita)) return false;
            riga.PrezzoCentesimi = prezzoCent;
            Aggiornato();
            return true;
        }

        /// <summary>Elimina le righe indicate. Una riga del blocco sconto elimina tutto il blocco.</summary>
        public void Rimuovi(IEnumerable<RigaScontrino> righe)
        {
            bool togliSconto = false;
            foreach (var r in righe.ToList())
            {
                if (r.Tipo == TipoRiga.Prodotto) Righe.Remove(r);
                else togliSconto = true;
            }
            if (togliSconto || !HaArticoli) RimuoviBloccoSconto();
            Aggiornato();
        }

        /// <summary>Sconto percentuale sul totale (come ScontoDialog percentuale del PC).</summary>
        public void ScontoPercentuale(decimal percentuale)
        {
            if (percentuale <= 0 || percentuale >= 100 || HaSconto || !HaArticoli) return;
            AggiungiBloccoSconto(new RigaScontrino
            {
                Tipo = TipoRiga.Sconto,
                Descrizione = $"SCONTO {percentuale:F2}%",
                ScontoPercentualeCent = (int)Math.Round(percentuale * 100),
                Quantita = 1,
                RepartoFiscale = 0
            });
        }

        /// <summary>Sconto a valore sul totale, in centesimi (come ScontoDialog a valore del PC).</summary>
        public void ScontoValore(int importoCent)
        {
            if (importoCent <= 0 || importoCent >= SubtotaleProdottiCent || HaSconto) return;
            AggiungiBloccoSconto(new RigaScontrino
            {
                Tipo = TipoRiga.Sconto,
                Descrizione = $"SCONTO {importoCent / 100m:F2} euro".Replace('.', ','),
                PrezzoCentesimi = -importoCent,
                Quantita = 1,
                RepartoFiscale = 0
            });
        }

        public void Svuota()
        {
            Righe.Clear();
            Aggiornato();
        }

        /// <summary>Copia delle righe da usare per l'emissione (il carrello può cambiare nel frattempo).</summary>
        public List<RigaScontrino> Fotografia() => Righe.Select(r => r.Copia()).ToList();

        // ══════════════ INTERNI ══════════════

        private void AggiungiBloccoSconto(RigaScontrino sconto)
        {
            Righe.Add(new RigaScontrino { Tipo = TipoRiga.Separatore, Descrizione = Riga1, RepartoFiscale = -1 });
            Righe.Add(new RigaScontrino { Tipo = TipoRiga.Subtotale, Descrizione = "SUBTOTALE", RepartoFiscale = -1 });
            Righe.Add(new RigaScontrino { Tipo = TipoRiga.Separatore, Descrizione = Riga2, RepartoFiscale = -1 });
            Righe.Add(sconto);
            Aggiornato();
        }

        private void RimuoviBloccoSconto()
        {
            foreach (var r in Righe.Where(r => r.Tipo != TipoRiga.Prodotto).ToList())
                Righe.Remove(r);
        }

        // Gli articoli aggiunti dopo uno sconto vanno sopra il blocco SUBTOTALE/SCONTO
        private void InserisciArticolo(RigaScontrino r)
        {
            var inizioBlocco = Righe.FirstOrDefault(x => x.Tipo != TipoRiga.Prodotto);
            if (inizioBlocco == null) Righe.Add(r);
            else Righe.Insert(Righe.IndexOf(inizioBlocco), r);
        }

        private void Aggiornato()
        {
            int sub = SubtotaleProdottiCent;
            var subtot = Righe.FirstOrDefault(r => r.Tipo == TipoRiga.Subtotale);
            if (subtot != null) subtot.PrezzoCentesimi = sub;

            // Sconto a valore diventato maggiore del subtotale (articoli tolti): il blocco sconto non ha più senso
            var s = Sconto;
            if (s != null && !s.ScontoPercentualeCent.HasValue && (sub <= 0 || -s.PrezzoCentesimi >= sub))
            {
                RimuoviBloccoSconto();
                s = null;
                subtot = null;
            }
            // Sconto percentuale ricalcolato sul subtotale attuale (l'RT lo applica sul totale finale)
            if (s?.ScontoPercentualeCent is int pc)
                s.PrezzoCentesimi = -(int)Math.Round(sub * pc / 10000m, MidpointRounding.AwayFromZero);

            for (int i = 0; i < Righe.Count; i++) Righe[i].Alternata = i % 2 == 1;
            Cambiato?.Invoke();
        }
    }
}
