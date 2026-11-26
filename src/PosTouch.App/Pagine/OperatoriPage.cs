using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// 👥 GESTIONE OPERATORI (solo amministratore): elenco, nuovo, scheda con PIN e permessi,
    /// turno attivo con possibilità di chiuderlo.
    /// </summary>
    public class OperatoriPage : PaginaPos
    {
        private readonly VerticalStackLayout _lista = new VerticalStackLayout { Spacing = 8 };
        private readonly Operatore _amministratore;

        /// <param name="amministratore">Chi è entrato (ha già dato il suo PIN): può chiudere qualsiasi turno.</param>
        public OperatoriPage(Operatore amministratore)
        {
            _amministratore = amministratore;
            BackgroundColor = Ui.C("#F5F5F5");
            var testa = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 10, BackgroundColor = Ui.C("#4527A0"), Padding = new Thickness(16, 8) };
            testa.Add(new Label { Text = "👥 OPERATORI", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center }, 0, 0);
            var nuovo = Ui.Tasto("➕ NUOVO", "#43A047", fontSize: 15, altezza: 46, clic: async (s, e) => { if (await OperatoreBox.Modifica(null)) Carica(); });
            var chiudi = Ui.Tasto("CHIUDI", "#78909C", fontSize: 15, altezza: 46, clic: async (s, e) => await Navigazione.Chiudi(this));
            nuovo.WidthRequest = 130; chiudi.WidthRequest = 120;
            testa.Add(nuovo, 1, 0); testa.Add(chiudi, 2, 0);

            var radice = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            radice.Add(testa, 0, 0);
            radice.Add(new ScrollView { Content = _lista, Padding = new Thickness(14) }, 0, 1);
            Content = radice;
            Carica();
        }

        private void Carica()
        {
            _lista.Children.Clear();
            var turno = ContestoCassa.Operatori.TurnoAttivo();
            if (turno != null)
            {
                var riga = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10, Padding = new Thickness(14, 10) };
                riga.Add(Ui.Testo($"🟢 Turno {(turno.Stato == StatoTurno.Sospeso ? "SOSPESO" : "APERTO")}: {turno.OperatoreNome} dalle {turno.Apertura:HH:mm} del {turno.Apertura:dd/MM}", 17, "#1B5E20", true), 0, 0);
                var btn = Ui.Tasto("🔒 CHIUDI TURNO", "#E65100", fontSize: 14, altezza: 46, clic: async (s, e) =>
                {
                    // non si chiude un turno mentre la vendita ha righe battute o un tavolo aperto
                    if (ContestoCassa.Operatori.TurnoCorrente?.Id == turno.Id && !ContestoCassa.Carrello.Vuoto)
                    { await MessaggioBox.Avviso("SCONTRINO IN CORSO", "Chiudi o svuota lo scontrino prima di chiudere il turno."); return; }
                    if (await Turni.ChiudiAsync(turno, _amministratore)) Carica();
                });
                btn.WidthRequest = 180;
                riga.Add(btn, 1, 0);
                _lista.Add(Ui.Riquadro(riga, "#E8F5E9", 8));
            }
            foreach (var o in ContestoCassa.Operatori.Archivio.Operatori())
            {
                string ruolo = o.Amministratore ? "👑 Amministratore" : o.Permessi.Count == 0 ? "Solo vendita" : $"{o.Permessi.Count} permessi";
                var testo = new VerticalStackLayout { Padding = new Thickness(14, 10) };
                testo.Add(Ui.Testo($"👤 {o.Nome}{(o.Attivo ? "" : "  (non attivo)")}", 19, o.Attivo ? "#263238" : "#9E9E9E", true));
                testo.Add(Ui.Testo(ruolo, 13, "#607D8B"));
                var card = Ui.Riquadro(testo, "#FFFFFF", 8, null, "#E0E0E0", 1);
                var op = o;
                Ui.Tocco(card, async () => { if (await OperatoreBox.Modifica(op)) Carica(); });
                _lista.Add(card);
            }
        }

        protected override bool Indietro() { _ = Navigazione.Chiudi(this); return true; }
    }
}
