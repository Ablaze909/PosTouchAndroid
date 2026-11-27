using System.Net;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Cassa;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;

namespace PosTouch.App.Pagine
{
    public partial class ImpostazioniPage
    {
        // ══════════════ azioni ══════════════

        /// <summary>Cambiando tipo propone la porta predefinita (1723 per 3i, 9100 per Epson).</summary>
        private void OnTipoCambiato(object sender, EventArgs e)
        {
            if (!int.TryParse(CampoPorta.Text, out int porta) ||
                porta == FiscalConnectorFactory.PortaDefault(TipoCassaFiscale.RT3i) ||
                porta == FiscalConnectorFactory.PortaDefault(TipoCassaFiscale.Epson))
                CampoPorta.Text = FiscalConnectorFactory.PortaDefault(TipoScelto).ToString();
        }

        private void OnFontMeno(object sender, EventArgs e) { _fontCategorie = Math.Max(8, _fontCategorie - 1); AggiornaFont(); }
        private void OnFontPiu(object sender, EventArgs e) { _fontCategorie = Math.Min(20, _fontCategorie + 1); AggiornaFont(); }

        private void AggiornaFont()
        {
            TestoFontCategorie.Text = _fontCategorie.ToString();
            EsempioCategoria.FontSize = _fontCategorie + 6;
        }

        private async void OnCambiaPassword(object sender, TappedEventArgs e)
        {
            string nuova = await TastierinoPrezzo.ChiediCodice("🔑 NUOVA PASSWORD ADMIN\n(vuota = nessuna)", nascondi: true);
            if (nuova == null) return;
            if (nuova.Length > 0)
            {
                string conferma = await TastierinoPrezzo.ChiediCodice("🔑 CONFERMA PASSWORD", nascondi: true);
                if (conferma == null) return;
                if (conferma != nuova)
                {
                    await MessaggioBox.Errore("PASSWORD", "Le due password non coincidono.");
                    return;
                }
            }
            _password = nuova;
            AggiornaPassword();
        }

        private void AggiornaPassword()
            => TestoPassword.Text = string.IsNullOrEmpty(_password) ? "Non configurata (clicca per impostare)" : "●●●● (clicca per cambiare)";

        private async void OnProva(object sender, EventArgs e)
        {
            var prova = await LeggiAsync();
            if (prova == null) return;
            BtnTest.IsEnabled = false;
            BtnTest.Text = "⏳ TEST IN CORSO...";
            try
            {
                var esito = await Task.Run(() => new FunzioniCassa(() => prova).TestConnessioneAsync());
                if (esito.Ok) await MessaggioBox.Successo("CONNESSIONE RIUSCITA", esito.Messaggio);
                else await MessaggioBox.Errore("CONNESSIONE FALLITA", esito.Messaggio);
            }
            finally
            {
                BtnTest.IsEnabled = true;
                BtnTest.Text = "🔌 TEST CONNESSIONE RT";
            }
        }

        private async void OnConfiguraSale(object sender, EventArgs e) => await Navigazione.Apri(this, () => new SaleConfigPage());


        /// <summary>🖨️ STAMPE: pagina delle opzioni di stampa come POSBY (salva da sé).</summary>
        private async void OnStampe(object sender, EventArgs e) => await Navigazione.Apri(this, () => new StampePage());

        private async void OnSalva(object sender, EventArgs e)
        {
            await Ui.ChiudiTastieraAsync(this);   // consegna l'ultima parola scritta
            var nuove = await LeggiAsync();
            if (nuove == null) return;
            try
            {
                bool temaCambiato = nuove.TemaInterfaccia != ContestoCassa.Impostazioni.TemaInterfaccia;
                ContestoCassa.SalvaImpostazioni(nuove);
                _statoIniziale = Firma();
                TestoStato.Text = $"✅ Impostazioni salvate alle {DateTime.Now:HH:mm:ss}";
                if (temaCambiato) await AvvisoTemaAsync();
                LogManager.Info($"Impostazioni salvate: {nuove.TipoCassa} {nuove.IP_RT}:{nuove.Porta_RT}");
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", $"Salvataggio non riuscito:\n{ex.Message}");
            }
        }

        private static Task AvvisoTemaAsync() => MessaggioBox.Info("ASPETTO CAMBIATO",
            "Il nuovo aspetto vale subito per la vendita, le finestre e le pagine che apri da adesso.\n\n" +
            "La schermata iniziale e Strumenti, già aperte, si aggiornano al prossimo avvio: chiudi e riapri l'app quando puoi.");

        /// <summary>Tasto indietro di Android = ✖ CHIUDI (chiede se salvare).</summary>
        protected override bool Indietro() { OnChiudi(this, EventArgs.Empty); return true; }

        private bool _chiusura;

        private async void OnChiudi(object sender, EventArgs e)
        {
            if (_chiusura) return;
            _chiusura = true;
            try
            {
                await Ui.ChiudiTastieraAsync(this);   // la parola in scrittura conta per "modifiche non salvate"
                if (Firma() != _statoIniziale &&
                    await MessaggioBox.Domanda("MODIFICHE NON SALVATE", "Salvare le impostazioni prima di chiudere?"))
                {
                    var nuove = await LeggiAsync();
                    if (nuove == null) return;
                    bool temaCambiato = nuove.TemaInterfaccia != ContestoCassa.Impostazioni.TemaInterfaccia;
                    ContestoCassa.SalvaImpostazioni(nuove);
                    if (temaCambiato) await AvvisoTemaAsync();   // anche salvando all'uscita
                }
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex) { await MessaggioBox.Errore("IMPOSTAZIONI", ex.Message); }
            finally { _chiusura = false; }
        }

        private async void OnImportaCatalogo(object sender, EventArgs e)
        {
            // con tavoli o scontrino aperti gli articoli cambierebbero sotto le righe già battute
            var blocco = ManutenzioneDati.BloccoDatiProva();
            if (blocco != null) { await MessaggioBox.Avviso("IMPORTA CATALOGO", blocco.Replace("caricare i dati di prova", "importare il catalogo")); return; }
            if (ContestoCassa.Catalogo.TuttiProdotti.Count > 0 &&
                !await MessaggioBox.Conferma("IMPORTA CATALOGO",
                    "Il catalogo attuale del tablet (categorie e articoli) verrà SOSTITUITO da quello del file scelto.\n\n" +
                    "Copia pos.db dal PC con POS Touch CHIUSO, altrimenti le ultime modifiche potrebbero mancare.\n\nContinuare?"))
                return;
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Scegli il file pos.db" });
                if (file == null) return;
                using var stream = await file.OpenReadAsync();
                int n = await ManutenzioneDati.ImportaCatalogoAsync(stream);
                MostraInfoCatalogo();
                await MessaggioBox.Successo("CATALOGO IMPORTATO", $"Importati {n} prodotti in {ContestoCassa.Catalogo.TutteCategorie.Count} categorie.");
                await AvvisaUsciteMancantiAsync();
            }
            catch (Exception ex)
            {
                LogManager.Error($"Import catalogo: {ex}");
                await MessaggioBox.Errore("IMPORT NON RIUSCITO", ex.Message);
            }
        }

        /// <summary>🧪 Dati di prova (vedi <see cref="ManutenzioneDati.CaricaDatiProvaAsync"/>).</summary>
        private async void OnDatiProva(object sender, EventArgs e)
        {
            var blocco = ManutenzioneDati.BloccoDatiProva();
            if (blocco != null) { await MessaggioBox.Avviso("DATI DI PROVA", blocco); return; }
            if (!await MessaggioBox.Conferma("🧪 DATI DI PROVA",
                    "Il catalogo del tablet verrà SOSTITUITO da un catalogo di prova (10 categorie, 58 articoli, 25 varianti).\n\n" +
                    "Vengono impostate anche: 2 sale (SALA 1 e TERRAZZA) con coperto, conti clienti, portate, uscite CUCINA / BAR / PIZZERIA.\n" +
                    "Le stampanti e le impostazioni fiscali NON cambiano.\n\nIl catalogo attuale resta salvato come copia (.bak).\n\nContinuare?"))
                return;
            try
            {
                int n = await ManutenzioneDati.CaricaDatiProvaAsync();
                MostraInfoCatalogo();
                await MessaggioBox.Successo("🧪 DATI DI PROVA CARICATI",
                    $"{n} articoli in {ContestoCassa.Catalogo.TutteCategorie.Count} categorie.\n\n" +
                    "Da provare:\n• PIZZA ½ + ½ e PIZZA 3 GUSTI (combinazioni)\n• GASTRONOMIA a peso (etichetta bilancia 2 00011 …)\n" +
                    "• Barcode bevande (es. 5449000000996 = COCA COLA)\n• VAR＋/VAR﹣ su pizze, primi, secondi, bevande\n• ⬅ SALA: SALA 1 (1-10) e TERRAZZA (101-110)\n\n" +
                    "Imposta gli IP delle stampanti in 🍽️ SALE E STAMPE → OPZIONI DI STAMPA.");
                _statoIniziale = Firma();
            }
            catch (Exception ex)
            {
                LogManager.Error($"Dati di prova: {ex}");
                await MessaggioBox.Errore("DATI DI PROVA", ex.Message);
            }
        }

        private void MostraInfoCatalogo() =>
            InfoCatalogo.Text = $"{ContestoCassa.Catalogo.TutteCategorie.Count} categorie, {ContestoCassa.Catalogo.TuttiProdotti.Count} prodotti";

        /// <summary>Dopo l'import: uscite usate dal catalogo del PC ma non impostate qui (le comande non uscirebbero).</summary>
        private static async Task AvvisaUsciteMancantiAsync()
        {
            var usati = ManutenzioneDati.UsciteMancanti();
            if (usati.Count > 0)
                await MessaggioBox.Avviso("CENTRI DI PRODUZIONE", $"Il catalogo usa uscite (centri) non impostate sul tablet (n. {string.Join(", ", usati)}).\n\n" +
                                          "Aggiungi le uscite con quei numeri in 🖨️ STAMPE, altrimenti quegli articoli non vanno in comanda.");
        }

        private async void OnCondividiLog(object sender, EventArgs e)
        {
            try
            {
                var copia = ManutenzioneDati.CopiaLog();
                if (copia == null) { await MessaggioBox.Info("LOG", "Nessun log presente."); return; }
                await Share.Default.RequestAsync(new ShareFileRequest { Title = "Log POS Touch", File = new ShareFile(copia) });
            }
            catch (Exception ex) { await MessaggioBox.Errore("LOG", ex.Message); }
        }
    }
}
