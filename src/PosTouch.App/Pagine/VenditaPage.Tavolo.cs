using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// VenditaPage — modalità TAVOLO (VenditaContoClienteDialog del PC): stessa schermata della vendita al banco
    /// con lo scontrino del tavolo, barra CASSA / CONTO / PRECONTO / NO STAMPA / ANNULLA / FUNZIONI / AVANZA
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
            BtnEsci.Text = tavolo ? "⬅" : "🚪";
            TitoloScontrino.Text = tavolo ? $"🍽️ {_sessione.Titolo.ToUpperInvariant()}" : "📋 SCONTRINO";
            BtnContoTavolo.IsVisible = BtnPreconto.IsVisible = Cfg.AbilitaStampanteTermica;
            BtnFunzioniPortate.IsVisible = BtnAvanza.IsVisible = tavolo && sale.AbilitaPortate && sale.AbilitaComande;
            bool inSala = tavolo && !_sessione.IsContoCliente;   // come il PC: sposta/unisci solo per i tavoli delle sale
            BtnSposta.IsVisible = inSala && sale.AbilitaSpostaTavolo;
            BtnUnisci.IsVisible = inSala && sale.AbilitaUnisciConti;
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
        private void OnNoStampa(object sender, EventArgs e) => _ = EsciDalTavoloAsync(s => s.EsciNoStampa());
        private void OnAnnullaTavolo(object sender, EventArgs e) => _ = EsciDalTavoloAsync(s => s.EsciSenzaInvio());

        private bool _uscitaInCorso;

        private async Task EsciDalTavoloAsync(Func<SessioneTavolo, EsitoTavolo> uscita)
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null || _uscitaInCorso || Attesa.IsVisible || _venditaAvviata) return;
            _uscitaInCorso = true;
            try
            {
                _timerBozza?.Stop();
                EsitoTavolo esito;
                try { esito = uscita(_sessione); }
                catch (Exception ex)
                {
                    await MessaggioBox.Errore("ERRORE SALVATAGGIO TAVOLO", $"Il tavolo NON è stato salvato:\n{ex.Message}\n\nRiprova.");
                    return;
                }
                CollegaCarrello(null);
                await MostraAvvisiAsync(esito);
                await Navigazione.Apri(this, () => new TavoliPage());
            }
            finally { _uscitaInCorso = false; }
        }

        /// <summary>Dopo scontrino o conto: il tavolo si libera e si torna alla sala.</summary>
        private async Task DopoPagamentoAsync(int restoCent = 0)
        {
            if (_sessione == null) return;
            // al tavolo si torna subito alla sala: il resto lo mostro prima di uscire
            if (restoCent > 0) await MessaggioBox.Info("💶 RESTO", $"Resto da dare:\n\n€ {restoCent / 100m:N2}");
            _timerBozza?.Stop();
            EsitoTavolo esito = null;
            try { esito = _sessione.ChiudiPagato(); }
            catch (Exception ex) { await MessaggioBox.Errore("TAVOLO", $"Tavolo pagato ma non liberato:\n{ex.Message}"); }
            CollegaCarrello(null);
            if (esito != null) await MostraAvvisiAsync(esito);
            await Navigazione.Apri(this, () => new TavoliPage());
        }

        private static async Task MostraAvvisiAsync(EsitoTavolo esito)
        {
            if (esito.Avvisi.Count > 0)
                await MessaggioBox.Avviso("⚠️ COMANDE", string.Join("\n\n", esito.Avvisi) + "\n\nImposta le stampanti in IMPOSTAZIONI → SALE.");
        }

        // ══════════════ PRECONTO / PORTATE ══════════════

        private async void OnPreconto(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null || Attesa.IsVisible || _venditaAvviata) return;
            MostraAttesa(true, "⏳ Stampa preconto...");
            string errore;
            try { errore = await _sessione.PrecontoAsync(); }   // sul thread principale: legge il conto e salva in sicurezza (la stampa è già asincrona)
            finally { MostraAttesa(false); }
            if (errore != null) await MessaggioBox.Errore("PRECONTO", errore);
        }

        /// <summary>📑 FUNZIONI: scelta del separatore (⚡ SUBITO, SEGUE, SEGUE 2...) dove vanno i prossimi articoli.</summary>
        private async void OnFunzioniPortate(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null) return;
            var separatori = Cfg.Sale.SeparatoriPortate;
            if (separatori == null || separatori.Count == 0)
            {
                await MessaggioBox.Avviso("NESSUN SEPARATORE", "Configura i separatori portate nelle impostazioni.");
                return;
            }
            var presenti = new HashSet<string>(Carrello.Righe.Where(r => r.Tipo == TipoRiga.Portata)
                                                             .Select(r => Portate.Nome(r).ToUpperInvariant()));
            var voci = new List<string> { presenti.Contains(Portate.NomeSubito) ? "⚡ SUBITO (vai a inizio)" : "⚡ SUBITO (aggiungi in testa)" };
            voci.AddRange(separatori.Select(s => presenti.Contains(s.Trim().ToUpperInvariant()) ? $"📌 {s} (vai a sezione)" : $"➕ {s}"));
            int i = await SceltaBox.Testi("📋 SCEGLI SEPARATORE", voci, 1);
            if (i < 0) return;
            Carrello.ScegliPortata(i == 0 ? Portate.NomeSubito : separatori[i - 1]);
            var sep = Carrello.Righe.LastOrDefault(r => r.Tipo == TipoRiga.Portata &&
                         (i == 0 ? Portate.IsSubito(r) : Portate.Nome(r).Equals(separatori[i - 1].Trim(), StringComparison.OrdinalIgnoreCase)));
            if (sep != null) ListaRighe.ScrollTo(sep, position: ScrollToPosition.Start, animate: false);
        }

        /// <summary>⏩ AVANZA: VAI con la prossima portata.</summary>
        private bool _avanzaInCorso;

        private async void OnAvanza(object sender, EventArgs e)
        {
            if (_avanzaInCorso) return;   // doppio tocco: una portata sola
            _avanzaInCorso = true;
            try { await AvanzaAsync(); }
            finally { _avanzaInCorso = false; }
        }

        private async Task AvanzaAsync()
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null || Attesa.IsVisible) return;
            var a = _sessione.PreparaAvanza();
            if (!a.Possibile)
            {
                await MessaggioBox.Avviso(a.Titolo, a.Messaggio);
                return;
            }
            if (!a.SenzaStampa && !await MessaggioBox.Domanda(a.Titolo, a.Messaggio)) return;
            try
            {
                var esito = _sessione.EseguiAvanza(a);
                await MostraAvvisiAsync(esito);
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("AVANZA", ex.Message);
            }
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
                    try { _sessione?.SalvaBozza(); }
                    catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Error($"Salvataggio tavolo: {ex.Message}"); }
                };
            }
            _timerBozza.Stop();
            _timerBozza.Start();
        }
    }
}
