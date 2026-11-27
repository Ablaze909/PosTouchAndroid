using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// VenditaPage — modalità TAVOLO (VenditaContoClienteDialog del PC): stessa schermata della vendita al banco
    /// con lo scontrino del tavolo, barra CASSA / CONTO / PRECONTO / NO STAMPA / AVANZA
    /// e 🚪 che diventa ⬅ SALA (salva e manda la comanda).
    /// </summary>
    public partial class VenditaPage
    {
        private SessioneTavolo _sessione;
        private Carrello _carrelloCollegato;
        private IDispatcherTimer _timerBozza;

        private void CollegaCarrello(SessioneTavolo sessione)
        {
            _combinazione = null;
            OverlayPagamento.IsVisible = false;   // il popup CASSA non deve restare aperto sull'altro conto
            NascondiResto();
            if (_carrelloCollegato != null) _carrelloCollegato.Cambiato -= AggiornaTotale;
            if (_carrelloCollegato != null) _carrelloCollegato.Cambiato -= ProgrammaBozza;
            _sessione = sessione;
            AvvisoSalvataggio.IsVisible = false;   // tavolo nuovo (o banco): l'avviso del tavolo di prima non vale qui
            _carrelloCollegato = Carrello;
            _carrelloCollegato.Cambiato += AggiornaTotale;
            if (sessione != null) _carrelloCollegato.Cambiato += ProgrammaBozza;
            ListaRighe.ItemsSource = _carrelloCollegato.Righe;
            _tastierino.Azzera();
            if (_scontrinoEspanso) ApplicaModalitaEspansa(false);
            AggiornaTotale();
            AggiornaModoTavolo();
            if (BannerCombinazione != null) { BannerCombinazione.IsVisible = false; MostraArticoli(); }
        }

        private void AggiornaModoTavolo()
        {
            bool tavolo = _sessione != null;
            var sale = Cfg.Sale;
            BarraInferiore.IsVisible = !tavolo;
            BarraTavolo.IsVisible = tavolo;
            BtnSale.IsVisible = false;   // come il PC: alle sale si va con 🚪 (il tasto resta solo per compatibilità)
            BtnSale.Text = sale.AbilitaSale ? "🍽️ SALE" : "👤 CONTI";
            AggiornaPosto();
            TitoloScontrino.Text = "📋 SCONTRINO";   // il posto lo dicono già la banda in alto e il totale
            BtnContoTavolo.IsVisible = BtnPreconto.IsVisible = Cfg.AbilitaStampanteTermica;
            BtnAvanza.IsVisible = tavolo && sale.AbilitaPortate && sale.AbilitaComande;
            AggiornaBarraPortate();
            bool inSala = tavolo && !_sessione.IsContoCliente;   // come il PC: sposta/unisci solo per i tavoli delle sale
            BtnSposta.IsVisible = inSala && sale.AbilitaSpostaTavolo;
            BtnUnisci.IsVisible = inSala && sale.AbilitaUnisciConti;
            SistemaRigaTasti();
            SistemaBarraTavolo();
        }

        // ══════════════ APERTURA ══════════════

        private async void OnSale(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_sessione != null || _modalitaModifica) return;
            await Navigazione.Apri(this, () => new TavoliPage());
        }

        /// <summary>Chiamato dalla mappa tavoli dopo essere tornati alla vendita.</summary>
        public async Task ApriTavoloAsync(SessioneTavolo sessione)
        {
            CollegaCarrello(sessione);
            if (sessione.Nuovo) await ChiediCopertiAsync(sessione);
        }

        /// <summary>ChiediCoperti del PC: tavolo nuovo con l'articolo coperto impostato per la sala.</summary>
        private async Task ChiediCopertiAsync(SessioneTavolo s)
        {
            int id = s.Sala?.CopertoProdottoId ?? 0;
            if (id <= 0) return;
            var coperto = ContestoCassa.Catalogo.TuttiProdotti.FirstOrDefault(p => p.Id == id);
            if (coperto == null) return;
            var n = await TastierinoPrezzo.ChiediQuantita("🍽️ QUANTI COPERTI?");
            if (n == null || n <= 0) return;
            if (Carrello.Aggiungi(coperto, Math.Max(1, (int)n.Value), null, false) == null)
                await MessaggioBox.Avviso("COPERTI", Carrello.UltimoErrore);
        }

        // ══════════════ USCITE ══════════════

        /// <summary>⬅ SALA: salva, manda la comanda delle righe nuove e gli storni.</summary>
        private void OnTornaSala() => _ = EsciDalTavoloAsync(s => s.EsciConComanda());
        private async void OnNoStampa(object sender, EventArgs e)
        {
            // NO STAMPA segna tutto come inviato senza stampare: con articoli nuovi si chiede (la cucina non riceverebbe nulla)
            if (!UscitaPossibile()) return;   // prima i controlli, poi la domanda (niente conferma inutile)
            int nuovi = Carrello.Righe.Count(r => r.Tipo == TipoRiga.Prodotto && !r.Comandata);
            if (nuovi > 0 && !await MessaggioBox.Conferma("🚫 NO STAMPA",
                    $"{nuovi} articoli non ancora inviati verranno segnati come comandati SENZA stampare la comanda.\n\nLa cucina non li riceverà. Continuare?")) return;
            await EsciDalTavoloAsync(s => s.EsciNoStampa());
        }
        private bool _uscitaInCorso;

        private bool UscitaPossibile() => !CombinazioneAperta() && _sessione != null && !_uscitaInCorso && !Attesa.IsVisible && !_venditaAvviata;

        private async Task EsciDalTavoloAsync(Func<SessioneTavolo, EsitoTavolo> uscita)
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null || _uscitaInCorso || Attesa.IsVisible || _venditaAvviata) return;
            _uscitaInCorso = true;
            try
            {
                _timerBozza?.Stop();
                CongelaLista();   // il riordino delle portate e lo svuotamento non si vedono più riga per riga
                EsitoTavolo esito;
                try { esito = uscita(_sessione); }
                catch (Exception ex)
                {
                    ScongelaLista();
                    ProgrammaBozza();   // si resta nel tavolo: il salvataggio automatico deve ripartire
                    await MessaggioBox.Errore("ERRORE SALVATAGGIO TAVOLO", $"Il tavolo NON è stato salvato:\n{ex.Message}\n\nRiprova.");
                    return;
                }
                await MostraAvvisiAsync(esito);
                await TornaAllaSalaAsync();
            }
            finally { _uscitaInCorso = false; }
        }

        /// <summary>
        /// Documento uscito al tavolo: comande degli articoli non inviati, storni e liberazione in un solo salvataggio,
        /// prima di svuotare lo scontrino e di mostrare messaggi (niente finestra in cui il tavolo resta vuoto ma non chiuso).
        /// </summary>
        private async Task<EsitoTavolo> ChiudiTavoloPagatoAsync(List<RigaScontrino> venduti)
        {
            if (_sessione == null) return null;
            _timerBozza?.Stop();
            try { return _sessione.ChiudiPagato(venduti); }
            catch (Exception ex)
            {
                LogManager.Error($"Tavolo pagato ma non liberato: {ex}");
                await MessaggioBox.Errore("TAVOLO", $"Tavolo pagato ma non liberato:\n{ex.Message}\n\nControlla il tavolo in sala: NON incassarlo di nuovo.");
                return null;
            }
        }

        /// <summary>Dopo scontrino o conto (tavolo già chiuso): resto, avvisi comande e ritorno alla sala.</summary>
        private async Task DopoPagamentoAsync(EsitoTavolo esito, int restoCent = 0)
        {
            if (_sessione == null) return;
            if (restoCent > 0) await MessaggioBox.Info("💶 RESTO", $"Resto da dare:\n\n€ {restoCent / 100m:N2}");
            if (esito != null) await MostraAvvisiAsync(esito);
            await TornaAllaSalaAsync();
        }

        /// <summary>
        /// Uscita dal tavolo senza "scatti": la lista resta ferma com'era (fotografia), si apre subito la sala
        /// e solo dopo, a schermata coperta, la vendita torna al banco. Prima si vedevano sparire le righe una per una.
        /// </summary>
        private async Task TornaAllaSalaAsync()
        {
            var tavolo = _sessione;
            CongelaLista();
            try { await Navigazione.Apri(this, () => new TavoliPage()); }
            finally
            {
                // se nel frattempo è già stato aperto un altro tavolo non si tocca
                if (ReferenceEquals(_sessione, tavolo)) CollegaCarrello(null);
            }
        }

        private void CongelaLista()
        {
            if (ListaRighe.ItemsSource is not List<RigaScontrino>) ListaRighe.ItemsSource = Carrello.Righe.ToList();
        }

        private void ScongelaLista() => ListaRighe.ItemsSource = Carrello.Righe;

        private static async Task MostraAvvisiAsync(EsitoTavolo esito)
        {
            if (esito.Avvisi.Count > 0)
                await MessaggioBox.Avviso("⚠️ COMANDE", string.Join("\n\n", esito.Avvisi) + "\n\nImposta le stampanti in IMPOSTAZIONI → SALE.");
        }

        // ══════════════ salvataggio continuo ══════════════

        /// <summary>Ogni modifica al tavolo viene salvata dopo un attimo (nessuna stampa).</summary>
        private void ProgrammaBozza()
        {
            if (_sessione == null) return;
            if (_timerBozza == null)
            {
                _timerBozza = Dispatcher.CreateTimer();
                _timerBozza.Interval = TimeSpan.FromMilliseconds(700);
                _timerBozza.IsRepeating = false;
                _timerBozza.Tick += (s, e) =>
                {
                    try { _sessione?.SalvaBozza(); AvvisoSalvataggio.IsVisible = false; }
                    catch (Exception ex)
                    {
                        PosTouch.Core.Diagnostica.LogManager.Error($"Salvataggio tavolo: {ex.Message}");
                        AvvisoSalvataggio.IsVisible = true;   // il cameriere lo vede: resta finché il salvataggio dopo non va
                    }
                };
            }
            _timerBozza.Stop();
            _timerBozza.Start();
        }

        /// <summary>App in background (o chiusa da Android): il salvataggio in attesa (700 ms) si fa subito.</summary>
        public void SalvaBozzaSubito()
        {
            if (_sessione == null || _timerBozza == null || !_timerBozza.IsRunning) return;
            _timerBozza.Stop();
            try { _sessione.SalvaBozza(); AvvisoSalvataggio.IsVisible = false; }
            catch (Exception ex)
            {
                PosTouch.Core.Diagnostica.LogManager.Error($"Salvataggio tavolo (app in background): {ex.Message}");
                AvvisoSalvataggio.IsVisible = true;
            }
        }
    }
}
