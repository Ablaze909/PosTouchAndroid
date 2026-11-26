using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — Categorie (colonna sinistra), tasti articolo (centro) e lettore barcode.</summary>
    public partial class VenditaPage
    {
        // ══════════════ COLONNA SINISTRA: CATEGORIE ══════════════

        private void CaricaCategorie()
        {
            var categorie = ContestoCassa.Catalogo.Categorie;
            if (_categoriaCorrente != IdPreferiti && !categorie.Any(c => c.Id == _categoriaCorrente))
                _categoriaCorrente = IdPreferiti;
            if (_categoriaCorrente == IdPreferiti && !ContestoCassa.Catalogo.Preferiti().Any() && categorie.Count > 0)
                _categoriaCorrente = categorie[0].Id;

            ListaCategorie.BatchBegin();
            try
            {
                ListaCategorie.Children.Clear();
                _tastiCategoria.Clear();
                foreach (var c in categorie)
                    ListaCategorie.Children.Add(CreaTastoCategoria(c));
            }
            finally { ListaCategorie.BatchCommit(); }
            MostraArticoli();
        }

        private readonly Dictionary<int, Border> _tastiCategoria = new();
        private int _categoriaMostrata = int.MinValue;

        /// <summary>Categoria attiva con bordo scuro; al cambio di categoria la griglia articoli riparte dall'alto.</summary>
        private void EvidenziaCategoria()
        {
            foreach (var (id, b) in _tastiCategoria)
            {
                bool attiva = id == _categoriaCorrente;
                // spessore fisso (il testo non si ridispone); bordo bianco con ombra scura: si vede su ogni colore
                b.StrokeThickness = 4;
                b.Stroke = new SolidColorBrush(attiva ? Colors.White : Colors.Transparent);
                b.Shadow = attiva ? new Shadow { Brush = new SolidColorBrush(Colors.Black), Offset = new Point(0, 0), Radius = 6, Opacity = 0.8f } : null;
            }
            if (_categoriaMostrata != _categoriaCorrente)
            {
                _categoriaMostrata = _categoriaCorrente;
                if (ScrollArticoli.ScrollY > 0) _ = ScrollArticoli.ScrollToAsync(0, 0, false);
            }
        }

        private View CreaTastoCategoria(Categoria c)
        {
            var tasto = new Border
            {
                HeightRequest = Cfg.AltezzaCategoria,
                StrokeThickness = 0,
                BackgroundColor = Ui.ColoreSicuro(c.Colore, "#2196F3"),
                Padding = new Thickness(4),
                Content = new Label
                {
                    Text = c.TestoTasto,
                    FontSize = Math.Clamp(Cfg.FontSizeCategorie, 8, 20) + 2,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Ui.ColoreSicuro(c.ColoreTesto, "#000000"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap,
                    MaxLines = 3
                }
            };
            _tastiCategoria[c.Id] = tasto;
            Tocco(tasto, () =>
            {
                if (CombinazioneAperta()) return Task.CompletedTask;
                _categoriaCorrente = c.Id;
                MostraArticoli();
                return Task.CompletedTask;
            });
            return tasto;
        }

        private async void OnCategorieSu(object sender, EventArgs e)
            => await ScrollCategorie.ScrollToAsync(0, Math.Max(0, ScrollCategorie.ScrollY - Cfg.AltezzaCategoria * 4), true);

        private async void OnCategorieGiu(object sender, EventArgs e)
            => await ScrollCategorie.ScrollToAsync(0, ScrollCategorie.ScrollY + Cfg.AltezzaCategoria * 4, true);

        private void OnPreferiti(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            _categoriaCorrente = IdPreferiti;
            MostraArticoli();
        }

        // ══════════════ COLONNA CENTRO: ARTICOLI ══════════════

        private void MostraArticoli()
        {
            EvidenziaCategoria();
            var catalogo = ContestoCassa.Catalogo;
            var articoli = _combinazione != null ? _combinazione.Gusti.ToList()     // durante la combinazione: solo i gusti
                : _categoriaCorrente == IdPreferiti
                ? catalogo.Preferiti().ToList()
                : catalogo.ProdottiDi(_categoriaCorrente).ToList();

            // tasti già pronti per questa categoria (prestazioni): si ricreano solo se cambiano catalogo,
            // impostazioni o misura dei tasti; durante la combinazione i gusti si creano al momento
            List<View> tasti;
            var chiave = (ContestoCassa.Catalogo.Versione, ContestoCassa.VersioneImpostazioni, _latoTasto);
            if (chiave != _chiaveCacheTasti) { _cacheTasti.Clear(); _chiaveCacheTasti = chiave; }
            if (_combinazione != null) tasti = articoli.Select(CreaTastoArticolo).ToList();
            else if (!_cacheTasti.TryGetValue(_categoriaCorrente, out tasti))
                _cacheTasti[_categoriaCorrente] = tasti = articoli.Select(CreaTastoArticolo).ToList();

            GrigliaArticoli.BatchBegin();   // un solo ricalcolo della griglia invece di uno per tasto
            try
            {
                GrigliaArticoli.Children.Clear();
                foreach (var t in tasti) GrigliaArticoli.Children.Add(t);
            }
            finally { GrigliaArticoli.BatchCommit(); }

            MessaggioVuoto.IsVisible = articoli.Count == 0;
            MessaggioVuoto.Text = catalogo.Categorie.Count == 0
                ? "Nessuna categoria.\n\nCrea categorie e articoli da 🔧 → BACK OFFICE,\noppure importa il pos.db del PC da 🔧 → IMPOSTAZIONI → AVANZATE."
                : _categoriaCorrente == IdPreferiti ? "Nessun preferito.\nScegli una categoria a sinistra."
                : "Nessun articolo in questa categoria.";
        }

        private readonly Dictionary<int, List<View>> _cacheTasti = new();
        private (int, int, double) _chiaveCacheTasti = (-1, -1, -1);

        private View CreaTastoArticolo(Prodotto p)
        {
            var testo = Ui.ColoreSicuro(p.ColoreTesto, "#FFFFFF");
            var griglia = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
            griglia.Add(new Label
            {
                Text = p.Descrizione,
                FontSize = Math.Clamp(p.DimensioneFontTasto, 8, 20) * Math.Clamp(_latoTasto / 100, 0.9, 1.3),
                FontAttributes = FontAttributes.Bold,
                TextColor = testo,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 3,   // descrizioni lunghe: al massimo 3 righe, non tagliate sopra e sotto
                Margin = new Thickness(4)
            }, 0, 0);
            griglia.Add(new Label   // prezzo su fascia scura: una sola scritta invece di griglia + scritta
            {
                Text = p.PrezzoDisplay, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = testo,
                HorizontalTextAlignment = TextAlignment.Center, BackgroundColor = Ui.C("#40000000"), Padding = new Thickness(4, 2)
            }, 0, 1);

            var tasto = new Border
            {
                WidthRequest = _latoTasto,
                HeightRequest = _latoTasto,
                Margin = new Thickness(3),
                StrokeThickness = 0,
                BackgroundColor = Ui.ColoreSicuro(p.Colore, "#4CAF50"),
                Content = griglia
            };
            Tocco(tasto, async () =>
            {
                if (_modalitaModifica) await Navigazione.Apri(this, () => new ProdottoPage(p, p.CategoriaId));
                else await AggiungiProdottoAsync(p);
            });
            return tasto;
        }

        /// <summary>AggiungiProdotto del PC: quantità e prezzo € dalla tastiera, richiesta prezzo per articoli a 0.</summary>
        private async Task AggiungiProdottoAsync(Prodotto p)
        {
            if (Occupata) return;
            // l'autorizzazione del tasto € vale solo per il prezzo che è ancora sul tastierino, e per un solo articolo
            bool prezzoGiaAutorizzato = _prezzoAutorizzato && _tastierino.PrezzoPersonalizzato.HasValue;
            _prezzoAutorizzato = false;
            NascondiResto();
            if (_combinazione != null) { await AggiungiGustoAsync(p); return; }   // combinazione aperta: il tasto è un gusto
            if (Cfg.BloccoRigaSingola && Carrello.HaArticoli)
            {
                _tastierino.Azzera();
                await MessaggioBox.Avviso("RIGA SINGOLA",
                    "È consentito un solo prodotto per scontrino.\n\nChiudi lo scontrino corrente prima di aggiungere un altro articolo.");
                return;
            }
            if (p.IsCombinazione) { await AvviaCombinazioneAsync(p); return; }
            bool quantitaEsplicita = _tastierino.QuantitaBloccata;   // "150 X": nessun dubbio pezzi/prezzo
            var (quantita, prezzoCent) = _tastierino.PrendiPerArticolo();
            if (prezzoCent.HasValue && !Cfg.AbilitaCambioPrezzo)
            {
                // il prezzo battuto ("3 X 1,50") è un cambio prezzo: se disabilitato vale il prezzo di listino
                prezzoCent = null;
                await MessaggioBox.Avviso("CAMBIO PREZZO DISABILITATO", $"Il prezzo digitato è stato ignorato: {p.Descrizione} va al prezzo di listino.");
            }
            else if (prezzoCent.HasValue && !prezzoGiaAutorizzato && !await Accesso.VerificaAsync(Permesso.ModificaPrezzi, "CAMBIO PREZZO"))
            {
                prezzoCent = null;   // operatore senza permesso e nessuna autorizzazione: prezzo di listino
                await MessaggioBox.Avviso("CAMBIO PREZZO", $"Il prezzo digitato è stato ignorato: {p.Descrizione} va al prezzo di listino.");
            }
            if (Cfg.VirgolaImplicita && !quantitaEsplicita && prezzoCent == null && quantita > 99 && quantita == Math.Floor(quantita) && !p.VenditaAPeso)
            {
                // virgola implicita: "150" + articolo è quasi sempre un prezzo dimenticato senza €
                int scelta = await SceltaBox.Testi($"{quantita:0} — {p.Descrizione}",
                    new[] { $"{quantita:0} PEZZI", $"PREZZO € {quantita / 100m:0.00}" }, colonne: 2);
                if (scelta < 0) return;
                if (scelta == 1)
                {
                    if (!Cfg.AbilitaCambioPrezzo) { await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Cambio prezzo disabilitato dall'amministratore."); return; }
                    if (!await Accesso.VerificaAsync(Permesso.ModificaPrezzi, "CAMBIO PREZZO")) return;
                    prezzoCent = (int)quantita; quantita = 1;
                }
            }
            if (p.VenditaAPeso) { await AggiungiAPesoAsync(p, prezzoCent); return; }
            int prezzo = prezzoCent ?? p.PrezzoCentesimi;

            if (prezzo == 0)
            {
                if (!Cfg.RichiestaPrezzoArticoliZero)
                {
                    await MessaggioBox.Avviso("PREZZO ZERO", $"L'articolo {p.Descrizione} ha prezzo € 0,00.\n\nDigita il prezzo, premi € e poi l'articolo.");
                    return;
                }
                var v = await TastierinoPrezzo.ChiediEuro($"💶 PREZZO — {p.Descrizione}");
                if (v == null || v <= 0) return;
                prezzo = (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero);
            }

            bool accorpa = Cfg.AccorpaRigheVideo && !Cfg.BloccoModificaQuantita && !p.VenditaAPeso;
            var riga = Carrello.Aggiungi(p, quantita, prezzo, accorpa);
            if (riga == null)
            {
                await MessaggioBox.Avviso("ARTICOLO NON AGGIUNTO", $"{p.Descrizione}\n\n{Carrello.UltimoErrore}");
                return;
            }
            MostraInFondo(riga);
        }

        /// <summary>
        /// AggiungiProdottoAPeso del PC: prezzo al Kg/Etto (chiesto se è 0 o battuto con €), poi il peso;
        /// riga con quantità = peso e prezzo unitario.
        /// </summary>
        private async Task AggiungiAPesoAsync(Prodotto p, int? prezzoBattuto)
        {
            string unita = string.IsNullOrWhiteSpace(p.UnitaPeso) ? "Kg" : p.UnitaPeso;
            int prezzo = prezzoBattuto ?? p.PrezzoCentesimi;
            if (prezzo <= 0)
            {
                var v = await TastierinoPrezzo.ChiediEuro($"💶 PREZZO AL {unita.ToUpperInvariant()} — {p.Descrizione}");
                if (v == null || v <= 0) return;
                prezzo = (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero);
            }
            var peso = await TastierinoPrezzo.ChiediQuantita($"⚖️ PESO ({unita}) — {p.Descrizione}\n€ {prezzo / 100m:N2} al {unita}");
            if (peso == null || peso <= 0) return;
            var riga = Carrello.Aggiungi(p, peso.Value, prezzo, accorpa: false);
            if (riga == null) { await MessaggioBox.Avviso("ARTICOLO NON AGGIUNTO", $"{p.Descrizione}\n\n{Carrello.UltimoErrore}"); return; }
            MostraInFondo(riga);
        }

        /// <summary>Etichetta bilancia (EAN-13 "2…"): articolo dal codice, prezzo dall'etichetta (non dal listino), come il PC.</summary>
        private async Task AggiungiDaEtichettaBilanciaAsync(string barcode)
        {
            var l = BarcodeBilancia.Leggi(barcode);
            if (!l.CheckValido)
            {
                await MessaggioBox.Avviso("BARCODE NON VALIDO", $"Il codice bilancia scansionato non è valido (checksum errato).\n\nCodice: {barcode}\n\nRiscansionare o inserire manualmente.");
                return;
            }
            var p = ContestoCassa.Catalogo.CercaCodiceBilancia(l.CodiceProdotto);
            if (p == null)
            {
                await MessaggioBox.Avviso("CODICE BILANCIA NON TROVATO", $"Barcode bilancia rilevato ma codice prodotto non trovato:\n\nCodice prodotto: {l.CodiceProdotto}\n" +
                    $"Prezzo letto: € {l.PrezzoCentesimi / 100m:N2}\n\nCrea il prodotto in Back Office con questo codice\ne spunta 'Codice bilancia'.");
                return;
            }
            if (!p.IsCodiceBilancia)
            {
                await MessaggioBox.Avviso("PRODOTTO NON CONFIGURATO", $"Il prodotto '{p.Descrizione}' (codice {l.CodiceProdotto}) esiste\nma NON è configurato come codice bilancia.\n\n" +
                    "Vai in Back Office → Modifica Prodotto → spunta 'Codice bilancia'.");
                return;
            }
            var (qta, _) = _tastierino.PrendiPerArticolo();
            var riga = Carrello.Aggiungi(p, qta, l.PrezzoCentesimi, accorpa: false);
            if (riga == null) { await MessaggioBox.Avviso("ARTICOLO NON AGGIUNTO", $"{p.Descrizione}\n\n{Carrello.UltimoErrore}"); return; }
            riga.Barcode = barcode;   // nello storico l'etichetta letta
            MostraInFondo(riga);
        }

        private void AggiungiDaBarcode(string codice)
        {
            // con un dialogo aperto, il popup CASSA o la stampa in corso il lettore non aggiunge articoli
            // (un articolo letto durante l'emissione verrebbe perso alla chiusura dello scontrino)
            if (HaLivelli || Attesa.IsVisible || OverlayPagamento.IsVisible || ContestoCassa.ServizioVendita.InCorso || ContestoCassa.ServizioConto.InCorso
                || _modalitaModifica || _combinazione != null)
            {
                // lettura ignorata: almeno una vibrazione, così il cassiere sa di dover rileggere
                try { Microsoft.Maui.Devices.Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(250)); } catch { }
                return;
            }
            // prima il codice esatto: anche gli EAN interni/a pezzi che iniziano con 2 sono articoli normali
            var p = ContestoCassa.Catalogo.CercaBarcode(codice);
            if (p == null && Cfg.UsaBarcodeBilancia && BarcodeBilancia.IsBarcodeBilancia(codice)) { _ = AggiungiDaEtichettaBilanciaAsync(codice); return; }
            if (p == null) _ = MessaggioBox.Avviso("ARTICOLO NON TROVATO", $"Nessun articolo con codice:\n{codice}");
            else _ = AggiungiProdottoAsync(p);
        }
    }
}
