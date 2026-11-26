using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// Carrello della vendita al banco: righe prodotto + al massimo uno sconto sul totale.
    /// Nessuna dipendenza dalla UI: la pagina si limita a mostrare <see cref="Righe"/>.
    /// </summary>
    public class Carrello
    {
        public ObservableCollection<RigaScontrino> Righe { get; } = new ObservableCollection<RigaScontrino>();

        /// <summary>Notifica ogni modifica (per aggiornare totale e pulsanti).</summary>
        public event Action Cambiato;

        public bool Vuoto => !Righe.Any(r => r.Tipo == TipoRiga.Prodotto);

        public int SubtotaleProdottiCent => Righe.Where(r => r.Tipo == TipoRiga.Prodotto).Sum(r => r.ImportoCentesimi);
        public int TotaleCent => Righe.Sum(r => r.ImportoCentesimi);
        public decimal TotaleEuro => TotaleCent / 100m;

        public RigaScontrino Sconto => Righe.FirstOrDefault(r => r.Tipo == TipoRiga.Sconto);

        public void Aggiungi(Prodotto p, decimal quantita = 1)
        {
            if (p == null || quantita <= 0) return;

            // Se il prodotto è già presente allo stesso prezzo, aumenta la quantità
            var esistente = Righe.FirstOrDefault(r => r.Tipo == TipoRiga.Prodotto
                                                   && r.ProdottoId == p.Id
                                                   && r.PrezzoCentesimi == p.PrezzoCentesimi);
            if (esistente != null)
                esistente.Quantita += quantita;
            else
                InserisciProdotto(new RigaScontrino
                {
                    Tipo = TipoRiga.Prodotto,
                    ProdottoId = p.Id,
                    Descrizione = p.Descrizione,
                    Barcode = string.IsNullOrEmpty(p.Barcode) ? p.Codice : p.Barcode,
                    RepartoFiscale = p.RepartoFiscale,
                    CategoriaId = p.CategoriaId,
                    PrezzoCentesimi = p.PrezzoCentesimi,
                    PrezzoOriginaleCentesimi = p.PrezzoCentesimi,
                    Quantita = quantita
                });
            Aggiornato();
        }

        /// <summary>Vendita a reparto con importo libero (senza prodotto in catalogo).</summary>
        public void AggiungiLibero(string descrizione, int reparto, int prezzoCent, decimal quantita = 1)
        {
            if (prezzoCent <= 0 || reparto <= 0 || quantita <= 0) return;
            InserisciProdotto(new RigaScontrino
            {
                Tipo = TipoRiga.Prodotto,
                Descrizione = string.IsNullOrWhiteSpace(descrizione) ? $"REPARTO {reparto}" : descrizione.Trim(),
                RepartoFiscale = reparto,
                PrezzoCentesimi = prezzoCent,
                PrezzoOriginaleCentesimi = prezzoCent,
                Quantita = quantita
            });
            Aggiornato();
        }

        public void CambiaQuantita(RigaScontrino riga, decimal delta)
        {
            if (riga == null || riga.Tipo != TipoRiga.Prodotto) return;
            decimal nuova = riga.Quantita + delta;
            if (nuova <= 0) Righe.Remove(riga);
            else riga.Quantita = nuova;
            Aggiornato();
        }

        public void Rimuovi(RigaScontrino riga)
        {
            if (riga == null) return;
            Righe.Remove(riga);
            Aggiornato();
        }

        /// <summary>Sconto percentuale sul totale (sostituisce un eventuale sconto già presente).</summary>
        public void ScontoPercentuale(decimal percentuale)
        {
            if (percentuale <= 0 || percentuale >= 100) return;
            RimuoviSconto(notifica: false);
            Righe.Add(new RigaScontrino
            {
                Tipo = TipoRiga.Sconto,
                Descrizione = $"SCONTO {percentuale:0.##}%",
                ScontoPercentualeCent = (int)Math.Round(percentuale * 100),
                Quantita = 1
            });
            Aggiornato();
        }

        /// <summary>Sconto a valore sul totale, in centesimi (positivo).</summary>
        public void ScontoValore(int importoCent)
        {
            if (importoCent <= 0 || importoCent >= SubtotaleProdottiCent) return;
            RimuoviSconto(notifica: false);
            Righe.Add(new RigaScontrino
            {
                Tipo = TipoRiga.Sconto,
                Descrizione = $"SCONTO € {importoCent / 100m:N2}",
                PrezzoCentesimi = -importoCent,
                Quantita = 1
            });
            Aggiornato();
        }

        public void RimuoviSconto(bool notifica = true)
        {
            var s = Sconto;
            if (s != null) Righe.Remove(s);
            if (notifica) Aggiornato();
        }

        public void Svuota()
        {
            Righe.Clear();
            Aggiornato();
        }

        /// <summary>Copia delle righe da usare per l'emissione (il carrello può cambiare nel frattempo).</summary>
        public List<RigaScontrino> Fotografia() => Righe.Select(r => r.Copia()).ToList();

        // Le righe prodotto stanno sempre prima dello sconto
        private void InserisciProdotto(RigaScontrino r)
        {
            var s = Sconto;
            if (s == null) Righe.Add(r);
            else Righe.Insert(Righe.IndexOf(s), r);
        }

        private void Aggiornato()
        {
            // Lo sconto percentuale va ricalcolato a ogni modifica dei prodotti
            var s = Sconto;
            if (s?.ScontoPercentualeCent is int pc)
                s.PrezzoCentesimi = -(int)Math.Round(SubtotaleProdottiCent * pc / 10000m, MidpointRounding.AwayFromZero);
            if (Vuoto && s != null) Righe.Remove(s);
            Cambiato?.Invoke();
        }
    }
}
