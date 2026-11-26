using PosTouch.App.Servizi;
using PosTouch.Core.Cassa;

namespace PosTouch.App.Pagine
{
    public partial class FunzioniCassaPage : ContentPage
    {
        private FunzioniCassa Funzioni => ContestoCassa.FunzioniCassa;

        public FunzioniCassaPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            AggiornaTotali();
        }

        private void AggiornaTotali()
        {
            try
            {
                var t = ContestoCassa.Vendite.TotaliDi(DateTime.Today);
                TotScontrini.Text = t.NumeroScontrini.ToString();
                TotContanti.Text = Importi.Euro(t.ContantiCent);
                TotCarta.Text = Importi.Euro(t.CartaCent);
                TotTotale.Text = Importi.Euro(t.TotaleCent);
            }
            catch (Exception ex)
            {
                TotTotale.Text = "errore";
                PosTouch.Core.Diagnostica.LogManager.Error($"Totali giorno: {ex.Message}");
            }
        }

        private void OnLettura(object sender, EventArgs e) => _ = EseguiAsync(Funzioni.LetturaGiornalieraAsync);
        private void OnRistampa(object sender, EventArgs e) => _ = EseguiAsync(Funzioni.RistampaUltimoAsync);
        private void OnRapportoReparti(object sender, EventArgs e) => _ = EseguiAsync(Funzioni.RapportoRepartiAsync);
        private void OnRistampaPenultimo(object sender, EventArgs e) => _ = EseguiAsync(Funzioni.RistampaPenultimoAsync);
        private async void OnChiudi(object sender, EventArgs e) => await Navigation.PopAsync();

        private async void OnSblocca(object sender, EventArgs e)
        {
            if (await DisplayAlert("RESET RT", "Annullare l'eventuale scontrino rimasto aperto sul registratore?", "Sì", "No"))
                await EseguiAsync(Funzioni.SbloccaRTAsync);
        }

        private async void OnChiusura(object sender, EventArgs e)
        {
            if (!await DisplayAlert("CHIUSURA FISCALE",
                    "Eseguire la chiusura giornaliera (Z) sul registratore?\n\nL'operazione non si può annullare.",
                    "Esegui chiusura", "Annulla"))
                return;
            await EseguiAsync(Funzioni.ChiusuraFiscaleAsync);
        }

        private async Task EseguiAsync(Func<Task<EsitoOperazione>> operazione, bool mostraOk = true)
        {
            if (Attesa.IsVisible) return; // un'operazione alla volta
            Attesa.IsVisible = true;
            EsitoOperazione esito;
            try
            {
                esito = await Task.Run(operazione);
            }
            finally
            {
                Attesa.IsVisible = false;
            }
            if (!esito.Ok || mostraOk)
                await DisplayAlert(esito.Ok ? "Fatto" : "Errore", esito.Messaggio, "OK");
        }
    }
}
