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
        /// <summary>Campi da scrivere anche in basso: la pagina si accorcia sopra la tastiera.</summary>
        protected override bool AccorciaConTastiera => true;

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
        private void OnTabSaleStampe(object sender, EventArgs e) => MostraScheda(4);

        private void MostraScheda(int n)
        {
            var pannelli = new View[] { PannelloConnessioni, PannelloVendita, PannelloSicurezza, PannelloAvanzate, PannelloSaleStampe };
            var schede = new[] { TabConnessioni, TabVendita, TabSicurezza, TabAvanzate, TabSaleStampe };
            for (int i = 0; i < pannelli.Length; i++)
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
            ChkVirgolaImplicita.IsChecked = i.VirgolaImplicita;
            ChkRigaSingola.IsChecked = i.BloccoRigaSingola;
            ChkBilancia.IsChecked = i.UsaBarcodeBilancia;
            ChkVarScontrino.IsChecked = i.VariantiSuScontrino;
            ChkVarConto.IsChecked = i.VariantiSuConto;
            ChkVarGruppi.IsChecked = i.VariantiPerGruppo;
            ChkTermica.IsChecked = i.AbilitaStampanteTermica;
            CampoIntest1.Text = i.IntestazioneConto1;
            CampoIntest2.Text = i.IntestazioneConto2;
            CampoIntest3.Text = i.IntestazioneConto3;
            ChkAccorpaConto.IsChecked = i.AccorpaRigheConto;
            ChkNumeroConto.IsChecked = i.StampaNumeroConto;
            ChkMediaCoperto.IsChecked = i.StampaMediaPerCoperto;
            ChkRigaPiuMeno.IsChecked = i.MostraRigaPiuMeno;
            SceltaTema.Voci = new List<string> { "Classico", "Scuro", "Chiaro" };
            SceltaTema.Indice = Math.Clamp(i.TemaInterfaccia, 0, 2);
            ChkAccorpaVideo.IsChecked = i.AccorpaRigheVideo;
            ChkAccorpaScontrino.IsChecked = i.AccorpaRigheScontrino;
            RbPiccola.IsChecked = i.DimensioniTastiCategorie == "Piccola";
            RbGrande.IsChecked = i.DimensioniTastiCategorie == "Grande";
            RbMedia.IsChecked = !RbPiccola.IsChecked && !RbGrande.IsChecked;
            _fontCategorie = Math.Clamp(i.FontSizeCategorie, 8, 20);
            AggiornaFont();

            _password = i.PinImpostazioni ?? "";
            AggiornaAssistenza();
            AggiornaPassword();
            CaricaOperatori();
            ChkFondoCassa.IsChecked = i.ChiediFondoCassa;
            ChkProponiFondo.IsChecked = i.ProponiFondoPrecedente;
            ChkMovimentiCassa.IsChecked = i.AbilitaMovimentiCassa;
            ChkFondoLasciato.IsChecked = i.ChiediFondoLasciato;
            ChkConteggioObbligatorio.IsChecked = i.ConteggioObbligatorio;

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
            n.VirgolaImplicita = ChkVirgolaImplicita.IsChecked;
            n.BloccoRigaSingola = ChkRigaSingola.IsChecked;
            n.UsaBarcodeBilancia = ChkBilancia.IsChecked;
            n.VariantiSuScontrino = ChkVarScontrino.IsChecked;
            n.VariantiSuConto = ChkVarConto.IsChecked;
            n.VariantiPerGruppo = ChkVarGruppi.IsChecked;
            n.AbilitaStampanteTermica = ChkTermica.IsChecked;
            n.IntestazioneConto1 = (CampoIntest1.Text ?? "").Trim();
            n.IntestazioneConto2 = (CampoIntest2.Text ?? "").Trim();
            n.IntestazioneConto3 = (CampoIntest3.Text ?? "").Trim();
            n.AccorpaRigheConto = ChkAccorpaConto.IsChecked;
            n.StampaNumeroConto = ChkNumeroConto.IsChecked;
            n.StampaMediaPerCoperto = ChkMediaCoperto.IsChecked;
            n.MostraRigaPiuMeno = ChkRigaPiuMeno.IsChecked;
            n.TemaInterfaccia = Math.Clamp(SceltaTema.Indice, 0, 2);
            n.AccorpaRigheVideo = ChkAccorpaVideo.IsChecked;
            n.AccorpaRigheScontrino = ChkAccorpaScontrino.IsChecked;
            n.DimensioniTastiCategorie = RbPiccola.IsChecked ? "Piccola" : RbGrande.IsChecked ? "Grande" : "Media";
            n.FontSizeCategorie = _fontCategorie;
            n.PinImpostazioni = _password;
            n.ChiediFondoCassa = ChkFondoCassa.IsChecked;
            n.ProponiFondoPrecedente = ChkProponiFondo.IsChecked;
            n.AbilitaMovimentiCassa = ChkMovimentiCassa.IsChecked;
            n.ChiediFondoLasciato = ChkFondoLasciato.IsChecked;
            n.ConteggioObbligatorio = ChkConteggioObbligatorio.IsChecked;
            n.TimeoutConnessioneRT_ms = tc;
            n.TimeoutRispostaRT_ms = tr;
            n.LivelloLog = RbLogError.IsChecked ? "ERROR" : RbLogWarn.IsChecked ? "WARN" : RbLogInfo.IsChecked ? "INFO" : "DEBUG";
            return n;
        }

        /// <summary>Riassunto dei campi per capire se ci sono modifiche non salvate.</summary>
        private string Firma() => string.Join("|", SceltaTipo.Indice, CampoIP.Text, CampoPorta.Text, CampoOperatore.Text, ChkCassetto.IsChecked,
            CampoNomePV.Text, CampoSottotitoloPV.Text, ChkSconti.IsChecked, ChkCambioPrezzo.IsChecked, ChkModificaVendita.IsChecked,
            ChkMostraStrumenti.IsChecked, ChkPrezzoZero.IsChecked, ChkBloccoQuantita.IsChecked, ChkVirgolaImplicita.IsChecked, ChkRigaSingola.IsChecked, ChkBilancia.IsChecked, ChkVarScontrino.IsChecked, ChkVarConto.IsChecked, ChkVarGruppi.IsChecked, ChkRigaPiuMeno.IsChecked, SceltaTema.Indice, ChkTermica.IsChecked,
            CampoIntest1.Text, CampoIntest2.Text, CampoIntest3.Text, ChkAccorpaConto.IsChecked, ChkNumeroConto.IsChecked, ChkAccorpaVideo.IsChecked,
            ChkAccorpaScontrino.IsChecked, RbPiccola.IsChecked, RbGrande.IsChecked, _fontCategorie, _password, CampoTimeoutConn.Text,
            CampoTimeoutRisp.Text, RbLogDebug.IsChecked, RbLogInfo.IsChecked, RbLogWarn.IsChecked,
            ChkFondoCassa.IsChecked, ChkProponiFondo.IsChecked, ChkMovimentiCassa.IsChecked, ChkFondoLasciato.IsChecked, ChkConteggioObbligatorio.IsChecked, ChkMediaCoperto.IsChecked);

    }
}
