using System.Net;
using PosTouch.App.Servizi;
using PosTouch.Core.Cassa;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;

namespace PosTouch.App.Pagine
{
    public partial class ImpostazioniPage : ContentPage
    {
        public ImpostazioniPage()
        {
            InitializeComponent();
            Carica(ContestoCassa.Impostazioni);
            AggiornaInfo();
        }

        private void Carica(Impostazioni i)
        {
            SceltaTipo.SelectedIndex = i.TipoCassa == TipoCassaFiscale.Epson ? 1 : 0;
            CampoIP.Text = i.IP_RT;
            CampoPorta.Text = i.Porta_RT.ToString();
            CampoOperatore.Text = i.OperatoreRT.ToString();
            CampoTimeoutConn.Text = i.TimeoutConnessioneRT_ms.ToString();
            CampoTimeoutRisp.Text = i.TimeoutRispostaRT_ms.ToString();
            SwAccorpa.IsToggled = i.AccorpaRigheScontrino;
            SwCassetto.IsToggled = i.ApriCassetto;
            CampoPin.Text = i.PinImpostazioni;
        }

        private void AggiornaInfo()
        {
            var cat = ContestoCassa.Catalogo;
            InfoCatalogo.Text = cat.Presente
                ? $"{cat.Categorie.Count} categorie caricate"
                : "Nessun catalogo importato";
            InfoVersione.Text = $"Versione {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";
        }

        private TipoCassaFiscale TipoScelto => SceltaTipo.SelectedIndex == 1 ? TipoCassaFiscale.Epson : TipoCassaFiscale.RT3i;

        /// <summary>Cambiando tipo, propone la porta predefinita se era quella dell'altro tipo.</summary>
        private void OnTipoCambiato(object sender, EventArgs e)
        {
            if (!int.TryParse(CampoPorta.Text, out int porta) ||
                porta == FiscalConnectorFactory.PortaDefault(TipoCassaFiscale.RT3i) ||
                porta == FiscalConnectorFactory.PortaDefault(TipoCassaFiscale.Epson))
                CampoPorta.Text = FiscalConnectorFactory.PortaDefault(TipoScelto).ToString();
        }

        /// <summary>Legge i campi; restituisce null e mostra l'errore se qualcosa non va.</summary>
        private async Task<Impostazioni> LeggiCampiAsync()
        {
            string errore = null;
            string ip = (CampoIP.Text ?? "").Trim();
            if (!IPAddress.TryParse(ip, out _)) errore = "Indirizzo IP non valido.";
            else if (!int.TryParse(CampoPorta.Text, out int porta) || porta < 1 || porta > 65535) errore = "Porta non valida.";
            else if (!int.TryParse(CampoOperatore.Text, out int op) || op < 1 || op > 99) errore = "Operatore RT non valido (1-99).";
            else if (!int.TryParse(CampoTimeoutConn.Text, out int tc) || tc < 1000) errore = "Timeout connessione: minimo 1000 ms.";
            else if (!int.TryParse(CampoTimeoutRisp.Text, out int tr) || tr < 1000) errore = "Timeout risposta: minimo 1000 ms.";
            else
                return new Impostazioni
                {
                    TipoCassa = TipoScelto,
                    IP_RT = ip,
                    Porta_RT = porta,
                    OperatoreRT = op,
                    TimeoutConnessioneRT_ms = tc,
                    TimeoutRispostaRT_ms = tr,
                    AccorpaRigheScontrino = SwAccorpa.IsToggled,
                    ApriCassetto = SwCassetto.IsToggled,
                    PinImpostazioni = (CampoPin.Text ?? "").Trim()
                };

            await DisplayAlert("Impostazioni", errore, "OK");
            return null;
        }

        private async void OnSalva(object sender, EventArgs e)
        {
            var nuove = await LeggiCampiAsync();
            if (nuove == null) return;
            try
            {
                ContestoCassa.SalvaImpostazioni(nuove);
                LogManager.Info($"Impostazioni salvate: {nuove.TipoCassa} {nuove.IP_RT}:{nuove.Porta_RT}");
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Impostazioni", $"Salvataggio non riuscito:\n{ex.Message}", "OK");
            }
        }

        /// <summary>Prova con i valori sullo schermo, anche se non ancora salvati.</summary>
        private async void OnProva(object sender, EventArgs e)
        {
            var prova = await LeggiCampiAsync();
            if (prova == null) return;
            var btn = (Button)sender;
            btn.IsEnabled = false;
            btn.Text = "PROVA IN CORSO...";
            try
            {
                var esito = await Task.Run(() => new FunzioniCassa(() => prova).TestConnessioneAsync());
                await DisplayAlert(esito.Ok ? "Connessione riuscita" : "Connessione fallita", esito.Messaggio, "OK");
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Text = "PROVA CONNESSIONE";
            }
        }

        private async void OnImportaCatalogo(object sender, EventArgs e)
        {
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Scegli il file pos.db" });
                if (file == null) return;

                using var stream = await file.OpenReadAsync();
                int n = await Task.Run(() => ContestoCassa.Catalogo.Importa(stream));
                AggiornaInfo();
                await DisplayAlert("Catalogo", $"Importati {n} prodotti in {ContestoCassa.Catalogo.Categorie.Count} categorie.", "OK");
            }
            catch (Exception ex)
            {
                LogManager.Error($"Import catalogo: {ex}");
                await DisplayAlert("Catalogo", $"Import non riuscito:\n{ex.Message}", "OK");
            }
        }

        private async void OnCondividiLog(object sender, EventArgs e)
        {
            string percorso = LogManager.Percorso;
            if (percorso == null || !File.Exists(percorso))
            {
                await DisplayAlert("Log", "Nessun log presente.", "OK");
                return;
            }
            // Copia: il file originale può essere in scrittura
            string copia = Path.Combine(FileSystem.CacheDirectory, $"postouch_log_{DateTime.Now:yyyyMMdd_HHmm}.txt");
            File.Copy(percorso, copia, overwrite: true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Log POS Touch",
                File = new ShareFile(copia)
            });
        }
    }
}
