using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Vendita al banco. La pagina mostra e raccoglie i tocchi:
    /// la logica sta in Carrello, Tastierino e ServizioVendita (libreria Core).
    /// </summary>
    public partial class VenditaPage : ContentPage
    {
        private const int IdPreferiti = -1;
        private readonly Tastierino _tastierino = new Tastierino();
        private Carrello Carrello => ContestoCassa.Carrello;

        public VenditaPage()
        {
            InitializeComponent();
            ListaRighe.ItemsSource = Carrello.Righe;
            Carrello.Cambiato += AggiornaTotale;
            _tastierino.Cambiato += () => DisplayTastierino.Text = _tastierino.Display;
            AggiornaTotale();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CaricaCategorie();     // il catalogo può essere cambiato in Articoli/Impostazioni
            CreaTastiReparto();
            LettoreBarcode.Destinatario = AggiungiDaBarcode;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            LettoreBarcode.Destinatario = null;
        }

        // ══════════════ CATALOGO ══════════════

        private void CaricaCategorie()
        {
            var cat = ContestoCassa.Catalogo;
            var lista = new List<Categoria>();
            if (cat.Preferiti().Any())
                lista.Add(new Categoria { Id = IdPreferiti, Nome = "★ PREFERITI", Colore = "#F9A825", ColoreTesto = "#000000" });
            lista.AddRange(cat.Categorie);

            int selezionata = (ListaCategorie.SelectedItem as Categoria)?.Id ?? int.MinValue;
            ListaCategorie.ItemsSource = lista;
            MessaggioVuoto.Text = lista.Count > 0
                ? "Nessun articolo in questa categoria.\nAggiungili da ARTICOLI."
                : "Nessuna categoria.\nCreale da ARTICOLI oppure importa il pos.db del PC da Impostazioni.\n\nIntanto puoi vendere con i tasti reparto in basso.";

            var daSelezionare = lista.FirstOrDefault(c => c.Id == selezionata) ?? lista.FirstOrDefault();
            ListaCategorie.SelectedItem = daSelezionare;
            if (daSelezionare == null) ListaProdotti.ItemsSource = null;
            else MostraProdotti(daSelezionare); // aggiorna anche se la categoria selezionata non cambia
        }

        private void MostraProdotti(Categoria c)
        {
            ListaProdotti.ItemsSource = c.Id == IdPreferiti
                ? ContestoCassa.Catalogo.Preferiti().ToList()
                : ContestoCassa.Catalogo.ProdottiDi(c.Id).ToList();
        }

        private void OnCategoriaScelta(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Categoria c) MostraProdotti(c);
        }

        private async void OnProdottoScelto(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not Prodotto p) return;
            ListaProdotti.SelectedItem = null; // permette di toccare di nuovo lo stesso articolo
            await AggiungiArticoloAsync(p);
        }

        /// <summary>
        /// Articolo a prezzo fisso: le cifre digitate sono la quantità.
        /// Articolo a prezzo zero (o dopo X): le cifre digitate sono il prezzo.
        /// </summary>
        private async Task AggiungiArticoloAsync(Prodotto p)
        {
            if (p.PrezzoCentesimi > 0 && !(_tastierino.QuantitaBloccata.HasValue && !_tastierino.Vuoto))
            {
                Carrello.Aggiungi(p, _tastierino.PrendiQuantita());
                return;
            }

            int prezzo;
            if (!_tastierino.PrendiImporto(out prezzo))
            {
                string v = await DisplayPromptAsync(p.Descrizione, "Prezzo", "Aggiungi", "Annulla", "0,00", keyboard: Keyboard.Numeric);
                if (v == null) return;
                if (!Importi.ProvaCentesimi(v, out prezzo))
                {
                    await DisplayAlert(p.Descrizione, "Prezzo non valido.", "OK");
                    return;
                }
            }
            Carrello.Aggiungi(p, _tastierino.PrendiQuantitaBloccata(), prezzo);
        }

        private void AggiungiDaBarcode(string codice)
        {
            var p = ContestoCassa.Catalogo.CercaBarcode(codice);
            if (p == null)
            {
                Stato($"Codice {codice} non trovato");
                return;
            }
            _ = AggiungiArticoloAsync(p);
            Stato("");
        }

        private async void OnBarcode(object sender, EventArgs e)
        {
            string codice = await DisplayPromptAsync("Barcode", "Codice o barcode dell'articolo", "Cerca", "Annulla", keyboard: Keyboard.Numeric);
            if (!string.IsNullOrWhiteSpace(codice)) AggiungiDaBarcode(codice.Trim());
        }

        // ══════════════ TASTI REPARTO (barra sotto) ══════════════

        private void CreaTastiReparto()
        {
            BarraReparti.Children.Clear();
            foreach (var t in ContestoCassa.Impostazioni.ElencoTastiReparto())
            {
                var tasto = t;
                var b = new Button
                {
                    Text = tasto.Nome,
                    FontSize = 14,
                    HeightRequest = 56,
                    MinimumWidthRequest = 110,
                    Padding = new Thickness(10, 0),
                    BackgroundColor = Color.FromArgb("#37474F")
                };
                b.Clicked += async (s, e) => await VendiRepartoAsync(tasto.Numero, tasto.Nome);
                BarraReparti.Children.Add(b);
            }
            var altro = new Button
            {
                Text = "REP…",
                FontSize = 14,
                HeightRequest = 56,
                MinimumWidthRequest = 80,
                BackgroundColor = Color.FromArgb("#37474F")
            };
            altro.Clicked += OnAltroReparto;
            BarraReparti.Children.Add(altro);
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
                    await DisplayAlert(nome, "Importo non valido.", "OK");
                    return;
                }
            }
            Carrello.AggiungiLibero(nome, reparto, cent, _tastierino.PrendiQuantitaBloccata());
        }

        private async void OnAltroReparto(object sender, EventArgs e)
        {
            string r = await DisplayPromptAsync("Reparto", "Numero reparto del registratore", "Avanti", "Annulla",
                maxLength: 2, keyboard: Keyboard.Numeric, initialValue: "1");
            if (r == null) return;
            if (!int.TryParse(r, out int reparto) || reparto <= 0)
            {
                await DisplayAlert("Reparto", "Numero reparto non valido.", "OK");
                return;
            }
            await VendiRepartoAsync(reparto, $"REPARTO {reparto}");
        }

        // ══════════════ TASTIERINO ══════════════

        private void OnCifra(object sender, EventArgs e) => _tastierino.Premi(((Button)sender).Text);
        private void OnCancella(object sender, EventArgs e) => _tastierino.Cancella();

        private void OnPer(object sender, EventArgs e)
        {
            if (!_tastierino.Moltiplica()) Stato("Digitare prima la quantità, poi X");
        }

        /// <summary>Tasto €: il numero digitato diventa il prezzo dell'ultima riga.</summary>
        private async void OnEuro(object sender, EventArgs e)
        {
            var riga = Carrello.UltimaRigaProdotto;
            if (riga == null)
            {
                Stato("Nessuna riga a cui cambiare prezzo");
                return;
            }
            if (!_tastierino.PrendiImporto(out int cent))
            {
                string v = await DisplayPromptAsync("Cambia prezzo", riga.Descrizione, "OK", "Annulla",
                    initialValue: (riga.PrezzoCentesimi / 100m).ToString("0.00"), keyboard: Keyboard.Numeric);
                if (v == null) return;
                if (!Importi.ProvaCentesimi(v, out cent))
                {
                    await DisplayAlert("Cambia prezzo", "Prezzo non valido.", "OK");
                    return;
                }
            }
            Carrello.CambiaPrezzo(riga, cent);
        }

        // ══════════════ RIGHE ══════════════

        private async void OnRigaScelta(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not RigaScontrino riga) return;
            ListaRighe.SelectedItem = null;

            if (riga.Tipo == TipoRiga.Sconto)
            {
                if (await DisplayAlert("Sconto", "Rimuovere lo sconto?", "Rimuovi", "Annulla"))
                    Carrello.RimuoviSconto();
                return;
            }

            string scelta = await DisplayActionSheet(riga.Descrizione, "Annulla", "Elimina riga", "+1", "−1", "Quantità...", "Prezzo...");
            switch (scelta)
            {
                case "+1": Carrello.CambiaQuantita(riga, 1); break;
                case "−1": Carrello.CambiaQuantita(riga, -1); break;
                case "Elimina riga": Carrello.Rimuovi(riga); break;
                case "Quantità...":
                    string q = await DisplayPromptAsync("Quantità", riga.Descrizione, "OK", "Annulla",
                        initialValue: riga.QuantitaDisplay, keyboard: Keyboard.Numeric);
                    if (Importi.ProvaDecimale(q, out decimal nuova))
                        Carrello.CambiaQuantita(riga, nuova - riga.Quantita);
                    break;
                case "Prezzo...":
                    string p = await DisplayPromptAsync("Prezzo", riga.Descrizione, "OK", "Annulla",
                        initialValue: (riga.PrezzoCentesimi / 100m).ToString("0.00"), keyboard: Keyboard.Numeric);
                    if (Importi.ProvaCentesimi(p, out int cent)) Carrello.CambiaPrezzo(riga, cent);
                    break;
            }
        }

        private void AggiornaTotale()
        {
            EtichettaTotale.Text = Importi.Euro(Carrello.TotaleCent);
            bool abilitato = !Carrello.Vuoto;
            BtnContanti.IsEnabled = BtnCarta.IsEnabled = BtnMisto.IsEnabled = abilitato;
        }

        private async void OnScontoValore(object sender, EventArgs e)
        {
            if (Carrello.Vuoto) return;
            if (!_tastierino.PrendiImporto(out int cent))
            {
                string v = await DisplayPromptAsync("€ Sconto", "Importo da scontare sul totale", "OK", "Annulla", "1,00", keyboard: Keyboard.Numeric);
                if (v == null) return;
                Importi.ProvaCentesimi(v, out cent);
            }
            if (cent <= 0 || cent >= Carrello.SubtotaleProdottiCent)
            {
                await DisplayAlert("Sconto", "Importo non valido.", "OK");
                return;
            }
            Carrello.ScontoValore(cent);
        }

        private async void OnScontoPercentuale(object sender, EventArgs e)
        {
            if (Carrello.Vuoto) return;
            if (!_tastierino.PrendiValore(out decimal perc))
            {
                string v = await DisplayPromptAsync("% Sconto", "Percentuale sul totale", "OK", "Annulla", "10", keyboard: Keyboard.Numeric);
                if (v == null) return;
                Importi.ProvaDecimale(v, out perc);
            }
            if (perc <= 0 || perc >= 100)
            {
                await DisplayAlert("Sconto", "Percentuale non valida.", "OK");
                return;
            }
            Carrello.ScontoPercentuale(perc);
        }

        private async void OnSvuota(object sender, EventArgs e)
        {
            _tastierino.Cancella();
            if (Carrello.Righe.Count == 0) return;
            if (await DisplayAlert("Annulla", "Eliminare tutte le righe dello scontrino?", "Elimina", "No"))
                Carrello.Svuota();
        }

        // ══════════════ PAGAMENTO ══════════════

        /// <summary>CONTANTI: se è stato digitato un importo è il contante ricevuto (calcolo resto).</summary>
        private async void OnContanti(object sender, EventArgs e)
        {
            int versato = 0;
            if (!_tastierino.Vuoto)
            {
                _tastierino.PrendiImporto(out versato);
                if (versato < Carrello.TotaleCent)
                {
                    await DisplayAlert("Contanti", $"Ricevuti {Importi.Euro(versato)}: meno del totale {Importi.Euro(Carrello.TotaleCent)}.", "OK");
                    return;
                }
            }
            await EmettiAsync(Pagamento.Contanti(versato));
        }

        private async void OnCarta(object sender, EventArgs e) => await EmettiAsync(Pagamento.Carta());

        /// <summary>MISTO: importo digitato (o chiesto) = parte in contanti, il resto su carta.</summary>
        private async void OnMisto(object sender, EventArgs e)
        {
            int totale = Carrello.TotaleCent;
            if (!_tastierino.PrendiImporto(out int contanti))
            {
                string v = await DisplayPromptAsync("Pagamento misto", $"Totale {Importi.Euro(totale)}\nParte pagata in CONTANTI (il resto su carta):",
                    "Conferma", "Annulla", keyboard: Keyboard.Numeric);
                if (v == null) return;
                Importi.ProvaCentesimi(v, out contanti);
            }
            if (contanti <= 0 || contanti >= totale)
            {
                await DisplayAlert("Pagamento misto", "La parte in contanti deve essere maggiore di zero e minore del totale.", "OK");
                return;
            }
            await EmettiAsync(Pagamento.Misto(contanti));
        }

        private async Task EmettiAsync(Pagamento pagamento)
        {
            if (Carrello.Vuoto || ContestoCassa.ServizioVendita.InCorso) return;

            var righe = Carrello.Fotografia();
            MostraAttesa(true, "Stampa scontrino in corso...");
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

            switch (esito.Stato)
            {
                case StatoEsito.Fallito:
                    await DisplayAlert(esito.Titolo, esito.Messaggio, "OK");
                    return; // carrello intatto: l'operatore può riprovare

                case StatoEsito.RiuscitoSenzaArchivio:
                    Carrello.Svuota();
                    await DisplayAlert("ATTENZIONE", esito.Messaggio, "OK");
                    break;

                default:
                    Carrello.Svuota();
                    break;
            }
            _tastierino.Cancella();

            Stato(esito.NumeroDocumento > 0
                ? $"✅ Scontrino n. {esito.NumeroAzzeramento}-{esito.NumeroDocumento} emesso alle {DateTime.Now:HH:mm}"
                : $"✅ Scontrino emesso alle {DateTime.Now:HH:mm}");

            if (esito.RestoCent > 0)
                await DisplayAlert("RESTO", Importi.Euro(esito.RestoCent), "OK");
        }

        private void MostraAttesa(bool visibile, string testo = null)
        {
            if (testo != null) TestoAttesa.Text = testo;
            Attesa.IsVisible = visibile;
        }

        private void Stato(string testo) => EtichettaStato.Text = testo;

        // ══════════════ BARRA SOTTO / NAVIGAZIONE ══════════════

        private async void OnCassetto(object sender, EventArgs e)
        {
            var esito = await Task.Run(() => ContestoCassa.FunzioniCassa.ApriCassettoAsync());
            if (!esito.Ok) await DisplayAlert("Cassetto", esito.Messaggio, "OK");
        }

        private async void OnFunzioniCassa(object sender, EventArgs e)
            => await Navigation.PushAsync(new FunzioniCassaPage());

        private async void OnArticoli(object sender, EventArgs e)
        {
            if (await Accesso.VerificaPinAsync(this, "Articoli"))
                await Navigation.PushAsync(new CatalogoPage());
        }

        private async void OnImpostazioni(object sender, EventArgs e)
        {
            if (await Accesso.VerificaPinAsync(this, "Impostazioni"))
                await Navigation.PushAsync(new ImpostazioniPage());
        }
    }
}
