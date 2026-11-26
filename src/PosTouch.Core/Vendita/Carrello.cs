using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;

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

        // ── Tavolo (portate e comande) ──
        /// <summary>Separatori portate configurati (null al banco): servono per il numero logico.</summary>
        public IList<string> SeparatoriPortate { get; set; }
        /// <summary>Portata dove inserire i prossimi articoli (null = in fondo; 0 = SUBITO).</summary>
        public int? PortataTarget { get; set; }
        /// <summary>Righe già comandate tolte dal carrello: la sessione tavolo le accumula per lo STORNO.</summary>
        public event Action<IReadOnlyList<RigaScontrino>> ComandateRimosse;

        /// <summary>Ricerca articolo per Id (impostata all'avvio con il catalogo): serve a ricalcolare le combinazioni.</summary>
        public static Func<int, Prodotto> RisolviProdotto { get; set; }

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
                // mai accorpare su una riga già comandata (la cucina riceverebbe la differenza sbagliata)
                // né su una riga di un'altra portata
                int portataDest = PortataDestinazione();
                var esistente = Righe.FirstOrDefault(r => r.Tipo == TipoRiga.Prodotto
                                                       && r.ProdottoId == p.Id
                                                       && r.PrezzoCentesimi == prezzo
                                                       && !r.Comandata
                                                       && r.Portata == portataDest
                                                       && string.IsNullOrEmpty(r.VariantiTesto)
                                                       && FigliDi(r).Count == 0);
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
                CategoriaNome = p.CategoriaNome ?? "",
                AliquotaIVA = p.AliquotaIVA,
                UnitaPeso = p.VenditaAPeso ? (string.IsNullOrWhiteSpace(p.UnitaPeso) ? "Kg" : p.UnitaPeso) : null,
                PrezzoCentesimi = prezzo,
                PrezzoOriginaleCentesimi = p.PrezzoCentesimi,
                Quantita = quantita,
                CentroProduzioneId = p.CentroStampa
            };
            InserisciArticolo(riga);
            Aggiornato();
            return riga;
        }

        public bool ImpostaQuantita(RigaScontrino riga, decimal quantita)
        {
            if (riga?.Comandata == true)
            {
                UltimoErrore = "Articolo già inviato in comanda: per cambiarlo eliminalo (verrà stornato) e battilo di nuovo.";
                return false;
            }
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
            var stornate = new List<RigaScontrino>();
            // combinazioni: toccare un gusto o il padre toglie tutta la combinazione (padre + gusti), come il PC
            var daTogliere = new List<RigaScontrino>();
            var varianti = righe.Where(r => r.Tipo == TipoRiga.Variante).ToList();
            foreach (var r in righe.ToList())
            {
                if (r.Tipo == TipoRiga.Variante) continue;   // varianti: tolte una per una più sotto
                var x = r.Tipo == TipoRiga.Gusto ? PadreDi(r) : r;
                if (x == null || daTogliere.Contains(x)) continue;
                daTogliere.Add(x);
                if (x.Tipo == TipoRiga.Prodotto)
                    foreach (var f in FigliDi(x)) if (!daTogliere.Contains(f)) daTogliere.Add(f);
            }
            foreach (var v in varianti.Where(v => !daTogliere.Contains(v))) RimuoviVariante(v);
            foreach (var r in daTogliere)
            {
                if (r.Tipo is TipoRiga.Gusto or TipoRiga.Variante) { Righe.Remove(r); continue; }
                if (r.Tipo == TipoRiga.Prodotto)
                {
                    if (Righe.Remove(r) && r.Comandata) stornate.Add(r);
                }
                else if (r.Tipo == TipoRiga.Portata) Righe.Remove(r);
                else togliSconto = true;
            }
            if (togliSconto || !HaArticoli) RimuoviBloccoSconto();
            if (!HaArticoli) foreach (var sep in Righe.Where(x => x.Tipo == TipoRiga.Portata).ToList()) Righe.Remove(sep);
            Aggiornato();
            if (stornate.Count > 0) ComandateRimosse?.Invoke(stornate);
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
            PortataTarget = null;
            Aggiornato();
        }

        /// <summary>Carica le righe di un tavolo (dall'archivio tavoli).</summary>
        public void Carica(IEnumerable<RigaScontrino> righe)
        {
            Righe.Clear();
            foreach (var r in righe) Righe.Add(r);
            PortataTarget = null;
            Aggiornato();
        }

        // ══════════════ COMBINAZIONI (gusti) ══════════════

        /// <summary>Riga padre di una combinazione, a prezzo 0 finché non viene chiusa (vedi <see cref="CombinazioneInCorso"/>).</summary>
        internal RigaScontrino AggiungiPadreCombinazione(Prodotto p, string descrizione)
        {
            var riga = new RigaScontrino
            {
                Tipo = TipoRiga.Prodotto, ProdottoId = p.Id, Descrizione = descrizione,
                Barcode = string.IsNullOrEmpty(p.Barcode) ? p.Codice : p.Barcode,
                RepartoFiscale = p.RepartoFiscale, CategoriaId = p.CategoriaId, CategoriaNome = p.CategoriaNome ?? "",
                AliquotaIVA = p.AliquotaIVA, PrezzoCentesimi = 0, PrezzoOriginaleCentesimi = p.PrezzoCentesimi,
                Quantita = 1, CentroProduzioneId = p.CentroStampa
            };
            InserisciArticolo(riga);
            Aggiornato();
            return riga;
        }

        private static bool IsFiglio(RigaScontrino r) => r.Tipo is TipoRiga.Gusto or TipoRiga.Variante;

        /// <summary>Aggiunge una riga figlia (gusto) dopo il padre e tutti i suoi figli.</summary>
        internal void AggiungiFiglio(RigaScontrino padre, RigaScontrino figlio)
        {
            int i = Righe.IndexOf(padre);
            if (i < 0) return;
            int pos = i + 1;
            while (pos < Righe.Count && IsFiglio(Righe[pos])) pos++;
            if (pos >= Righe.Count) Righe.Add(figlio); else Righe.Insert(pos, figlio);
            Aggiornato();
        }

        /// <summary>Inserisce una variante subito sotto la riga indicata (dopo le sue varianti già presenti).</summary>
        internal void InserisciVariante(RigaScontrino target, RigaScontrino variante)
        {
            int pos = Righe.IndexOf(target) + 1;
            if (pos <= 0) return;
            while (pos < Righe.Count && Righe[pos].Tipo == TipoRiga.Variante) pos++;
            if (pos >= Righe.Count) Righe.Add(variante); else Righe.Insert(pos, variante);
        }

        /// <summary>Togli l'ultimo gusto (e le sue varianti) del padre — tasto C durante la combinazione.</summary>
        internal bool TogliUltimoFiglio(RigaScontrino padre)
        {
            var figli = FigliDi(padre);
            var gusto = figli.LastOrDefault(f => f.Tipo == TipoRiga.Gusto);
            if (gusto == null) return false;
            foreach (var f in figli.Skip(figli.IndexOf(gusto))) Righe.Remove(f);
            Aggiornato();
            return true;
        }

        /// <summary>Gusti e varianti subito sotto l'articolo.</summary>
        public List<RigaScontrino> FigliDi(RigaScontrino padre)
        {
            var figli = new List<RigaScontrino>();
            int i = Righe.IndexOf(padre);
            if (i < 0) return figli;
            for (int k = i + 1; k < Righe.Count && IsFiglio(Righe[k]); k++) figli.Add(Righe[k]);
            return figli;
        }

        /// <summary>Articolo a cui appartiene un gusto o una variante.</summary>
        public RigaScontrino PadreDi(RigaScontrino figlio)
        {
            for (int k = Righe.IndexOf(figlio) - 1; k >= 0; k--)
            {
                if (Righe[k].Tipo == TipoRiga.Prodotto) return Righe[k];
                if (!IsFiglio(Righe[k])) break;
            }
            return null;
        }

        /// <summary>Riga a cui è attaccata una variante (l'articolo o il gusto subito sopra).</summary>
        public RigaScontrino ProprietarioDi(RigaScontrino variante)
        {
            for (int k = Righe.IndexOf(variante) - 1; k >= 0; k--)
                if (Righe[k].Tipo != TipoRiga.Variante) return Righe[k];
            return null;
        }

        /// <summary>RimuoviSingolaVariante del PC: toglie la variante e ne scala il prezzo (con il segno giusto).</summary>
        private void RimuoviVariante(RigaScontrino v)
        {
            var proprietario = ProprietarioDi(v);
            var padre = PadreDi(v);
            if (padre?.Comandata == true) { UltimoErrore = "Articolo già inviato in comanda: le varianti non si possono togliere."; return; }
            Righe.Remove(v);
            if (proprietario?.Tipo == TipoRiga.Prodotto)
            {
                proprietario.PrezzoCentesimi -= v.PrezzoOriginaleCentesimi;
                proprietario.VariantiTesto = Varianti.TogliTesto(proprietario.VariantiTesto, v.Descrizione);
            }
            else if (proprietario?.Tipo == TipoRiga.Gusto && padre != null && Combinazioni.Chiusa(padre))
                Combinazioni.Ricalcola(this, padre);
        }

        /// <summary>Ricalcola totali e lista dopo una modifica fatta da fuori (es. chiusura combinazione).</summary>
        internal void Notifica() => Aggiornato();

        // ══════════════ PORTATE ══════════════

        /// <summary>
        /// FUNZIONI → separatore (btnFunzioni_Click del PC): se esiste già diventa la destinazione dei prossimi
        /// articoli, altrimenti viene aggiunto in fondo. "SUBITO" va in testa. Restituisce la portata logica.
        /// </summary>
        public int ScegliPortata(string nome)
        {
            if (nome == null) return PortataTarget ?? 0;
            if (nome.Contains("SUBITO"))
            {
                PortataTarget = 0;
                bool presente = Righe.Any(Portate.IsSubito);
                if (!presente && Righe.Any(Portate.IsSeparatoreReale))
                {
                    int pos = Righe.IndexOf(Righe.First(Portate.IsSeparatoreReale));
                    Righe.Insert(pos, new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.NomeSubito, Quantita = 0 });
                }
                Aggiornato();
                return 0;
            }
            var esistente = Righe.FirstOrDefault(r => Portate.IsSeparatoreReale(r) &&
                                                      Portate.Nome(r).Equals(nome.Trim(), StringComparison.OrdinalIgnoreCase));
            if (esistente == null)
            {
                esistente = new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.Descrizione(nome), Quantita = 0 };
                var blocco = InizioBloccoSconto();
                if (blocco == null) Righe.Add(esistente); else Righe.Insert(Righe.IndexOf(blocco), esistente);
            }
            Aggiornato();
            PortataTarget = esistente.Portata;
            return esistente.Portata;
        }

        /// <summary>Riordino delle sezioni portata (alla chiusura del tavolo, come il PC).</summary>
        public void RiordinaPortate()
        {
            var blocco = Righe.Where(r => r.Tipo is TipoRiga.Separatore or TipoRiga.Subtotale or TipoRiga.Sconto).ToList();
            var area = Righe.Where(r => r.Tipo is TipoRiga.Prodotto or TipoRiga.Portata).ToList();
            var ordinate = Portate.Riordina(area, SeparatoriPortate);
            if (ordinate.SequenceEqual(area)) return;
            Righe.Clear();
            foreach (var r in ordinate.Concat(blocco)) Righe.Add(r);
            Aggiornato();
        }

        /// <summary>Portata che riceverà il prossimo articolo.</summary>
        private int PortataDestinazione()
        {
            if (PortataTarget.HasValue) return PortataTarget.Value;
            var ultimo = Righe.LastOrDefault(r => r.Tipo == TipoRiga.Portata);
            return ultimo == null || Portate.IsSubito(ultimo) ? 0 : ultimo.Portata;
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
            foreach (var r in Righe.Where(r => r.Tipo is TipoRiga.Separatore or TipoRiga.Subtotale or TipoRiga.Sconto).ToList())
                Righe.Remove(r);
        }

        private RigaScontrino InizioBloccoSconto() =>
            Righe.FirstOrDefault(x => x.Tipo is TipoRiga.Separatore or TipoRiga.Subtotale or TipoRiga.Sconto);

        // Gli articoli aggiunti dopo uno sconto vanno sopra il blocco SUBTOTALE/SCONTO;
        // al tavolo vanno in fondo alla sezione della portata scelta (PortataTarget).
        private void InserisciArticolo(RigaScontrino r)
        {
            var inizioBlocco = InizioBloccoSconto();
            int fine = inizioBlocco == null ? Righe.Count : Righe.IndexOf(inizioBlocco);
            int pos = fine;
            if (PortataTarget.HasValue && Righe.Any(x => x.Tipo == TipoRiga.Portata))
            {
                int target = PortataTarget.Value;
                int inizio = -1;
                if (target == 0)
                {
                    // SUBITO: prima del primo separatore reale
                    var primo = Righe.FirstOrDefault(Portate.IsSeparatoreReale);
                    if (primo != null) pos = Righe.IndexOf(primo);
                }
                else
                {
                    var sep = Righe.FirstOrDefault(x => Portate.IsSeparatoreReale(x) && x.Portata == target);
                    if (sep != null) inizio = Righe.IndexOf(sep);
                    if (inizio >= 0)
                    {
                        pos = fine;
                        for (int i = inizio + 1; i < fine; i++)
                            if (Righe[i].Tipo == TipoRiga.Portata) { pos = i; break; }
                    }
                }
            }
            if (pos >= Righe.Count) Righe.Add(r); else Righe.Insert(pos, r);
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

            if (Righe.Any(x => x.Tipo == TipoRiga.Portata)) Portate.Assegna(Righe, SeparatoriPortate);
            else foreach (var x in Righe) if (x.Tipo == TipoRiga.Prodotto) x.Portata = 0;

            for (int i = 0; i < Righe.Count; i++) Righe[i].Alternata = i % 2 == 1;
            Cambiato?.Invoke();
        }
    }
}
