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

            ListaCategorie.Children.Clear();
            foreach (var c in categorie)
                ListaCategorie.Children.Add(CreaTastoCategoria(c));
            MostraArticoli();
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
                    LineBreakMode = LineBreakMode.WordWrap
                }
            };
            Tocco(tasto, () =>
            {
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
            _categoriaCorrente = IdPreferiti;
            MostraArticoli();
        }

        // ══════════════ COLONNA CENTRO: ARTICOLI ══════════════

        private void MostraArticoli()
        {
            var catalogo = ContestoCassa.Catalogo;
            var articoli = _categoriaCorrente == IdPreferiti
                ? catalogo.Preferiti().ToList()
                : catalogo.ProdottiDi(_categoriaCorrente).ToList();

            GrigliaArticoli.Children.Clear();
            foreach (var p in articoli)
                GrigliaArticoli.Children.Add(CreaTastoArticolo(p));

            MessaggioVuoto.IsVisible = articoli.Count == 0;
            MessaggioVuoto.Text = catalogo.Categorie.Count == 0
                ? "Nessuna categoria.\n\nCrea categorie e articoli da 🔧 → BACK OFFICE,\noppure importa il pos.db del PC da 🔧 → IMPOSTAZIONI → AVANZATE."
                : _categoriaCorrente == IdPreferiti ? "Nessun preferito.\nScegli una categoria a sinistra."
                : "Nessun articolo in questa categoria.";
        }

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
                Margin = new Thickness(4)
            }, 0, 0);
            griglia.Add(new Grid
            {
                BackgroundColor = Ui.C("#40000000"),
                Padding = new Thickness(4, 2),
                Children =
                {
                    new Label { Text = p.PrezzoDisplay, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = testo,
                                HorizontalTextAlignment = TextAlignment.Center }
                }
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
            NascondiResto();
            if (p.IsCombinazione)
            {
                _tastierino.Azzera();
                await MessaggioBox.Avviso("COMBINAZIONE", $"{p.Descrizione} è un articolo multi-gusto.\n\nLe combinazioni non sono ancora disponibili su Android.");
                return;
            }
            var (quantita, prezzoCent) = _tastierino.PrendiPerArticolo();
            if (p.VenditaAPeso)
            {
                // Vendita a peso (dal PC): quantità = peso, prezzo per unità di peso
                var peso = await TastierinoPrezzo.ChiediQuantita($"⚖️ PESO ({p.UnitaPeso}) — {p.Descrizione}");
                if (peso == null || peso <= 0) return;
                quantita = peso.Value;
            }
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
            if (riga != null) ListaRighe.ScrollTo(riga, position: ScrollToPosition.End, animate: false);
        }

        private void AggiungiDaBarcode(string codice)
        {
            // con un dialogo aperto, il popup CASSA o la stampa in corso il lettore non aggiunge articoli
            // (un articolo letto durante l'emissione verrebbe perso alla chiusura dello scontrino)
            if (HaLivelli || Attesa.IsVisible || OverlayPagamento.IsVisible || ContestoCassa.ServizioVendita.InCorso) return;
            var p = ContestoCassa.Catalogo.CercaBarcode(codice);
            if (p == null) _ = MessaggioBox.Avviso("ARTICOLO NON TROVATO", $"Nessun articolo con codice:\n{codice}");
            else _ = AggiungiProdottoAsync(p);
        }
    }
}
