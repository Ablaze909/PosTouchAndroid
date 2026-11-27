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
    public partial class Carrello
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
        private int _listino = 1;
        /// <summary>Listino dei prezzi (1-4): al tavolo quello della sala, al banco 1. Le righe già battute non cambiano.</summary>
        public int Listino { get => _listino; set => _listino = Math.Clamp(value, 1, 4); }

        public RigaScontrino Aggiungi(Prodotto p, decimal quantita, int? prezzoCent, bool accorpa)
        {
            if (p == null) return null;
            int prezzo = prezzoCent ?? p.PrezzoListino(Listino);
            if (!RigaAmmessa(prezzo, quantita)) return null;

            if (accorpa)
            {
                // mai accorpare su una riga già comandata (la cucina riceverebbe la differenza sbagliata)
                // né su una riga di un'altra portata
                int portataDest = p.Subito && HaSeparatori ? 0 : PortataDestinazione();
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
                PrezzoOriginaleCentesimi = p.PrezzoListino(Listino),
                Quantita = quantita,
                CentroProduzioneId = p.CentroStampa
            };
            if (p.Subito && HaSeparatori) InserisciSubito(riga);   // ⚡ SUBITO automatico della categoria
            else InserisciArticolo(riga);
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
            if (!ImportoConVariantiValido(riga.PrezzoCentesimi, quantita, riga)) return false;
            riga.Quantita = quantita;
            Aggiornato();
            return true;
        }

        public bool CambiaPrezzo(RigaScontrino riga, int prezzoCent)
        {
            if (riga == null || riga.Tipo != TipoRiga.Prodotto || !RigaAmmessa(prezzoCent, riga.Quantita)) return false;
            if (FigliDi(riga).Any(f => f.Tipo == TipoRiga.Gusto) && !Combinazioni.Chiusa(riga))
            {
                UltimoErrore = "Chiudi prima la combinazione: il prezzo si può cambiare dopo.";
                return false;
            }
            if (!ImportoConVariantiValido(prezzoCent, riga.Quantita, riga)) return false;
            riga.PrezzoCentesimi = prezzoCent;
            Aggiornato();
            return true;
        }

        /// <summary>Articolo a peso con varianti a importo fisso: la riga deve restare sopra zero.</summary>
        private bool ImportoConVariantiValido(int prezzoCent, decimal quantita, RigaScontrino riga)
        {
            if (riga.SovrapprezzoCent == 0) return true;
            if ((int)Math.Round(prezzoCent * quantita, MidpointRounding.AwayFromZero) + riga.SovrapprezzoCent > 0) return true;
            UltimoErrore = "Con le varianti di questo articolo l'importo della riga andrebbe a zero o sotto.";
            return false;
        }

        /// <summary>Elimina le righe indicate. Una riga del blocco sconto elimina tutto il blocco.</summary>
        public void Rimuovi(IEnumerable<RigaScontrino> righe)
        {
            UltimoErrore = null;
            bool togliSconto = false;
            var stornate = new List<RigaScontrino>();
            // combinazioni: toccare un gusto o il padre toglie tutta la combinazione (padre + gusti), come il PC
            var daTogliere = new List<RigaScontrino>();
            var giaScelte = new HashSet<RigaScontrino>();   // ricerca veloce (con tante righe List.Contains rallentava)
            var varianti = righe.Where(r => r.Tipo == TipoRiga.Variante).ToList();
            foreach (var r in righe.ToList())
            {
                if (r.Tipo == TipoRiga.Variante) continue;   // varianti: tolte una per una più sotto
                var x = r.Tipo == TipoRiga.Gusto ? PadreDi(r) : r;
                if (x == null || !giaScelte.Add(x)) continue;
                daTogliere.Add(x);
                if (x.Tipo == TipoRiga.Prodotto)
                    foreach (var f in FigliDi(x)) if (giaScelte.Add(f)) daTogliere.Add(f);
            }
            string primoErrore = null;   // il primo rifiuto resta visibile anche se le varianti dopo passano
            foreach (var v in varianti.Where(v => !giaScelte.Contains(v)))
            {
                UltimoErrore = null;
                RimuoviVariante(v);
                primoErrore ??= UltimoErrore;
            }
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
            if (primoErrore != null) UltimoErrore = primoErrore;
            if (!HaArticoli) foreach (var sep in Righe.Where(x => x.Tipo == TipoRiga.Portata).ToList()) Righe.Remove(sep);
            Aggiornato();
            if (stornate.Count > 0) ComandateRimosse?.Invoke(stornate);
        }

        /// <summary>Sconto percentuale sul totale (come ScontoDialog percentuale del PC).</summary>
        public bool ScontoPercentuale(decimal percentuale)
        {
            UltimoErrore = null;
            if (percentuale <= 0 || percentuale >= 100 || HaSconto || !HaArticoli) return false;
            if (Math.Round(SubtotaleProdottiCent * Math.Round(percentuale * 100) / 10000m, MidpointRounding.AwayFromZero) < 1)
            {
                UltimoErrore = "Lo sconto vale meno di un centesimo.";
                return false;
            }
            AggiungiBloccoSconto(new RigaScontrino
            {
                Tipo = TipoRiga.Sconto,
                Descrizione = $"SCONTO {percentuale:F2}%",
                ScontoPercentualeCent = (int)Math.Round(percentuale * 100),
                Quantita = 1,
                RepartoFiscale = 0
            });
            return true;
        }

        /// <summary>Sconto a valore sul totale, in centesimi (come ScontoDialog a valore del PC).</summary>
        public bool ScontoValore(int importoCent)
        {
            UltimoErrore = null;
            if (importoCent <= 0 || importoCent >= SubtotaleProdottiCent || HaSconto) return false;
            AggiungiBloccoSconto(new RigaScontrino
            {
                Tipo = TipoRiga.Sconto,
                Descrizione = $"SCONTO {importoCent / 100m:F2} euro".Replace('.', ','),
                PrezzoCentesimi = -importoCent,
                Quantita = 1,
                RepartoFiscale = 0
            });
            return true;
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
        /// <summary>Al tavolo con le portate in uso (c'è almeno un separatore reale).</summary>
        private bool HaSeparatori => Righe.Any(Portate.IsSeparatoreReale);

        /// <summary>
        /// ⚡ SUBITO automatico: l'articolo va nella sezione SUBITO (creata se manca, prima del primo separatore)
        /// qualunque sia la portata scelta per gli articoli successivi, che non cambia.
        /// </summary>
        private void InserisciSubito(RigaScontrino r)
        {
            var primo = Righe.First(Portate.IsSeparatoreReale);
            int pos = Righe.IndexOf(primo);
            if (!Righe.Any(Portate.IsSubito))
            {
                // SUBITO in testa alle righe (come ScegliPortata), così anche gli articoli già battuti prima restano nella prima uscita
                Righe.Insert(pos, new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.NomeSubito, Quantita = 0 });
                pos++;
            }
            Righe.Insert(pos, r);
        }

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
