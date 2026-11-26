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
    /// <summary>Copia di ImpostazioniDialog del PC con le voci già disponibili su Android.</summary>
    public partial class ImpostazioniPage : PaginaPos
    {
        private int _fontCategorie;
        private string _password;
        private string _statoIniziale;

        public ImpostazioniPage()
        {
            InitializeComponent();
            Carica(ContestoCassa.Impostazioni);
            MostraScheda(0);
            var cat = ContestoCassa.Catalogo;
            InfoCatalogo.Text = cat.Presente
                ? $"{cat.TutteCategorie.Count} categorie, {cat.TuttiProdotti.Count} prodotti"
                : "Nessun catalogo";
            InfoVersione.Text = $"POS Touch Android v{AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";
            _statoIniziale = Firma();
        }

        // ══════════════ schede ══════════════

        private void OnTabConnessioni(object sender, EventArgs e) => MostraScheda(0);
        private void OnTabVendita(object sender, EventArgs e) => MostraScheda(1);
        private void OnTabSicurezza(object sender, EventArgs e) => MostraScheda(2);
        private void OnTabAvanzate(object sender, EventArgs e) => MostraScheda(3);

        private void MostraScheda(int n)
        {
            var pannelli = new View[] { PannelloConnessioni, PannelloVendita, PannelloSicurezza, PannelloAvanzate };
            var schede = new[] { TabConnessioni, TabVendita, TabSicurezza, TabAvanzate };
            for (int i = 0; i < 4; i++)
            {
                pannelli[i].IsVisible = i == n;
                schede[i].BackgroundColor = Ui.C(i == n ? "#FF9800" : "#FFFFFF");
                schede[i].TextColor = Ui.C(i == n ? "#FFFFFF" : "#333333");
            }
        }

        // ══════════════ caricamento / lettura ══════════════

        private void Carica(Impostazioni i)
        {
            SceltaTipo.Voci = new List<string> { "3i (XON/XOFF)", "Epson (Communication Protocol)" };
            SceltaTipo.Indice = i.TipoCassa == TipoCassaFiscale.Epson ? 1 : 0;
            CampoIP.Text = i.IP_RT;
            CampoPorta.Text = i.Porta_RT.ToString();
            CampoOperatore.Text = i.OperatoreRT.ToString();
            ChkCassetto.IsChecked = i.ApriCassetto;
            CampoNomePV.Text = i.NomePuntoVendita;
            CampoSottotitoloPV.Text = i.SottotitoloPuntoVendita;

            ChkSconti.IsChecked = i.AbilitaSconti;
            ChkCambioPrezzo.IsChecked = i.AbilitaCambioPrezzo;
            ChkModificaVendita.IsChecked = i.AbilitaModificaVendita;
            ChkMostraStrumenti.IsChecked = i.MostraStrumentiVendita;
            ChkPrezzoZero.IsChecked = i.RichiestaPrezzoArticoliZero;
            ChkBloccoQuantita.IsChecked = i.BloccoModificaQuantita;
            ChkRigaSingola.IsChecked = i.BloccoRigaSingola;
            ChkBilancia.IsChecked = i.UsaBarcodeBilancia;
            ChkVarScontrino.IsChecked = i.VariantiSuScontrino;
            ChkVarConto.IsChecked = i.VariantiSuConto;
            ChkVarGruppi.IsChecked = i.VariantiPerGruppo;
            ChkTermica.IsChecked = i.AbilitaStampanteTermica;
            CampoIPTermica.Text = i.IP_Termica;
            CampoPortaTermica.Text = i.Porta_Termica.ToString();
            CampoIntest1.Text = i.IntestazioneConto1;
            CampoIntest2.Text = i.IntestazioneConto2;
            CampoIntest3.Text = i.IntestazioneConto3;
            ChkAccorpaConto.IsChecked = i.AccorpaRigheConto;
            ChkNumeroConto.IsChecked = i.StampaNumeroConto;
            ChkRigaPiuMeno.IsChecked = i.MostraRigaPiuMeno;
            ChkAccorpaVideo.IsChecked = i.AccorpaRigheVideo;
            ChkAccorpaScontrino.IsChecked = i.AccorpaRigheScontrino;
            RbPiccola.IsChecked = i.DimensioniTastiCategorie == "Piccola";
            RbGrande.IsChecked = i.DimensioniTastiCategorie == "Grande";
            RbMedia.IsChecked = !RbPiccola.IsChecked && !RbGrande.IsChecked;
            _fontCategorie = Math.Clamp(i.FontSizeCategorie, 8, 20);
            AggiornaFont();

            _password = i.PinImpostazioni ?? "";
            AggiornaPassword();

            CampoTimeoutConn.Text = i.TimeoutConnessioneRT_ms.ToString();
            CampoTimeoutRisp.Text = i.TimeoutRispostaRT_ms.ToString();
            RbLogInfo.IsChecked = i.LivelloLog == "INFO";
            RbLogWarn.IsChecked = i.LivelloLog == "WARN";
            RbLogError.IsChecked = i.LivelloLog == "ERROR";
            RbLogDebug.IsChecked = !RbLogInfo.IsChecked && !RbLogWarn.IsChecked && !RbLogError.IsChecked;
        }

        private TipoCassaFiscale TipoScelto => SceltaTipo.Indice == 1 ? TipoCassaFiscale.Epson : TipoCassaFiscale.RT3i;

        /// <summary>Legge i campi; restituisce null (e mostra l'errore) se qualcosa non va.</summary>
        private async Task<Impostazioni> LeggiAsync()
        {
            string ip = (CampoIP.Text ?? "").Trim();
            string errore = null;
            int porta = 0, op = 0, tc = 0, tr = 0;
            if (!IPAddress.TryParse(ip, out _)) errore = "Indirizzo IP non valido.";
            else if (!int.TryParse(CampoPorta.Text, out porta) || porta < 1 || porta > 65535) errore = "Porta TCP non valida.";
            else if (!int.TryParse(CampoOperatore.Text, out op) || op < 1 || op > 99) errore = "Operatore RT non valido (1-99).";
            else if (!int.TryParse(CampoTimeoutConn.Text, out tc) || tc < 1000) errore = "Timeout connessione: minimo 1000 ms.";
            else if (!int.TryParse(CampoTimeoutRisp.Text, out tr) || tr < 1000) errore = "Timeout risposta: minimo 1000 ms.";
            string ipTermica = (CampoIPTermica.Text ?? "").Trim();
            int portaTermica = 0;
            if (errore == null && ChkTermica.IsChecked)
            {
                if (!IPAddress.TryParse(ipTermica, out _)) errore = "Indirizzo IP della stampante termica non valido.";
                else if (!int.TryParse(CampoPortaTermica.Text, out portaTermica) || portaTermica < 1 || portaTermica > 65535) errore = "Porta della stampante termica non valida.";
            }
            else if (!int.TryParse(CampoPortaTermica.Text, out portaTermica)) portaTermica = 9100;
            if (errore != null)
            {
                await MessaggioBox.Avviso("VALORE NON VALIDO", errore);
                return null;
            }

            var n = ContestoCassa.Impostazioni.Copia();
            n.TipoCassa = TipoScelto;
            n.IP_RT = ip;
            n.Porta_RT = porta;
            n.OperatoreRT = op;
            n.ApriCassetto = ChkCassetto.IsChecked;
            n.NomePuntoVendita = (CampoNomePV.Text ?? "").Trim();
            n.SottotitoloPuntoVendita = (CampoSottotitoloPV.Text ?? "").Trim();
            n.AbilitaSconti = ChkSconti.IsChecked;
            n.AbilitaCambioPrezzo = ChkCambioPrezzo.IsChecked;
            n.AbilitaModificaVendita = ChkModificaVendita.IsChecked;
            n.MostraStrumentiVendita = ChkMostraStrumenti.IsChecked;
            n.RichiestaPrezzoArticoliZero = ChkPrezzoZero.IsChecked;
            n.BloccoModificaQuantita = ChkBloccoQuantita.IsChecked;
            n.BloccoRigaSingola = ChkRigaSingola.IsChecked;
            n.UsaBarcodeBilancia = ChkBilancia.IsChecked;
            n.VariantiSuScontrino = ChkVarScontrino.IsChecked;
            n.VariantiSuConto = ChkVarConto.IsChecked;
            n.VariantiPerGruppo = ChkVarGruppi.IsChecked;
            n.AbilitaStampanteTermica = ChkTermica.IsChecked;
            n.IP_Termica = ipTermica;
            n.Porta_Termica = portaTermica;
            n.IntestazioneConto1 = (CampoIntest1.Text ?? "").Trim();
            n.IntestazioneConto2 = (CampoIntest2.Text ?? "").Trim();
            n.IntestazioneConto3 = (CampoIntest3.Text ?? "").Trim();
            n.AccorpaRigheConto = ChkAccorpaConto.IsChecked;
            n.StampaNumeroConto = ChkNumeroConto.IsChecked;
            n.MostraRigaPiuMeno = ChkRigaPiuMeno.IsChecked;
            n.AccorpaRigheVideo = ChkAccorpaVideo.IsChecked;
            n.AccorpaRigheScontrino = ChkAccorpaScontrino.IsChecked;
            n.DimensioniTastiCategorie = RbPiccola.IsChecked ? "Piccola" : RbGrande.IsChecked ? "Grande" : "Media";
            n.FontSizeCategorie = _fontCategorie;
            n.PinImpostazioni = _password;
            n.TimeoutConnessioneRT_ms = tc;
            n.TimeoutRispostaRT_ms = tr;
            n.LivelloLog = RbLogError.IsChecked ? "ERROR" : RbLogWarn.IsChecked ? "WARN" : RbLogInfo.IsChecked ? "INFO" : "DEBUG";
            return n;
        }

        /// <summary>Riassunto dei campi per capire se ci sono modifiche non salvate.</summary>
        private string Firma() => string.Join("|", SceltaTipo.Indice, CampoIP.Text, CampoPorta.Text, CampoOperatore.Text, ChkCassetto.IsChecked,
            CampoNomePV.Text, CampoSottotitoloPV.Text, ChkSconti.IsChecked, ChkCambioPrezzo.IsChecked, ChkModificaVendita.IsChecked,
            ChkMostraStrumenti.IsChecked, ChkPrezzoZero.IsChecked, ChkBloccoQuantita.IsChecked, ChkRigaSingola.IsChecked, ChkBilancia.IsChecked, ChkVarScontrino.IsChecked, ChkVarConto.IsChecked, ChkVarGruppi.IsChecked, ChkRigaPiuMeno.IsChecked, ChkTermica.IsChecked, CampoIPTermica.Text, CampoPortaTermica.Text,
            CampoIntest1.Text, CampoIntest2.Text, CampoIntest3.Text, ChkAccorpaConto.IsChecked, ChkNumeroConto.IsChecked, ChkAccorpaVideo.IsChecked,
            ChkAccorpaScontrino.IsChecked, RbPiccola.IsChecked, RbGrande.IsChecked, _fontCategorie, _password, CampoTimeoutConn.Text,
            CampoTimeoutRisp.Text, RbLogDebug.IsChecked, RbLogInfo.IsChecked, RbLogWarn.IsChecked);

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

        private bool _provaTermica;

        /// <summary>🖨️ Stampa di prova con IP e porta scritti nei campi (anche prima di salvare).</summary>
        private async void OnProvaTermica(object sender, EventArgs e)
        {
            if (_provaTermica) return;
            string ip = (CampoIPTermica.Text ?? "").Trim();
            if (!IPAddress.TryParse(ip, out _) || !int.TryParse(CampoPortaTermica.Text, out int porta) || porta < 1 || porta > 65535)
            {
                await MessaggioBox.Avviso("VALORE NON VALIDO", "Inserire IP e porta validi della stampante termica.");
                return;
            }
            _provaTermica = true;
            BtnProvaTermica.Text = "⏳ Stampa in corso...";
            try
            {
                string intestazione = CampoIntest1.Text;
                var (ok, msg) = await Task.Run(() => PosTouch.Core.Conti.ServizioConto.ProvaAsync(ip, porta, intestazione));
                if (ok) await MessaggioBox.Successo("STAMPANTE TERMICA", msg);
                else await MessaggioBox.Errore("STAMPANTE TERMICA", msg);
            }
            finally
            {
                _provaTermica = false;
                BtnProvaTermica.Text = "🖨️ STAMPA DI PROVA";
            }
        }

        private async void OnSalva(object sender, EventArgs e)
        {
            var nuove = await LeggiAsync();
            if (nuove == null) return;
            try
            {
                ContestoCassa.SalvaImpostazioni(nuove);
                _statoIniziale = Firma();
                TestoStato.Text = $"✅ Impostazioni salvate alle {DateTime.Now:HH:mm:ss}";
                LogManager.Info($"Impostazioni salvate: {nuove.TipoCassa} {nuove.IP_RT}:{nuove.Porta_RT}");
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", $"Salvataggio non riuscito:\n{ex.Message}");
            }
        }

        /// <summary>Tasto indietro di Android = ✖ CHIUDI (chiede se salvare).</summary>
        protected override bool Indietro() { OnChiudi(this, EventArgs.Empty); return true; }

        private bool _chiusura;

        private async void OnChiudi(object sender, EventArgs e)
        {
            if (_chiusura) return;
            _chiusura = true;
            try
            {
                if (Firma() != _statoIniziale &&
                    await MessaggioBox.Domanda("MODIFICHE NON SALVATE", "Salvare le impostazioni prima di chiudere?"))
                {
                    var nuove = await LeggiAsync();
                    if (nuove == null) return;
                    ContestoCassa.SalvaImpostazioni(nuove);
                }
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex) { await MessaggioBox.Errore("IMPOSTAZIONI", ex.Message); }
            finally { _chiusura = false; }
        }

        private async void OnImportaCatalogo(object sender, EventArgs e)
        {
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
                int n = await Task.Run(() => ContestoCassa.Catalogo.Importa(stream));
                InfoCatalogo.Text = $"{ContestoCassa.Catalogo.TutteCategorie.Count} categorie, {n} prodotti";
                await MessaggioBox.Successo("CATALOGO IMPORTATO", $"Importati {n} prodotti in {ContestoCassa.Catalogo.TutteCategorie.Count} categorie.");
                await AvvisaCentriMancantiAsync();
            }
            catch (Exception ex)
            {
                LogManager.Error($"Import catalogo: {ex}");
                await MessaggioBox.Errore("IMPORT NON RIUSCITO", ex.Message);
            }
        }

        /// <summary>Dopo l'import: centri di produzione usati dal catalogo del PC ma non impostati qui (le comande non uscirebbero).</summary>
        private async Task AvvisaCentriMancantiAsync()
        {
            var cfg = ContestoCassa.Impostazioni.Sale;
            if (!cfg.AbilitaSale) return;
            var noti = cfg.Centri.Select(c => c.Id).ToHashSet();
            var usati = ContestoCassa.Catalogo.TutteCategorie.Select(c => c.CentroProduzioneId)
                        .Concat(ContestoCassa.Catalogo.TuttiProdotti.Select(p => p.CentroProduzioneId))
                        .Where(id => id > 0 && !noti.Contains(id)).Distinct().OrderBy(id => id).ToList();
            if (usati.Count > 0)
                await MessaggioBox.Avviso("CENTRI DI PRODUZIONE", $"Il catalogo usa centri non impostati sul tablet (n. {string.Join(", ", usati)}).\n\n" +
                                          "Crea i centri in 🍽️ SALE E COMANDE, altrimenti quegli articoli non vanno in comanda.");
        }

        private async void OnCondividiLog(object sender, EventArgs e)
        {
            string percorso = LogManager.Percorso;
            if (percorso == null || !File.Exists(percorso))
            {
                await MessaggioBox.Info("LOG", "Nessun log presente.");
                return;
            }
            string copia = Path.Combine(FileSystem.CacheDirectory, $"postouch_log_{DateTime.Now:yyyyMMdd_HHmm}.txt");
            File.Copy(percorso, copia, overwrite: true);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Log POS Touch", File = new ShareFile(copia) });
        }
    }
}
