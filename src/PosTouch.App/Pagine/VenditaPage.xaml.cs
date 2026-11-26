using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Vendita al banco. La pagina mostra e raccoglie i tocchi:
    /// la logica sta nel Carrello e nel ServizioVendita (libreria Core).
    /// </summary>
    public partial class VenditaPage : ContentPage
    {
        private const int IdPreferiti = -1;
        private Carrello Carrello => ContestoCassa.Carrello;

        public VenditaPage()
        {
            InitializeComponent();
            ListaRighe.ItemsSource = Carrello.Righe;
            Carrello.Cambiato += AggiornaTotale;
            AggiornaTotale();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CaricaCategorie(); // il catalogo può essere stato importato dalle impostazioni
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
            MessaggioVuoto.Text = cat.Presente
                ? "Nessun prodotto in questa categoria"
                : "Catalogo non presente.\nApri Impostazioni → Importa catalogo e scegli il file pos.db del PC.\n\nIntanto puoi vendere con il tasto REPARTO.";

            var daSelezionare = lista.FirstOrDefault(c => c.Id == selezionata) ?? lista.FirstOrDefault();
            ListaCategorie.SelectedItem = daSelezionare;
            if (daSelezionare == null) ListaProdotti.ItemsSource = null;
        }

        private void OnCategoriaScelta(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not Categoria c) return;
            ListaProdotti.ItemsSource = c.Id == IdPreferiti
                ? ContestoCassa.Catalogo.Preferiti().ToList()
                : ContestoCassa.Catalogo.ProdottiDi(c.Id).ToList();
        }

        private void OnProdottoScelto(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not Prodotto p) return;
            ListaProdotti.SelectedItem = null; // permette di toccare di nuovo lo stesso prodotto
            Carrello.Aggiungi(p);
        }

        private void OnBarcode(object sender, EventArgs e)
        {
            string codice = CampoBarcode.Text;
            CampoBarcode.Text = "";
            if (string.IsNullOrWhiteSpace(codice)) return;

            var p = ContestoCassa.Catalogo.CercaBarcode(codice);
            if (p != null)
            {
                Carrello.Aggiungi(p);
                EtichettaStato.Text = "";
            }
            else
                EtichettaStato.Text = $"Codice {codice.Trim()} non trovato";
            CampoBarcode.Focus();
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

            string scelta = await DisplayActionSheet(riga.Descrizione, "Annulla", "Elimina riga", "+1", "−1", "Quantità...");
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
            }
        }

        private void AggiornaTotale()
        {
            EtichettaTotale.Text = Importi.Euro(Carrello.TotaleCent);
            bool abilitato = !Carrello.Vuoto;
            BtnContanti.IsEnabled = BtnResto.IsEnabled = BtnCarta.IsEnabled = BtnMisto.IsEnabled = abilitato;
        }

        private async void OnSconto(object sender, EventArgs e)
        {
            if (Carrello.Vuoto) return;
            string scelta = await DisplayActionSheet("Sconto sul totale", "Annulla", null, "Sconto %", "Sconto €", "Rimuovi sconto");
            switch (scelta)
            {
                case "Sconto %":
                    string p = await DisplayPromptAsync("Sconto %", "Percentuale sul totale", "OK", "Annulla", "10", keyboard: Keyboard.Numeric);
                    if (Importi.ProvaDecimale(p, out decimal perc) && perc < 100) Carrello.ScontoPercentuale(perc);
                    else if (p != null) await DisplayAlert("Sconto", "Percentuale non valida.", "OK");
                    break;
                case "Sconto €":
                    string v = await DisplayPromptAsync("Sconto €", "Importo da scontare sul totale", "OK", "Annulla", "1,00", keyboard: Keyboard.Numeric);
                    if (Importi.ProvaCentesimi(v, out int cent) && cent < Carrello.SubtotaleProdottiCent) Carrello.ScontoValore(cent);
                    else if (v != null) await DisplayAlert("Sconto", "Importo non valido.", "OK");
                    break;
                case "Rimuovi sconto":
                    Carrello.RimuoviSconto();
                    break;
            }
        }

        /// <summary>Vendita a reparto con importo libero.</summary>
        private async void OnReparto(object sender, EventArgs e)
        {
            string r = await DisplayPromptAsync("Reparto", "Numero reparto del registratore", "Avanti", "Annulla", "1",
                maxLength: 2, keyboard: Keyboard.Numeric, initialValue: "1");
            if (r == null) return;
            if (!int.TryParse(r, out int reparto) || reparto <= 0)
            {
                await DisplayAlert("Reparto", "Numero reparto non valido.", "OK");
                return;
            }
            string imp = await DisplayPromptAsync($"Reparto {reparto}", "Importo", "Aggiungi", "Annulla", "0,00", keyboard: Keyboard.Numeric);
            if (imp == null) return;
            if (!Importi.ProvaCentesimi(imp, out int cent))
            {
                await DisplayAlert("Reparto", "Importo non valido.", "OK");
                return;
            }
            Carrello.AggiungiLibero(null, reparto, cent);
        }

        private async void OnSvuota(object sender, EventArgs e)
        {
            if (Carrello.Righe.Count == 0) return;
            if (await DisplayAlert("Svuota", "Eliminare tutte le righe dello scontrino?", "Svuota", "Annulla"))
                Carrello.Svuota();
        }

        // ══════════════ PAGAMENTO ══════════════

        private async void OnContanti(object sender, EventArgs e) => await EmettiAsync(Pagamento.Contanti());

        private async void OnContantiConResto(object sender, EventArgs e)
        {
            string v = await DisplayPromptAsync("Contanti ricevuti", $"Totale {Importi.Euro(Carrello.TotaleCent)}", "Conferma", "Annulla",
                keyboard: Keyboard.Numeric);
            if (v == null) return;
            if (!Importi.ProvaCentesimi(v, out int versato) || versato < Carrello.TotaleCent)
            {
                await DisplayAlert("Contanti", "L'importo ricevuto è inferiore al totale.", "OK");
                return;
            }
            await EmettiAsync(Pagamento.Contanti(versato));
        }

        private async void OnCarta(object sender, EventArgs e) => await EmettiAsync(Pagamento.Carta());

        private async void OnMisto(object sender, EventArgs e)
        {
            int totale = Carrello.TotaleCent;
            string v = await DisplayPromptAsync("Pagamento misto", $"Totale {Importi.Euro(totale)}\nParte pagata in CONTANTI (il resto su carta):",
                "Conferma", "Annulla", keyboard: Keyboard.Numeric);
            if (v == null) return;
            if (!Importi.ProvaCentesimi(v, out int contanti) || contanti >= totale)
            {
                await DisplayAlert("Pagamento misto", "La parte in contanti deve essere minore del totale.", "OK");
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

            EtichettaStato.Text = esito.NumeroDocumento > 0
                ? $"✅ Scontrino n. {esito.NumeroAzzeramento}-{esito.NumeroDocumento} emesso alle {DateTime.Now:HH:mm}"
                : $"✅ Scontrino emesso alle {DateTime.Now:HH:mm}";

            if (esito.RestoCent > 0)
                await DisplayAlert("RESTO", Importi.Euro(esito.RestoCent), "OK");
        }

        private void MostraAttesa(bool visibile, string testo = null)
        {
            if (testo != null) TestoAttesa.Text = testo;
            Attesa.IsVisible = visibile;
        }

        // ══════════════ NAVIGAZIONE ══════════════

        private async void OnFunzioniCassa(object sender, EventArgs e)
            => await Navigation.PushAsync(new FunzioniCassaPage());

        private async void OnImpostazioni(object sender, EventArgs e)
        {
            string pin = ContestoCassa.Impostazioni.PinImpostazioni;
            if (!string.IsNullOrEmpty(pin))
            {
                string inserito = await DisplayPromptAsync("Impostazioni", "Inserire il PIN", "OK", "Annulla",
                    keyboard: Keyboard.Numeric);
                if (inserito == null) return;
                if (inserito != pin)
                {
                    await DisplayAlert("Impostazioni", "PIN errato.", "OK");
                    return;
                }
            }
            await Navigation.PushAsync(new ImpostazioniPage());
        }
    }
}
