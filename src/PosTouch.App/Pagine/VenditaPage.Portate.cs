using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    public partial class VenditaPage
    {
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

        // ══════════════ CASELLE PORTATA (sotto lo scontrino) ══════════════
        // INIZIO, SEGUE, SEGUE 2… come i separatori delle impostazioni. La casella accesa decide dove vanno
        // i prossimi articoli; ognuno finisce da solo sotto la sua portata (prima: tasto 📑 FUNZIONI + elenco).

        private readonly List<Button> _casellePortata = new();
        private string _nomiCaselle;
        private const int MaxCaselleLarghe = 5;   // oltre: caselle a larghezza fissa che scorrono col dito

        private void AggiornaBarraPortate()
        {
            if (BarraPortate == null) return;
            var sale = Cfg?.Sale;
            var separatori = sale?.SeparatoriPortate;
            bool mostra = _sessione != null && sale != null && sale.AbilitaPortate && sale.AbilitaComande && separatori != null && separatori.Count > 0;
            BarraPortate.IsVisible = mostra;
            if (!mostra) return;
            string nomi = string.Join("|", separatori);
            if (nomi != _nomiCaselle) CostruisciCaselle(separatori, nomi);
            int attiva = Carrello.PortataAttiva;
            for (int i = 0; i < _casellePortata.Count; i++)
            {
                bool accesa = i == attiva;
                var b = _casellePortata[i];
                var sfondo = accesa ? Ui.C("#1565C0") : Ui.C("#FFFFFF");
                if (b.BackgroundColor != sfondo)
                {
                    b.BackgroundColor = sfondo;
                    b.TextColor = accesa ? Colors.White : Ui.C("#37474F");
                    b.BorderColor = accesa ? Ui.C("#0D47A1") : Ui.C("#B0BEC5");
                }
            }
        }

        private void CostruisciCaselle(IList<string> separatori, string nomi)
        {
            _nomiCaselle = nomi;
            _casellePortata.Clear();
            var testi = new List<string> { "⚡ INIZIO" };
            testi.AddRange(separatori.Select(x => x.Trim().ToUpperInvariant()));
            bool larghe = testi.Count <= MaxCaselleLarghe;
            var griglia = new Grid { ColumnSpacing = 3 };
            var fila = new HorizontalStackLayout { Spacing = 3 };
            for (int i = 0; i < testi.Count; i++)
            {
                int indice = i;
                var b = new Button
                {
                    Text = testi[i], FontSize = 13, FontAttributes = FontAttributes.Bold, HeightRequest = 40, Padding = new Thickness(4, 0),
                    CornerRadius = 6, BorderWidth = 1, BackgroundColor = Ui.C("#FFFFFF"), TextColor = Ui.C("#37474F"), BorderColor = Ui.C("#B0BEC5"),
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                b.Clicked += (s, e) => OnCasellaPortata(indice);
                _casellePortata.Add(b);
                if (larghe)
                {
                    griglia.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    griglia.Add(b, i, 0);
                }
                else { b.WidthRequest = 104; fila.Add(b); }
            }
            BarraPortate.Content = larghe ? griglia
                : new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = fila };
        }

        private void OnCasellaPortata(int indice)
        {
            if (CombinazioneAperta() || _sessione == null || Attesa.IsVisible || _venditaAvviata) return;
            var separatori = Cfg.Sale.SeparatoriPortate;
            if (separatori == null || indice > separatori.Count) return;
            try
            {
                Carrello.ScegliPortata(indice == 0 ? Portate.NomeSubito : separatori[indice - 1]);
                AggiornaBarraPortate();
                // si porta in vista la sezione scelta (con la lista espansa si vede la comanda intera per portate)
                var sep = Carrello.Righe.LastOrDefault(r => r.Tipo == TipoRiga.Portata &&
                             (indice == 0 ? Portate.IsSubito(r) : Portate.Nome(r).Equals(separatori[indice - 1].Trim(), StringComparison.OrdinalIgnoreCase)));
                if (sep != null) ListaRighe.ScrollTo(sep, position: ScrollToPosition.Start, animate: false);
            }
            catch (Exception ex) { LogManager.Error($"Scelta portata: {ex}"); }
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
    }
}
