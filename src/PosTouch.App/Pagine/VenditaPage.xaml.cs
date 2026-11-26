using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Vendita al banco, stesso layout e comportamento di MainWindow (POS Touch Windows).
    /// La pagina mostra e raccoglie i tocchi: la logica sta in Carrello, Tastierino e ServizioVendita (Core).
    /// </summary>
    public partial class VenditaPage : ContentPage
    {
        private const int IdPreferiti = -1;
        private const int LatoTasto = 100;        // tasti articolo 100x100 come sul PC
        private const int AltezzaCategoria = 64;

        private readonly Tastierino _tastierino = new Tastierino();
        private readonly IDispatcherTimer _orologio;
        private IDispatcherTimer _timerResto;
        private IDispatcherTimer _timerMessaggio;
        private int _categoriaCorrente = IdPreferiti;
        private bool _modalitaModifica;

        private Carrello Carrello => ContestoCassa.Carrello;

        public VenditaPage()
        {
            InitializeComponent();
            ListaRighe.ItemsSource = Carrello.Righe;
            Carrello.Cambiato += AggiornaTotale;
            _tastierino.Cambiato += AggiornaDisplay;

            _orologio = Dispatcher.CreateTimer();
            _orologio.Interval = TimeSpan.FromSeconds(1);
            _orologio.Tick += (s, e) => AggiornaOrologio();
            AggiornaOrologio();
            AggiornaTotale();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orologio.Start();
            CaricaCategorie();   // il catalogo può essere cambiato in Back office / modifica
            CreaBarraPagamento();
            LettoreBarcode.Destinatario = AggiungiDaBarcode;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _orologio.Stop();
            LettoreBarcode.Destinatario = null;
        }

        private void AggiornaOrologio()
        {
            Orologio.Text = DateTime.Now.ToString("HH:mm:ss");
            Data.Text = DateTime.Now.ToString("dd/MM/yyyy");
        }

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

            if (_modalitaModifica)
                ListaCategorie.Children.Add(CreaTastoNuovo("➕\nCATEGORIA", AltezzaCategoria, -1, async () =>
                    await Navigation.PushAsync(new CategoriaPage(null))));

            MostraArticoli();
        }

        private View CreaTastoCategoria(Categoria c)
        {
            bool selezionata = c.Id == _categoriaCorrente;
            var tasto = new Border
            {
                HeightRequest = AltezzaCategoria,
                StrokeThickness = selezionata ? 3 : 0,
                Stroke = new SolidColorBrush(Colors.Black),
                BackgroundColor = Colore(c.Colore, "#2196F3"),
                Padding = new Thickness(4),
                Content = new Label
                {
                    Text = c.Nome,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colore(c.ColoreTesto, "#000000"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap,
                    MaxLines = 3
                }
            };
            Tocco(tasto, async () =>
            {
                // In modifica: secondo tocco sulla categoria già aperta = modifica categoria
                if (_modalitaModifica && c.Id == _categoriaCorrente)
                {
                    await Navigation.PushAsync(new CategoriaPage(c));
                    return;
                }
                _categoriaCorrente = c.Id;
                CaricaCategorie();
            });
            return tasto;
        }

        private async void OnCategorieSu(object sender, EventArgs e)
            => await ScrollCategorie.ScrollToAsync(0, Math.Max(0, ScrollCategorie.ScrollY - AltezzaCategoria * 4), true);

        private async void OnCategorieGiu(object sender, EventArgs e)
            => await ScrollCategorie.ScrollToAsync(0, ScrollCategorie.ScrollY + AltezzaCategoria * 4, true);

        private void OnPreferiti(object sender, EventArgs e)
        {
            _categoriaCorrente = IdPreferiti;
            CaricaCategorie();
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

            if (_modalitaModifica && _categoriaCorrente != IdPreferiti)
                GrigliaArticoli.Children.Add(CreaTastoNuovo("➕\nNUOVO\nARTICOLO", LatoTasto, LatoTasto, async () =>
                    await Navigation.PushAsync(new ProdottoPage(null, _categoriaCorrente))));

            MessaggioVuoto.IsVisible = GrigliaArticoli.Children.Count == 0;
            if (catalogo.Categorie.Count == 0)
                MessaggioVuoto.Text = "Nessuna categoria.\n\nCreale con ✏️ (modalità modifica) o da 🔧 → BACK OFFICE,\noppure importa il pos.db del PC da 🔧 → IMPOSTAZIONI.\n\nIntanto puoi vendere con i tasti reparto in basso.";
            else if (_categoriaCorrente == IdPreferiti)
                MessaggioVuoto.Text = "Nessun preferito.\nScegli una categoria a sinistra.";
            else
                MessaggioVuoto.Text = "Nessun articolo in questa categoria.\nAggiungili con ✏️.";
        }

        private View CreaTastoArticolo(Prodotto p)
        {
            var testo = Colore(p.ColoreTesto, "#FFFFFF");
            var griglia = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
            griglia.Add(new Label
            {
                Text = p.Descrizione,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = testo,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 4,
                Margin = new Thickness(4)
            }, 0, 0);
            griglia.Add(new Grid
            {
                BackgroundColor = Color.FromArgb("#40000000"),
                Padding = new Thickness(4, 2),
                Children =
                {
                    new Label
                    {
                        Text = $"€ {p.PrezzoEuro:F2}".Replace('.', ','),
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = testo,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }, 0, 1);

            var tasto = new Border
            {
                WidthRequest = LatoTasto,
                HeightRequest = LatoTasto,
                Margin = new Thickness(3),
                StrokeThickness = 0,
                BackgroundColor = Colore(p.Colore, "#455A64"),
                Content = griglia
            };
            Tocco(tasto, async () =>
            {
                if (_modalitaModifica) await Navigation.PushAsync(new ProdottoPage(p, p.CategoriaId));
                else await AggiungiArticoloAsync(p);
            });
            return tasto;
        }

        private View CreaTastoNuovo(string testo, int altezza, int larghezza, Func<Task> azione)
        {
            var tasto = new Border
            {
                HeightRequest = altezza,
                Margin = larghezza > 0 ? new Thickness(3) : new Thickness(0),
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(Color.FromArgb("#FF9800")),
                StrokeDashArray = new DoubleCollection { 4, 2 },
                BackgroundColor = Color.FromArgb("#FFF3E0"),
                Content = new Label
                {
                    Text = testo,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#E65100"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };
            if (larghezza > 0) tasto.WidthRequest = larghezza;
            Tocco(tasto, azione);
            return tasto;
        }

        /// <summary>
        /// Articolo a prezzo fisso: le cifre digitate sono la quantità.
        /// Articolo a prezzo zero (o cifre dopo X): le cifre digitate sono il prezzo.
        /// </summary>
        private async Task AggiungiArticoloAsync(Prodotto p)
        {
            if (p.PrezzoCentesimi > 0 && !(_tastierino.QuantitaBloccata.HasValue && !_tastierino.Vuoto))
            {
                Carrello.Aggiungi(p, _tastierino.PrendiQuantita());
                SelezionaUltima();
                return;
            }

            if (!_tastierino.PrendiImporto(out int prezzo))
            {
                string v = await DisplayPromptAsync(p.Descrizione, "Prezzo", "Aggiungi", "Annulla", "0,00", keyboard: Keyboard.Numeric);
                if (v == null) return;
                if (!Importi.ProvaCentesimi(v, out prezzo))
                {
                    Messaggio("Prezzo non valido");
                    return;
                }
            }
            Carrello.Aggiungi(p, _tastierino.PrendiQuantitaBloccata(), prezzo);
            SelezionaUltima();
        }

        private void AggiungiDaBarcode(string codice)
        {
            var p = ContestoCassa.Catalogo.CercaBarcode(codice);
            if (p == null) Messaggio($"Codice {codice} non trovato");
            else _ = AggiungiArticoloAsync(p);
        }

        // ══════════════ TOOLBAR ══════════════

        private async void OnStrumenti(object sender, EventArgs e)
        {
            if (await Accesso.VerificaPinAsync(this, "Strumenti"))
                await Navigation.PushAsync(new StrumentiPage());
        }

        private async void OnModifica(object sender, EventArgs e)
        {
            if (!_modalitaModifica && !await Accesso.VerificaPinAsync(this, "Modalità modifica")) return;
            _modalitaModifica = !_modalitaModifica;
            BtnModifica.BackgroundColor = Color.FromArgb(_modalitaModifica ? "#FF9800" : "#757575");
            OverlayModifica.IsVisible = _modalitaModifica;
            CaricaCategorie();
        }

        private async void OnFunzioniCassa(object sender, EventArgs e)
            => await Navigation.PushAsync(new FunzioniCassaPage());

        private async void OnEsci(object sender, EventArgs e)
        {
            if (!Carrello.Vuoto)
            {
                await DisplayAlert("ESCI", "Lo scontrino non è vuoto.\nChiuderlo o annullarlo prima di uscire.", "OK");
                return;
            }
            if (await DisplayAlert("ESCI", "Chiudere POS Touch?", "Esci", "Annulla"))
                Application.Current?.Quit();
        }

        // ══════════════ BARRA INFERIORE ══════════════

        /// <summary>💰 CASSA (doppia), 📂 CASSETTO, 🔍 BARCODE e i tasti reparto configurati.</summary>
        private void CreaBarraPagamento()
        {
            BarraPagamento.Children.Clear();
            BarraPagamento.ColumnDefinitions.Clear();

            var tasti = new List<(Button tasto, int colonne)>
            {
                (TastoBarra("💰 CASSA", "#388E3C", (s, e) => OnCassa()), 2),
                (TastoBarra("📂 CASSETTO", "#FF9800", OnCassetto), 1),
                (TastoBarra("🔍 BARCODE", "#795548", OnBarcode), 1)
            };
            foreach (var t in ContestoCassa.Impostazioni.ElencoTastiReparto())
            {
                var rep = t;
                tasti.Add((TastoBarra(rep.Nome, "#37474F", async (s, e) => await VendiRepartoAsync(rep.Numero, rep.Nome)), 1));
            }
            tasti.Add((TastoBarra("REP…", "#546E7A", OnAltroReparto), 1));

            int col = 0;
            foreach (var (tasto, colonne) in tasti)
            {
                for (int i = 0; i < colonne; i++) BarraPagamento.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                BarraPagamento.Add(tasto, col, 0);
                Grid.SetColumnSpan(tasto, colonne);
                col += colonne;
            }
        }

        private static Button TastoBarra(string testo, string colore, EventHandler azione)
        {
            var b = new Button
            {
                Text = testo,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb(colore),
                CornerRadius = 0,
                Padding = new Thickness(2, 0),
                Margin = new Thickness(2),
                LineBreakMode = LineBreakMode.TailTruncation
            };
            b.Clicked += azione;
            return b;
        }

        private void OnCassa()
        {
            if (Carrello.Vuoto)
            {
                DisplayAlert("ATTENZIONE", "Lo scontrino è vuoto!", "OK");
                return;
            }
            OverlayPagamento.IsVisible = true;
        }

        private void OnChiudiPagamento(object sender, EventArgs e) => OverlayPagamento.IsVisible = false;

        private async void OnCassetto(object sender, EventArgs e)
        {
            var esito = await Task.Run(() => ContestoCassa.FunzioniCassa.ApriCassettoAsync());
            if (!esito.Ok) await DisplayAlert("CASSETTO", esito.Messaggio, "OK");
        }

        private async void OnBarcode(object sender, EventArgs e)
        {
            string codice = await DisplayPromptAsync("🔍 BARCODE", "Codice o barcode dell'articolo", "Cerca", "Annulla", keyboard: Keyboard.Numeric);
            if (!string.IsNullOrWhiteSpace(codice)) AggiungiDaBarcode(codice.Trim());
        }

        /// <summary>Importo digitato sul tastierino (o chiesto) venduto sul reparto.</summary>
        private async Task VendiRepartoAsync(int reparto, string nome)
        {
            if (!_tastierino.PrendiImporto(out int cent))
            {
                string v = await DisplayPromptAsync(nome, $"Importo sul reparto {reparto}", "Aggiungi", "Annulla", "0,00", keyboard: Keyboard.Numeric);
                if (v == null) return;
                if (!Importi.ProvaCentesimi(v, out cent))
                {
                    Messaggio("Importo non valido");
                    return;
                }
            }
            Carrello.AggiungiLibero(nome, reparto, cent, _tastierino.PrendiQuantitaBloccata());
            SelezionaUltima();
        }

        private async void OnAltroReparto(object sender, EventArgs e)
        {
            string r = await DisplayPromptAsync("REPARTO", "Numero reparto del registratore", "Avanti", "Annulla",
                maxLength: 2, keyboard: Keyboard.Numeric, initialValue: "1");
            if (r == null) return;
            if (!int.TryParse(r, out int reparto) || reparto <= 0)
            {
                Messaggio("Reparto non valido");
                return;
            }
            await VendiRepartoAsync(reparto, $"REPARTO {reparto}");
        }

        // ══════════════ TASTIERA ══════════════

        private void OnCifra(object sender, EventArgs e) => _tastierino.Premi(((Button)sender).Text);
        private void OnCancella(object sender, EventArgs e) => _tastierino.Cancella();

        private void OnPer(object sender, EventArgs e)
        {
            if (!_tastierino.Moltiplica()) Messaggio("Digita la quantità, poi X");
        }

        /// <summary>Tasto €: il numero digitato diventa il prezzo della riga selezionata (o dell'ultima).</summary>
        private async void OnEuro(object sender, EventArgs e)
        {
            var riga = RigaSelezionata() ?? Carrello.UltimaRigaProdotto;
            if (riga == null || riga.Tipo != TipoRiga.Prodotto)
            {
                Messaggio("Nessuna riga a cui cambiare prezzo");
                return;
            }
            if (!_tastierino.PrendiImporto(out int cent))
            {
                string v = await DisplayPromptAsync("CAMBIA PREZZO", riga.Descrizione, "OK", "Annulla",
                    initialValue: (riga.PrezzoCentesimi / 100m).ToString("0.00"), keyboard: Keyboard.Numeric);
                if (v == null) return;
                if (!Importi.ProvaCentesimi(v, out cent))
                {
                    Messaggio("Prezzo non valido");
                    return;
                }
            }
            Carrello.CambiaPrezzo(riga, cent);
        }

        private async void OnScontoValore(object sender, EventArgs e)
        {
            if (Carrello.Vuoto) return;
            if (!_tastierino.PrendiImporto(out int cent))
            {
                string v = await DisplayPromptAsync("€ SCONTO", "Importo da scontare sul totale", "OK", "Annulla", "1,00", keyboard: Keyboard.Numeric);
                if (v == null) return;
                Importi.ProvaCentesimi(v, out cent);
            }
            if (cent <= 0 || cent >= Carrello.SubtotaleProdottiCent)
            {
                Messaggio("Sconto non valido");
                return;
            }
            Carrello.ScontoValore(cent);
        }

        private async void OnScontoPercentuale(object sender, EventArgs e)
        {
            if (Carrello.Vuoto) return;
            if (!_tastierino.PrendiValore(out decimal perc))
            {
                string v = await DisplayPromptAsync("% SCONTO", "Percentuale sul totale", "OK", "Annulla", "10", keyboard: Keyboard.Numeric);
                if (v == null) return;
                Importi.ProvaDecimale(v, out perc);
            }
            if (perc <= 0 || perc >= 100)
            {
                Messaggio("Percentuale non valida");
                return;
            }
            Carrello.ScontoPercentuale(perc);
        }

        private async void OnAnnulla(object sender, EventArgs e)
        {
            _tastierino.Cancella();
            if (Carrello.Righe.Count == 0) return;
            if (await DisplayAlert("🗑️ ANNULLA", "Annullare lo scontrino in corso?", "Annulla scontrino", "No"))
                Carrello.Svuota();
        }

        // ══════════════ RIGHE SCONTRINO ══════════════

        private RigaScontrino RigaSelezionata() => ListaRighe.SelectedItem as RigaScontrino;

        private void SelezionaUltima()
        {
            var ultima = Carrello.UltimaRigaProdotto;
            ListaRighe.SelectedItem = ultima;
            if (ultima != null) ListaRighe.ScrollTo(ultima, position: ScrollToPosition.End, animate: false);
        }

        private void OnRigaScelta(object sender, SelectionChangedEventArgs e)
        {
            // La selezione serve a ＋ ﹣ ed €: nessuna azione al tocco, come sul PC
        }

        private void OnPiu(object sender, EventArgs e)
        {
            var riga = RigaSelezionata() ?? Carrello.UltimaRigaProdotto;
            if (riga?.Tipo == TipoRiga.Prodotto) Carrello.CambiaQuantita(riga, 1);
        }

        /// <summary>﹣ sulla riga selezionata: a quantità zero la riga viene eliminata. Sulla riga sconto la rimuove.</summary>
        private void OnMeno(object sender, EventArgs e)
        {
            var riga = RigaSelezionata() ?? Carrello.UltimaRigaProdotto;
            if (riga == null) return;
            if (riga.Tipo == TipoRiga.Sconto)
            {
                Carrello.RimuoviSconto();
                return;
            }
            bool sparisce = riga.Quantita <= 1;
            Carrello.CambiaQuantita(riga, -1);
            if (sparisce) ListaRighe.SelectedItem = null;
        }

        private void AggiornaTotale()
        {
            EtichettaTotale.Text = Importi.Euro(Carrello.TotaleCent);
            EtichettaPezzi.Text = $"Pz: {Carrello.Pezzi:0.###}";
            if (!Carrello.Vuoto) NascondiResto();
        }

        private void AggiornaDisplay()
        {
            _timerMessaggio?.Stop();
            DisplayTastierino.Text = _tastierino.Display;
            DisplayTastierino.TextColor = Color.FromArgb(_tastierino.QuantitaBloccata.HasValue ? "#FF9800" : "#666666");
        }

        /// <summary>Messaggio breve nel display (al posto delle finestre di avviso), sparisce dopo 3 secondi.</summary>
        private void Messaggio(string testo)
        {
            DisplayTastierino.Text = testo;
            DisplayTastierino.TextColor = Color.FromArgb("#D32F2F");
            _timerMessaggio?.Stop();
            _timerMessaggio = Dispatcher.CreateTimer();
            _timerMessaggio.Interval = TimeSpan.FromSeconds(3);
            _timerMessaggio.IsRepeating = false;
            _timerMessaggio.Tick += (s, e) => AggiornaDisplay();
            _timerMessaggio.Start();
        }

        // ══════════════ PAGAMENTO ══════════════

        /// <summary>CONTANTI: se è stato digitato un importo è il contante ricevuto (calcolo resto).</summary>
        private async void OnContanti(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            int versato = 0;
            if (!_tastierino.Vuoto)
            {
                _tastierino.PrendiImporto(out versato);
                if (versato < Carrello.TotaleCent)
                {
                    await DisplayAlert("IMPORTO INSUFFICIENTE",
                        $"Richiesto: {Importi.Euro(Carrello.TotaleCent)}\nVersato: {Importi.Euro(versato)}", "OK");
                    return;
                }
            }
            await EmettiAsync(Pagamento.Contanti(versato));
        }

        private async void OnCarta(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            await EmettiAsync(Pagamento.Carta());
        }

        /// <summary>MISTO: importo digitato (o chiesto) = parte in contanti, il resto su carta.</summary>
        private async void OnMisto(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            int totale = Carrello.TotaleCent;
            if (!_tastierino.PrendiImporto(out int contanti))
            {
                string v = await DisplayPromptAsync("PAGAMENTO MISTO", $"Totale {Importi.Euro(totale)}\nParte pagata in CONTANTI (il resto su carta):",
                    "Conferma", "Annulla", keyboard: Keyboard.Numeric);
                if (v == null) return;
                Importi.ProvaCentesimi(v, out contanti);
            }
            if (contanti <= 0 || contanti >= totale)
            {
                await DisplayAlert("PAGAMENTO MISTO", "La parte in contanti deve essere maggiore di zero e minore del totale.", "OK");
                return;
            }
            await EmettiAsync(Pagamento.Misto(contanti));
        }

        private async Task EmettiAsync(Pagamento pagamento)
        {
            if (Carrello.Vuoto || ContestoCassa.ServizioVendita.InCorso) return;

            var righe = Carrello.Fotografia();
            MostraAttesa(true, "⏳ Vendita in corso...");
            EsitoVendita esito;
            try
            {
                // Fuori dal thread grafico: la comunicazione col registratore non deve bloccare lo schermo
                esito = await Task.Run(() => ContestoCassa.ServizioVendita.EmettiAsync(righe, pagamento));
            }
            finally
            {
                MostraAttesa(false);
            }

            if (esito.Stato == StatoEsito.Fallito)
            {
                await DisplayAlert(esito.Titolo, esito.Messaggio, "OK");
                return; // carrello intatto: l'operatore può riprovare
            }

            Carrello.Svuota();
            _tastierino.Cancella();
            if (esito.RestoCent > 0) MostraResto(esito.RestoCent);
            if (esito.Stato == StatoEsito.RiuscitoSenzaArchivio)
                await DisplayAlert("ATTENZIONE", esito.Messaggio, "OK");
        }

        /// <summary>Barra RESTO arancione per 15 secondi o fino al prossimo articolo (come sul PC).</summary>
        private void MostraResto(int restoCent)
        {
            EtichettaResto.Text = Importi.Euro(restoCent);
            BarraResto.IsVisible = true;
            _timerResto?.Stop();
            _timerResto = Dispatcher.CreateTimer();
            _timerResto.Interval = TimeSpan.FromSeconds(15);
            _timerResto.IsRepeating = false;
            _timerResto.Tick += (s, e) => NascondiResto();
            _timerResto.Start();
        }

        private void NascondiResto()
        {
            _timerResto?.Stop();
            BarraResto.IsVisible = false;
        }

        private void MostraAttesa(bool visibile, string testo = null)
        {
            if (testo != null) TestoAttesa.Text = testo;
            Attesa.IsVisible = visibile;
        }

        // ══════════════ UTILITÀ ══════════════

        private static Color Colore(string hex, string predefinito)
            => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) ? c : Color.FromArgb(predefinito);

        private static void Tocco(View vista, Func<Task> azione)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                vista.Opacity = 0.75;
                try { await azione(); }
                finally { vista.Opacity = 1; }
            };
            vista.GestureRecognizers.Add(tap);
        }
    }
}
