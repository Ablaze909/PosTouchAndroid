using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Vendita al banco: stesso layout, stessi tasti e stessi gesti di MainWindow (POS Touch Windows).
    /// La logica sta in Carrello, Tastierino e ServizioVendita (libreria Core).
    /// </summary>
    public partial class VenditaPage : ContentPage
    {
        private const int IdPreferiti = -1;
        private double _latoTasto = 100;     // 100×100 come sul PC; si adatta per riempire la riga

        private readonly Tastierino _tastierino = new Tastierino();
        private readonly IDispatcherTimer _orologio;
        private IDispatcherTimer _timerResto;
        private int _categoriaCorrente = IdPreferiti;
        private bool _modalitaModifica;
        private bool _scontrinoEspanso;
        private (int, int) _versioneMostrata = (-1, -1);

        // doppio tocco sulle righe
        private RigaScontrino _ultimaRigaToccata;
        private DateTime _ultimoTocco = DateTime.MinValue;

        private Carrello Carrello => ContestoCassa.Carrello;
        private Impostazioni Cfg => ContestoCassa.Impostazioni;

        public VenditaPage()
        {
            InitializeComponent();
            ListaRighe.ItemsSource = Carrello.Righe;
            Carrello.Cambiato += AggiornaTotale;
            _tastierino.Cambiato += () =>
            {
                DisplayTastierino.Text = _tastierino.Display;
                DisplayTastierino.TextColor = Ui.C(_tastierino.ColoreDisplay);
            };

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
            _tastierino.BloccoModificaQuantita = Cfg.BloccoModificaQuantita;
            BtnStrumenti.IsVisible = Cfg.MostraStrumentiVendita;
            // Ridisegna categorie e articoli solo se catalogo o impostazioni sono cambiati
            // (OnAppearing scatta anche alla chiusura di ogni finestra: niente ridisegni inutili)
            var versione = (ContestoCassa.Catalogo.Versione, ContestoCassa.VersioneImpostazioni);
            if (versione != _versioneMostrata)
            {
                _versioneMostrata = versione;
                CaricaCategorie();
            }
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

        // ══════════════ ADATTAMENTO ALLO SCHERMO ══════════════

        /// <summary>
        /// Stesse proporzioni del PC su qualsiasi tablet (da 8" in su): colonne, tastiera e barra
        /// si ridimensionano con lo schermo; i tasti articolo si allargano per riempire la riga.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0 || height <= 0) return;

            double sinistra = Math.Clamp(width * 0.095, 96, 150);
            double destra = Math.Clamp(width * 0.26, 290, 420);
            Radice.ColumnDefinitions[0].Width = new GridLength(sinistra);
            Radice.ColumnDefinitions[2].Width = new GridLength(destra);

            double barra = Math.Clamp(height * 0.075, 50, 72);
            Radice.RowDefinitions[1].Height = new GridLength(barra);

            // tastiera: righe alte quanto lo permette lo schermo, lasciando spazio alla lista
            double riga = Math.Clamp((height - 380) / 4, 44, 72);
            foreach (var rd in TastieraNumerica.RowDefinitions) rd.Height = new GridLength(riga);
            foreach (var b in TastieraNumerica.Children.OfType<Button>())
                b.FontSize = b.Text == "€" ? riga * 0.6 : b.Text == "," ? riga * 0.55 : riga * 0.4;
            RigaSconti.HeightRequest = RigaQuantita.HeightRequest = Math.Clamp(riga * 0.7, 36, 52);

            // barra inferiore: testo che entra nei tasti
            double colonna = (width - destra - 12) / 5;
            foreach (var b in BarraInferiore.Children.OfType<Button>())
                b.FontSize = Math.Clamp(colonna / 7.5, 12, 20);

            // tasti articolo: quanti ne entrano in una riga, poi allargati per riempirla
            double area = width - sinistra - destra - 8;
            int perRiga = Math.Max(3, (int)(area / 110));
            double lato = Math.Clamp(Math.Floor(area / perRiga) - 6, 90, 150);
            if (Math.Abs(lato - _latoTasto) > 1)
            {
                _latoTasto = lato;
                MostraArticoli();
            }
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
            var p = ContestoCassa.Catalogo.CercaBarcode(codice);
            if (p == null) _ = MessaggioBox.Avviso("ARTICOLO NON TROVATO", $"Nessun articolo con codice:\n{codice}");
            else _ = AggiungiProdottoAsync(p);
        }

        // ══════════════ TOOLBAR ══════════════

        private async void OnStrumenti(object sender, EventArgs e)
        {
            if (await Accesso.VerificaPinAsync("🔧 STRUMENTI"))
                await Navigazione.Apri(this, () => new StrumentiPage());
        }

        private async void OnModifica(object sender, EventArgs e)
        {
            if (!Cfg.AbilitaModificaVendita)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            _modalitaModifica = !_modalitaModifica;
            BtnModifica.BackgroundColor = Ui.C(_modalitaModifica ? "#FF9800" : "#757575");
            OverlayModifica.IsVisible = _modalitaModifica;
        }

        private async void OnFunzioniCassa(object sender, EventArgs e)
            => await Navigazione.ApriModale(this, () => new FunzioniCassaPage());

        /// <summary>🚪: torna alla schermata iniziale (come TornaAlLauncher del PC).</summary>
        private async void OnEsci(object sender, EventArgs e)
        {
            if (!Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("SCONTRINO APERTO", "Chiudi o svuota lo scontrino prima di uscire.");
                return;
            }
            await Navigazione.Chiudi(this);
        }

        /// <summary>Tasto indietro di Android: come 🚪 (non si esce con lo scontrino aperto o durante la stampa).</summary>
        protected override bool OnBackButtonPressed()
        {
            if (Attesa.IsVisible) return true;
            if (OverlayPagamento.IsVisible) { OverlayPagamento.IsVisible = false; return true; }
            OnEsci(this, EventArgs.Empty);
            return true;
        }

        // ══════════════ TASTIERA ══════════════

        private void OnCifra(object sender, EventArgs e) => _tastierino.Premi(((Button)sender).Text);

        /// <summary>C del PC: azzera la tastiera; se non c'è nulla digitato cancella le righe selezionate, altrimenti l'ultima.</summary>
        private void OnCancella(object sender, EventArgs e)
        {
            if (_tastierino.Attivo)
            {
                _tastierino.Azzera();
                return;
            }
            var selezionate = Carrello.Righe.Where(r => r.Selezionata).ToList();
            if (selezionate.Count > 0)
            {
                Carrello.Rimuovi(selezionate);
            }
            else
            {
                if (_scontrinoEspanso) return; // lista espansa: C senza selezione non fa nulla
                var ultima = Carrello.Righe.LastOrDefault();
                if (ultima != null) Carrello.Rimuovi(new[] { ultima });
            }
        }

        private void OnPer(object sender, EventArgs e) => _tastierino.Moltiplica();

        private async void OnEuro(object sender, EventArgs e)
        {
            if (!Cfg.AbilitaCambioPrezzo)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            switch (_tastierino.Euro())
            {
                case EsitoTastoEuro.NonValido:
                    await MessaggioBox.Errore("PREZZO NON VALIDO", "Digita un importo valido!\n\nEsempio: 1,50 o 12,00");
                    break;
                case EsitoTastoEuro.Vuoto:
                    await MessaggioBox.Info("COME USARE IL TASTO €",
                        "Per impostare un prezzo personalizzato:\n\n1. Digita il prezzo (es: 1,50)\n2. Premi il tasto €\n3. Seleziona l'articolo");
                    break;
            }
        }

        private async void OnScontoValore(object sender, EventArgs e) => await ScontoAsync(percentuale: false);
        private async void OnScontoPercentuale(object sender, EventArgs e) => await ScontoAsync(percentuale: true);

        private async Task ScontoAsync(bool percentuale)
        {
            if (ContestoCassa.ServizioVendita.InCorso) return;
            if (!Cfg.AbilitaSconti)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            if (Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("SCONTRINO VUOTO", "Aggiungi prima dei prodotti!");
                return;
            }
            decimal subtotale = Carrello.SubtotaleProdottiCent / 100m;
            if (subtotale <= 0)
            {
                await MessaggioBox.Avviso("SUBTOTALE ZERO", "Non ci sono prodotti da scontare!");
                return;
            }
            if (Carrello.HaSconto)
            {
                await MessaggioBox.Avviso("SCONTO GIÀ PRESENTE", "È già presente uno sconto nello scontrino!\n\nRimuovilo prima di applicarne un altro.");
                return;
            }
            var valore = await ScontoBox.Chiedi(percentuale, subtotale);
            if (valore == null) return;
            if (percentuale) Carrello.ScontoPercentuale(valore.Value);
            else Carrello.ScontoValore((int)Math.Round(valore.Value * 100, MidpointRounding.AwayFromZero));
            ListaRighe.ScrollTo(Carrello.Righe.Count - 1, position: ScrollToPosition.End, animate: false);
        }

        private async void OnAnnulla(object sender, EventArgs e)
        {
            if (Carrello.Righe.Count == 0) return;
            if (await MessaggioBox.Conferma("ANNULLA SCONTRINO", "Vuoi annullare tutto lo scontrino?\n\nQuesta operazione non può essere annullata."))
                SvuotaScontrino();
        }

        private RigaScontrino PrimaSelezionata() => Carrello.Righe.FirstOrDefault(r => r.Selezionata);

        private void OnPiu(object sender, EventArgs e)
        {
            var riga = PrimaSelezionata();
            if (riga?.Tipo == TipoRiga.Prodotto) Carrello.ImpostaQuantita(riga, riga.Quantita + 1);
        }

        private void OnMeno(object sender, EventArgs e)
        {
            var riga = PrimaSelezionata();
            if (riga?.Tipo == TipoRiga.Prodotto && riga.Quantita > 1) Carrello.ImpostaQuantita(riga, riga.Quantita - 1);
        }

        // ══════════════ LISTA SCONTRINO: gesti ══════════════

        /// <summary>
        /// Tocco = seleziona/deseleziona (selezione multipla, come la ListView del PC).
        /// Doppio tocco sulla quantità = 🔢 QUANTITÀ; doppio tocco sull'importo = 💶 PREZZO UNITARIO.
        /// </summary>
        private async void OnRigaToccata(object sender, TappedEventArgs e)
        {
            if (sender is not View vista || vista.BindingContext is not RigaScontrino riga) return;

            bool doppio = ReferenceEquals(riga, _ultimaRigaToccata) && (DateTime.Now - _ultimoTocco).TotalMilliseconds < 400;
            _ultimaRigaToccata = riga;
            _ultimoTocco = DateTime.Now;

            if (!doppio)
            {
                riga.Selezionata = !riga.Selezionata;
                return;
            }

            // doppio tocco: annulla il cambio di selezione del primo tocco
            riga.Selezionata = !riga.Selezionata;
            _ultimaRigaToccata = null;
            if (riga.Tipo != TipoRiga.Prodotto) return;

            double x = e.GetPosition(vista)?.X ?? -1;
            double larghezza = vista.Width;
            if (x >= 0 && x <= 40) await ModificaQuantitaAsync(riga);
            else if (x >= larghezza - 80) await ModificaPrezzoAsync(riga);
        }

        private async Task ModificaQuantitaAsync(RigaScontrino riga)
        {
            if (!Cfg.AbilitaModificaVendita)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            var q = await TastierinoPrezzo.ChiediQuantita("🔢 QUANTITÀ", riga.Quantita);
            if (q == null) return;
            if (q > 0 && q <= 999) Carrello.ImpostaQuantita(riga, q.Value);
            else await MessaggioBox.Avviso("QUANTITÀ NON VALIDA", "La quantità deve essere tra 1 e 999");
        }

        private async Task ModificaPrezzoAsync(RigaScontrino riga)
        {
            if (!Cfg.AbilitaCambioPrezzo)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            var p = await TastierinoPrezzo.ChiediEuro("💶 PREZZO UNITARIO", riga.PrezzoEuro);
            if (p == null) return;
            if (p > 0) Carrello.CambiaPrezzo(riga, (int)Math.Round(p.Value * 100, MidpointRounding.AwayFromZero));
            else await MessaggioBox.Avviso("PREZZO NON VALIDO", "Il prezzo deve essere maggiore di zero");
        }

        /// <summary>Tocco su 📋 SCONTRINO: lista espansa (nasconde la tastiera) / normale.</summary>
        private void OnIntestazioneScontrino(object sender, TappedEventArgs e) => ApplicaModalitaEspansa(!_scontrinoEspanso);
        private void OnChiudiLista(object sender, EventArgs e) => ApplicaModalitaEspansa(false);

        private void ApplicaModalitaEspansa(bool espanso)
        {
            _scontrinoEspanso = espanso;
            TastieraNumerica.IsVisible = !espanso;
            BtnChiudiLista.IsVisible = espanso;
        }

        private void OnScrollSu(object sender, EventArgs e) => ScorriLista(-3);
        private void OnScrollGiu(object sender, EventArgs e) => ScorriLista(+3);

        private int _primaVisibile, _ultimaVisibile;

        private void ScorriLista(int righe)
        {
            if (Carrello.Righe.Count == 0) return;
            int indice = Math.Clamp(righe < 0 ? _primaVisibile + righe : _ultimaVisibile + righe, 0, Carrello.Righe.Count - 1);
            ListaRighe.ScrollTo(indice, position: righe < 0 ? ScrollToPosition.Start : ScrollToPosition.End, animate: true);
        }

        private void OnListaScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            _primaVisibile = e.FirstVisibleItemIndex;
            _ultimaVisibile = e.LastVisibleItemIndex;
            AggiornaFrecce();
        }

        private void AggiornaFrecce()
        {
            int n = Carrello.Righe.Count;
            BtnScrollSu.IsVisible = n > 0 && _primaVisibile > 0;
            BtnScrollGiu.IsVisible = n > 0 && _ultimaVisibile < n - 1 && _ultimaVisibile >= 0;
        }

        private void AggiornaTotale()
        {
            EtichettaTotale.Text = $"€ {Carrello.TotaleEuro:F2}".Replace('.', ',');
            EtichettaPezzi.Text = $"{Carrello.Pezzi} pz";
            if (Carrello.Righe.Count == 0) { _primaVisibile = 0; _ultimaVisibile = 0; }
            AggiornaFrecce();
        }

        private void SvuotaScontrino()
        {
            if (_scontrinoEspanso) ApplicaModalitaEspansa(false);
            Carrello.Svuota();
            _tastierino.Azzera();
        }

        // ══════════════ BARRA INFERIORE ══════════════

        private async void OnCassa(object sender, EventArgs e)
        {
            if (Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!");
                return;
            }
            OverlayPagamento.IsVisible = true;
        }

        private void OnChiudiPagamento(object sender, EventArgs e) => OverlayPagamento.IsVisible = false;

        private async void OnConto(object sender, EventArgs e)
            => await MessaggioBox.Info("📄 CONTO", "La gestione dei conti (parcheggio e conti clienti) arriverà nella prossima versione.");

        private async void OnCassetto(object sender, EventArgs e)
        {
            var esito = await Task.Run(() => ContestoCassa.FunzioniCassa.ApriCassettoAsync());
            if (!esito.Ok) await MessaggioBox.Errore("CASSETTO", esito.Messaggio);
        }

        private async void OnBarcode(object sender, EventArgs e)
        {
            string codice = await TastierinoPrezzo.ChiediCodice("🔍 BARCODE");
            if (!string.IsNullOrWhiteSpace(codice)) AggiungiDaBarcode(codice.Trim());
        }

        // ══════════════ PAGAMENTO ══════════════

        /// <summary>CONTANTI dal popup CASSA: chiusura immediata, importo esatto (come il PC).</summary>
        private async void OnContanti(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            if (Carrello.Vuoto) { await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!"); return; }
            if (Carrello.TotaleCent <= 0) return;
            await EseguiVenditaAsync(Pagamento.Contanti(), 0);
        }

        /// <summary>PAG. ELETTRONICO dal popup CASSA: chiusura immediata su carta.</summary>
        private async void OnCarta(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            if (Carrello.Vuoto) { await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!"); return; }
            if (Carrello.TotaleCent <= 0) return;
            await EseguiVenditaAsync(Pagamento.Carta(), 0);
        }

        /// <summary>Tocco sulla barra TOTALE: calcolo resto con banconote/monete, carta o misto.</summary>
        private async void OnTotaleToccato(object sender, TappedEventArgs e)
        {
            if (Carrello.TotaleCent <= 0 || ContestoCassa.ServizioVendita.InCorso) return;
            var esito = await CalcoloResto.Mostra(Carrello.TotaleEuro);
            if (esito == null) return;

            var pagamento = esito.Metodo switch
            {
                MetodiPagamento.Misto => Pagamento.Misto(esito.ContantiCent),
                MetodiPagamento.Carta => Pagamento.Carta(),
                _ => Pagamento.Contanti(esito.RicevutoCent > Carrello.TotaleCent ? esito.RicevutoCent : 0)
            };
            await EseguiVenditaAsync(pagamento, esito.RestoCent);
        }

        private bool _venditaAvviata;

        private async Task EseguiVenditaAsync(Pagamento pagamento, int restoCent)
        {
            if (Carrello.Vuoto || ContestoCassa.ServizioVendita.InCorso || _venditaAvviata) return;
            _venditaAvviata = true;
            try { await EseguiVenditaInternaAsync(pagamento, restoCent); }
            finally { _venditaAvviata = false; }
        }

        private async Task EseguiVenditaInternaAsync(Pagamento pagamento, int restoCent)
        {
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
                await MessaggioBox.Errore(esito.Titolo, esito.Messaggio);
                return; // scontrino intatto: l'operatore può riprovare
            }
            if (esito.Stato == StatoEsito.Incerto)
            {
                // Comunicazione interrotta durante la chiusura: decide l'operatore guardando l'RT (evita doppi scontrini)
                bool uscito = await MessaggioBox.Domanda(esito.Titolo, esito.Messaggio + "\n\nLo scontrino è stato STAMPATO?");
                if (!uscito)
                {
                    await MessaggioBox.Avviso("SCONTRINO NON EMESSO",
                        "Lo scontrino resta a video.\nAlla prossima vendita il registratore verrà controllato e sbloccato automaticamente.");
                    return;
                }
                esito = ContestoCassa.ServizioVendita.ConfermaScontrinoUscito(esito);
            }

            SvuotaScontrino();
            if (restoCent > 0) MostraRestoConTimer(restoCent);
            if (esito.Stato == StatoEsito.RiuscitoSenzaArchivio)
                await MessaggioBox.Avviso("ATTENZIONE", esito.Messaggio);
        }

        /// <summary>Barra RESTO arancione per 3 secondi (MostraRestoConTimer del PC).</summary>
        private void MostraRestoConTimer(int restoCent)
        {
            EtichettaResto.Text = $"€ {restoCent / 100m:N2}";
            BarraResto.IsVisible = true;
            _timerResto?.Stop();
            _timerResto = Dispatcher.CreateTimer();
            _timerResto.Interval = TimeSpan.FromSeconds(3);
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
