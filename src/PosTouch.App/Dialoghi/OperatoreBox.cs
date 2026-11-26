using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Scheda operatore: nome, PIN (vuoto = invariato), amministratore, attivo, permessi.
    /// Salva nell'archivio: true = salvato o eliminato, false = annullato. Gli errori (PIN doppio,
    /// ultimo amministratore...) si mostrano senza chiudere la scheda.
    /// </summary>
    public class OperatoreBox : DialogoBase<bool>
    {
        private readonly Operatore _o;
        private readonly Entry _nome;
        private readonly Label _pinTesto;
        private string _pinNuovo;
        private readonly CheckBox _admin, _attivo;
        private readonly Dictionary<Permesso, CheckBox> _permessi = new();
        private readonly VerticalStackLayout _boxPermessi = new VerticalStackLayout { Spacing = 4 };
        private bool _salvataggio;

        public OperatoreBox(Operatore originale)
        {
            _o = originale?.Copia() ?? new Operatore();
            bool nuovo = _o.Id == 0;
            _nome = new Entry { Text = _o.Nome, FontSize = 20, MaxLength = 20, BackgroundColor = Ui.C("#FAFAFA"), Keyboard = Keyboard.Text };
            _pinTesto = new Label { FontSize = 18, VerticalOptions = LayoutOptions.Center, TextColor = Ui.C("#263238") };
            AggiornaPin(nuovo);
            var pin = Ui.Riquadro(_pinTesto, "#FAFAFA", 8, new Thickness(12, 10), "#B0BEC5", 1);
            Ui.Tocco(pin, () => _ = ChiediPinAsync());

            _admin = new CheckBox { IsChecked = _o.Amministratore, Color = Ui.C("#6A1B9A") };
            _attivo = new CheckBox { IsChecked = _o.Attivo, Color = Ui.C("#2E7D32") };
            _admin.CheckedChanged += (s, e) => _boxPermessi.IsEnabled = !_admin.IsChecked;
            foreach (var p in Permessi.Tutti)
            {
                var chk = new CheckBox { IsChecked = _o.Permessi.Contains(p), Color = Ui.C("#00897B") };
                _permessi[p] = chk;
                _boxPermessi.Add(Spunta(chk, Permessi.Nome(p), null, () => !_admin.IsChecked));
            }
            _boxPermessi.IsEnabled = !_admin.IsChecked;

            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 55, clic: (s, e) => Chiudi(false)), 0, 0);
            azioni.Add(Ui.Tasto("💾 SALVA", "#4CAF50", altezza: 55, clic: async (s, e) => await SalvaAsync()), 1, 0);

            var corpo = new VerticalStackLayout
            {
                Spacing = 10, Padding = new Thickness(24),
                Children =
                {
                    new Label { Text = nuovo ? "➕ NUOVO OPERATORE" : $"👤 {_o.Nome}", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center },
                    Ui.Testo("Nome", 14, "#607D8B"),
                    Ui.Riquadro(_nome, "#FAFAFA", 8, new Thickness(10, 4), "#B0BEC5", 1),
                    Ui.Testo("PIN (da 4 a 8 cifre, identifica l'operatore)", 14, "#607D8B"),
                    pin,
                    Spunta(_admin, "👑 Amministratore", "Può tutto e gestisce gli operatori"),
                    Spunta(_attivo, "✅ Attivo", "Un operatore non attivo non può aprire turni"),
                    Ui.Testo("PERMESSI", 14, "#607D8B", true),
                    _boxPermessi
                }
            };
            if (!nuovo)
                corpo.Add(Ui.Tasto("🗑 ELIMINA OPERATORE", "#B71C1C", altezza: 50, clic: async (s, e) => await EliminaAsync()));
            corpo.Add(azioni);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.White, StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0,
                Content = new ScrollView { Content = corpo }
            };
            Content = riquadro;
            Dimensiona(riquadro, 620, 720);
        }

        private void AggiornaPin(bool nuovo) =>
            _pinTesto.Text = _pinNuovo != null ? "●●●● (nuovo, tocca per cambiare)" : nuovo ? "Tocca per impostare il PIN" : "●●●● (tocca per cambiare)";

        private async Task ChiediPinAsync()
        {
            string p1 = await TastierinoPrezzo.ChiediCodice("🔑 NUOVO PIN (4-8 cifre)", nascondi: true);
            if (p1 == null) return;
            try { PosTouch.Core.Dati.ArchivioOperatori.ControllaFormatoPin(p1); }
            catch (ArgumentException ex) { await MessaggioBox.Avviso("PIN", ex.Message); return; }
            if (!Accesso.PinDisponibile(p1)) { await MessaggioBox.Avviso("PIN", "PIN non disponibile: scegline un altro."); return; }
            string p2 = await TastierinoPrezzo.ChiediCodice("🔑 RIPETI IL PIN", nascondi: true);
            if (p2 == null) return;
            if (p1 != p2) { await MessaggioBox.Avviso("PIN", "I due PIN non coincidono."); return; }
            _pinNuovo = p1;
            AggiornaPin(_o.Id == 0);
        }

        /// <param name="abilitata">null = sempre; i permessi non si toccano per un amministratore (li ha tutti).</param>
        private static View Spunta(CheckBox chk, string titolo, string spiegazione, Func<bool> abilitata = null)
        {
            var testo = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
            testo.Add(new Label { Text = titolo, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#263238") });
            if (spiegazione != null) testo.Add(new Label { Text = spiegazione, FontSize = 12, TextColor = Ui.C("#607D8B") });
            var riga = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 6, MinimumHeightRequest = 48 };
            riga.Add(chk, 0, 0);
            riga.Add(testo, 1, 0);
            var tocco = new TapGestureRecognizer();
            tocco.Tapped += (s, e) => { if (abilitata?.Invoke() != false) chk.IsChecked = !chk.IsChecked; };
            testo.GestureRecognizers.Add(tocco);
            return riga;
        }

        private async Task SalvaAsync()
        {
            if (_salvataggio) return;
            _salvataggio = true;
            try
            {
                _o.Nome = (_nome.Text ?? "").Trim();
                _o.Amministratore = _admin.IsChecked;
                _o.Attivo = _attivo.IsChecked;
                _o.Permessi = new HashSet<Permesso>(_permessi.Where(x => x.Value.IsChecked).Select(x => x.Key));
                _nome.Unfocus();
                string pin = _pinNuovo;
                await Task.Run(() => ContestoCassa.Operatori.Archivio.Salva(_o, pin));   // hash del PIN fuori dal thread grafico
                ContestoCassa.Operatori.Aggiorna();
                Chiudi(true);
            }
            catch (ArgumentException ex) { await MessaggioBox.Avviso("OPERATORE", ex.Message); }
            catch (Exception ex) { await MessaggioBox.Errore("OPERATORE", ex.Message); }
            finally { _salvataggio = false; }
        }

        private async Task EliminaAsync()
        {
            if (!await MessaggioBox.Conferma("ELIMINA OPERATORE", $"Eliminare {_o.Nome}?\n\nI turni già chiusi restano nello storico.")) return;
            try
            {
                ContestoCassa.Operatori.Archivio.Elimina(_o.Id);
                ContestoCassa.Operatori.Aggiorna();
                Chiudi(true);
            }
            catch (ArgumentException ex) { await MessaggioBox.Avviso("OPERATORE", ex.Message); }
        }

        public override void Indietro() { _nome.Unfocus(); Chiudi(false); }

        public static Task<bool> Modifica(Operatore o) => Dialogo.Mostra(new OperatoreBox(o));
    }
}
