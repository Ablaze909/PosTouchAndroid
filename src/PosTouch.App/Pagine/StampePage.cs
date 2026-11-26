using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// 🖨️ OPZIONI DI STAMPA come POSBY: PRECONTI e RIEPILOGHI, uscite di produzione (solo quelle aggiunte, max 8),
    /// ognuna con il suo "Configura"; impostazioni per sala (Banco, sale, conti) con copia/incolla sala;
    /// comande da banco; elenco IP delle stampanti. Si lavora su una copia, 💾 SALVA la rende effettiva.
    /// </summary>
    public class StampePage : PaginaPos
    {
        private readonly ConfigStampe _c;
        private readonly VerticalStackLayout _sinistra = new VerticalStackLayout { Spacing = 6 };
        private readonly VerticalStackLayout _destra = new VerticalStackLayout { Spacing = 10 };
        private readonly Label _titolo = new Label { FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
        private int _sala = ConfigStampe.SalaBanco;
        private static StampeSala _salaCopiata;
        private readonly string _firma;

        private ConfigSale Sale => ContestoCassa.Impostazioni.Sale;

        public StampePage()
        {
            _c = (ContestoCassa.Impostazioni.Stampe ?? new ConfigStampe()).Copia();
            _firma = Firma();
            BackgroundColor = Ui.C("#263238");

            var chiudi = Ui.Tasto("✖ CHIUDI", "#C2185B", fontSize: 15, altezza: 46, clic: async (s, e) => await ChiudiAsync());
            var salva = Ui.Tasto("💾 SALVA", "#2E7D32", fontSize: 15, altezza: 46, clic: async (s, e) => await SalvaAsync());
            chiudi.WidthRequest = 130; salva.WidthRequest = 130;
            var testa = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 10, BackgroundColor = Ui.C("#37474F"), Padding = new Thickness(16, 8) };
            testa.Add(_titolo, 0, 0); testa.Add(chiudi, 1, 0); testa.Add(salva, 2, 0);

            var corpo = new Grid { ColumnDefinitions = { new(new GridLength(1.25, GridUnitType.Star)), new(GridLength.Star) }, ColumnSpacing = 16, Padding = new Thickness(12) };
            corpo.Add(new ScrollView { Content = _sinistra }, 0, 0);
            corpo.Add(new ScrollView { Content = _destra }, 1, 0);

            var radice = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            radice.Add(testa, 0, 0);
            radice.Add(corpo, 0, 1);
            Content = radice;
            Disegna();
        }

        protected override bool Indietro() { _ = ChiudiAsync(); return true; }

        // ══════════════ sale ══════════════

        /// <summary>Pagine per sala: Banco, le sale, i conti clienti (come il menu Banco / Sala 1 / Sala 2 / Asporto di POSBY).</summary>
        private List<(int id, string nome)> SaleDisponibili()
        {
            var l = new List<(int, string)> { (ConfigStampe.SalaBanco, "Banco") };
            l.AddRange(Sale.Sale.Select(s => (s.Id, s.Nome)));
            if (Sale.AbilitaContiClienti) l.Add((ConfigSale.SalaConti, "Conti clienti"));
            return l;
        }

        private string NomeSala(int id) => SaleDisponibili().FirstOrDefault(x => x.id == id).nome ?? $"Sala {id}";

        /// <summary>Pagina in modifica: quella della sala scelta (per sala attivo) o la base.</summary>
        private StampeSala Pagina => _c.ImpostazioniPerSala ? _c.PaginaDi(_sala) : _c.Base;

        // ══════════════ disegno ══════════════

        private void Disegna()
        {
            _titolo.Text = _c.ImpostazioniPerSala ? $"🖨️ Opzioni di Stampa  >> {NomeSala(_sala)} <<" : "🖨️ Opzioni di Stampa";
            DisegnaSinistra();
            DisegnaDestra();
        }

        private void DisegnaSinistra()
        {
            _sinistra.Children.Clear();
            var p = Pagina;
            _sinistra.Add(Riga("Preconti", "Conti e preconti (anche il ticket eliminacode)", p.Preconto, "#FFEB3B", v => Pagina.Preconto = v));
            _sinistra.Add(Riga("Riepiloghi", "Riepiloghi statistici", p.Riepiloghi, "#FFEB3B", v => Pagina.Riepiloghi = v));

            _sinistra.Add(new Label { Text = "USCITE DI PRODUZIONE (comande)", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#80CBC4"), Margin = new Thickness(2, 14, 0, 2) });
            if (_c.Uscite.Count == 0)
                _sinistra.Add(Ui.Testo("Nessuna uscita. Premi ➕ AGGIUNGI USCITA (es. BAR, CUCINA, PIZZERIA).", 14, "#B0BEC5"));
            foreach (var u in _c.Uscite)
            {
                int numero = u.Numero;
                _sinistra.Add(Riga($"{u.Numero}  {u.Nome}", null, p.Uscita(numero), "#FFFFFF", v => Pagina.Uscite[numero] = v, u));
            }
            if (_c.Uscite.Count < ConfigStampe.MaxUscite)
            {
                var agg = Ui.Tasto("➕ AGGIUNGI USCITA", "#00897B", fontSize: 16, altezza: 54, clic: async (s, e) => await AggiungiUscitaAsync());
                agg.Margin = new Thickness(0, 6, 0, 0);
                _sinistra.Add(agg);
            }
        }

        /// <summary>Riga "nome + stampante + Configura" come POSBY; per le uscite anche ✏️ nome e 🗑.</summary>
        private View Riga(string nome, string nota, ParametriStampa par, string coloreNome, Action<ParametriStampa> salva, Uscita uscita = null)
        {
            var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(new GridLength(150)) }, ColumnSpacing = 6 };
            var testo = new VerticalStackLayout { Padding = new Thickness(10, 6), VerticalOptions = LayoutOptions.Center };
            testo.Add(new Label { Text = nome, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Ui.C(coloreNome) });
            testo.Add(new Label
            {
                Text = par.Assegnata ? par.Descrizione : "— nessuna stampante —", FontSize = 14, FontAttributes = FontAttributes.Bold,
                TextColor = Ui.C(par.Assegnata ? "#FFFFFF" : "#90A4AE"), HorizontalOptions = LayoutOptions.End
            });
            if (nota != null) testo.Add(new Label { Text = nota, FontSize = 11, TextColor = Ui.C("#90A4AE") });
            g.Add(Ui.Riquadro(testo, "#37474F", 4), 0, 0);

            if (uscita != null)
            {
                var azioni = new HorizontalStackLayout { Spacing = 4 };
                var rinomina = Ui.Tasto("✏️", "#455A64", fontSize: 18, altezza: 60, clic: async (s, e) => await RinominaAsync(uscita)); rinomina.WidthRequest = 52;
                var elimina = Ui.Tasto("🗑", "#B71C1C", fontSize: 18, altezza: 60, clic: async (s, e) => await EliminaUscitaAsync(uscita)); elimina.WidthRequest = 52;
                azioni.Add(rinomina); azioni.Add(elimina);
                g.Add(azioni, 1, 0);
            }
            var configura = Ui.Tasto("Configura", "#BDBDBD", "#000000", 20, 2, async (s, e) =>
            {
                var nuovi = await ConfiguraStampaBox.Modifica(nome.Trim(), par, () => _c.IndirizziIP.ToList(), async ip => { _c.IndirizziIP = ip; await Task.CompletedTask; });
                if (nuovi == null) return;
                salva(nuovi);
                Disegna();
            }, 64);
            g.Add(configura, 2, 0);
            return g;
        }

        private void DisegnaDestra()
        {
            _destra.Children.Clear();
            _destra.Add(Interruttore("Impostazioni per sala", _c.ImpostazioniPerSala, v =>
            {
                _c.ImpostazioniPerSala = v;
                if (v) foreach (var (id, _) in SaleDisponibili()) _c.PaginaDi(id);   // ogni sala parte dalla configurazione attuale
                Disegna();
            }));
            if (_c.ImpostazioniPerSala)
            {
                var sale = SaleDisponibili();
                var scelta = new CampoScelta { Titolo = "Sala", Colonne = 2, FontSize = 20, Voci = sale.Select(x => x.nome).ToList(), Indice = Math.Max(0, sale.FindIndex(x => x.id == _sala)) };
                scelta.Cambiato += (s, e) => { _sala = sale[scelta.Indice].id; Disegna(); };
                _destra.Add(Ui.Riquadro(scelta, "#F5EEE6", 4, new Thickness(12, 10)));
                var copia = Ui.Tasto("Copia Sala", "#C000C0", fontSize: 18, altezza: 58, clic: async (s, e) =>
                {
                    _salaCopiata = Pagina.Copia();
                    await MessaggioBox.Info("COPIA SALA", $"Stampe di {NomeSala(_sala)} copiate.\nScegli un'altra sala e premi Incolla Sala.");
                });
                var incolla = Ui.Tasto("Incolla Sala", "#BF360C", fontSize: 18, altezza: 58, clic: async (s, e) =>
                {
                    if (_salaCopiata == null) { await MessaggioBox.Avviso("INCOLLA SALA", "Prima premi Copia Sala su un'altra sala."); return; }
                    if (!await MessaggioBox.Domanda("INCOLLA SALA", $"Sostituire tutte le stampe di {NomeSala(_sala)} con quelle copiate?")) return;
                    _c.PerSala[_sala] = _salaCopiata.Copia();
                    Disegna();
                });
                var riga = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
                riga.Add(copia, 0, 0); riga.Add(incolla, 1, 0);
                _destra.Add(riga);
            }

            _destra.Add(Interruttore("Comande da Banco", _c.ComandeDaBanco, v => { _c.ComandeDaBanco = v; Disegna(); }));
            if (_c.ComandeDaBanco)
            {
                _destra.Add(Ui.Testo("Chiudendo scontrino o conto al banco gli articoli con un'uscita vanno in comanda con un numero eliminacode " +
                                     "(stampato in appendice allo scontrino e sul conto). Le stampanti sono quelle della pagina Banco.", 13, "#B0BEC5"));
                _destra.Add(Interruttore("Eliminacode su ticket (stampante Preconti)", _c.TicketEliminacodeSuTermica, v => { _c.TicketEliminacodeSuTermica = v; Disegna(); }));
                if (!Sale.AbilitaComande)
                    _destra.Add(Ui.Testo("⚠️ Le comande sono spente in SALE E COMANDE → OPZIONI.", 13, "#FFB74D"));
            }

            var ip = Ui.Tasto($"Gestione porte TCP/IP  ({_c.IndirizziIP.Count})", "#455A64", "#ECEFF1", 17, 4, async (s, e) =>
            {
                var nuovo = await GestioneIPBox.Modifica(_c.IndirizziIP);
                if (nuovo == null) return;
                _c.IndirizziIP = nuovo;
                Disegna();
            }, 58);
            ip.Margin = new Thickness(0, 20, 0, 0);
            _destra.Add(ip);
            _destra.Add(Ui.Testo("Lo scontrino fiscale esce sempre dal registratore impostato in CONNESSIONI.", 12, "#78909C"));
        }

        private static View Interruttore(string testo, bool valore, Action<bool> cambia)
        {
            var sw = new Switch { IsToggled = valore, OnColor = Ui.C("#26A69A"), VerticalOptions = LayoutOptions.Center };
            sw.Toggled += (s, e) => cambia(e.Value);
            var l = new Label { Text = testo, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            Ui.Tocco(l, () => sw.IsToggled = !sw.IsToggled);
            var r = new HorizontalStackLayout { Spacing = 8 };
            r.Add(sw); r.Add(l);
            return r;
        }

        // ══════════════ uscite ══════════════

        private async Task AggiungiUscitaAsync()
        {
            int numero = Enumerable.Range(1, ConfigStampe.MaxUscite).FirstOrDefault(n => _c.UscitaDa(n) == null);
            if (numero == 0) return;
            string nome = await TestoBox.Chiedi($"NOME USCITA {numero} (es. BAR, CUCINA)");
            if (string.IsNullOrWhiteSpace(nome)) return;
            _c.Uscite.Add(new Uscita { Numero = numero, Nome = nome.Trim().ToUpperInvariant() });
            _c.Uscite = _c.Uscite.OrderBy(u => u.Numero).ToList();
            Disegna();
        }

        private async Task RinominaAsync(Uscita u)
        {
            string nome = await TestoBox.Chiedi($"NOME USCITA {u.Numero}", u.Nome);
            if (string.IsNullOrWhiteSpace(nome)) return;
            u.Nome = nome.Trim().ToUpperInvariant();
            Disegna();
        }

        private async Task EliminaUscitaAsync(Uscita u)
        {
            int usata = ContestoCassa.Catalogo.TutteCategorie.Count(c => c.CentroProduzioneId == u.Numero)
                      + ContestoCassa.Catalogo.TuttiProdotti.Count(p => p.CentroProduzioneId == u.Numero);
            string avviso = usata > 0 ? $"\n\n⚠️ È usata da {usata} tra categorie e articoli: non andranno più in comanda." : "";
            if (!await MessaggioBox.Conferma("ELIMINA USCITA", $"Eliminare l'uscita {u.Numero} {u.Nome}?{avviso}")) return;
            _c.Uscite.Remove(u);
            _c.Base.Uscite.Remove(u.Numero);
            foreach (var p in _c.PerSala.Values) p.Uscite.Remove(u.Numero);
            Disegna();
        }

        // ══════════════ salva / chiudi ══════════════

        private string Firma() => _c.Firma();

        private async Task SalvaAsync()
        {
            var n = ContestoCassa.Impostazioni.Copia();
            n.Stampe = _c.Copia();
            try
            {
                ContestoCassa.SalvaImpostazioni(n);
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex) { await MessaggioBox.Errore("SALVATAGGIO", ex.Message); }
        }

        private async Task ChiudiAsync()
        {
            if (Firma() != _firma && await MessaggioBox.Domanda("MODIFICHE NON SALVATE", "Salvare le opzioni di stampa prima di chiudere?"))
            {
                await SalvaAsync();
                return;
            }
            await Navigazione.Chiudi(this);
        }
    }
}
