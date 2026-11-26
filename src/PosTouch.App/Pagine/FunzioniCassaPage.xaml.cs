using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Cassa;

namespace PosTouch.App.Pagine
{
    /// <summary>Copia di FunzioniCassaDialog del PC.</summary>
    public partial class FunzioniCassaPage : ContentPage
    {
        private FunzioniCassa Funzioni => ContestoCassa.FunzioniCassa;

        public FunzioniCassaPage()
        {
            InitializeComponent();
        }

        private async void OnChiusura(object sender, EventArgs e)
        {
            if (await MessaggioBox.Conferma("CHIUSURA FISCALE", "Eseguire la chiusura fiscale giornaliera (Z)?\n\nL'operazione non può essere annullata."))
                await EseguiAsync(Funzioni.ChiusuraFiscaleAsync);
        }

        private async void OnLettura(object sender, EventArgs e) => await EseguiAsync(Funzioni.LetturaGiornalieraAsync);
        private async void OnRapportoReparti(object sender, EventArgs e) => await EseguiAsync(Funzioni.RapportoRepartiAsync);
        private async void OnRistampa(object sender, EventArgs e) => await EseguiAsync(Funzioni.RistampaUltimoAsync);
        private async void OnRistampaPenultimo(object sender, EventArgs e) => await EseguiAsync(Funzioni.RistampaPenultimoAsync);

        private async void OnSblocca(object sender, EventArgs e)
        {
            if (await MessaggioBox.Domanda("RESET RT", "Annullare l'eventuale scontrino rimasto aperto sul registratore?"))
                await EseguiAsync(Funzioni.SbloccaRTAsync);
        }

        private async void OnChiudi(object sender, EventArgs e) => await Navigation.PopAsync();

        private async Task EseguiAsync(Func<Task<EsitoOperazione>> operazione)
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
            if (esito.Ok) await MessaggioBox.Successo("OPERAZIONE COMPLETATA", esito.Messaggio);
            else await MessaggioBox.Errore("ERRORE REGISTRATORE", esito.Messaggio);
        }
    }
}
