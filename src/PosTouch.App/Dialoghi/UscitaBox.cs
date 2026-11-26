using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Scheda di un'uscita (OPZIONI DI STAMPA): nome ed esclusioni sulle comande (Escludi altre / Escludi in altre).
    /// Lavora su una copia: null = annullato, l'uscita originale non cambia.
    /// </summary>
    public class UscitaBox : DialogoBase<Uscita>
    {
        private readonly Uscita _u;
        private readonly Entry _nome;
        private readonly CheckBox _escludiAltre, _escludiInAltre;

        public UscitaBox(Uscita originale)
        {
            _u = originale.Copia();
            _nome = new Entry { Text = _u.Nome, FontSize = 22, MaxLength = 20, BackgroundColor = Ui.C("#FAFAFA"), Keyboard = Keyboard.Text };
            _escludiAltre = new CheckBox { IsChecked = _u.EscludiAltre, Color = Ui.C("#00897B") };
            _escludiInAltre = new CheckBox { IsChecked = _u.EscludiInAltre, Color = Ui.C("#00897B") };

            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 55, clic: (s, e) => Indietro()), 0, 0);
            azioni.Add(Ui.Tasto("✓ OK", "#4CAF50", altezza: 55, clic: (s, e) => Conferma()), 1, 0);

            var corpo = new VerticalStackLayout
            {
                Spacing = 14, Padding = new Thickness(25),
                Children =
                {
                    new Label { Text = $"USCITA {_u.Numero}", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = "Nome", FontSize = 14, TextColor = Ui.C("#607D8B") },
                    Ui.Riquadro(_nome, "#FAFAFA", 8, new Thickness(10, 4), "#B0BEC5", 1),
                    Spunta(_escludiAltre, "🚫 Escludi altre", "Sulla comanda di questa uscita non compaiono gli articoli degli altri reparti."),
                    Spunta(_escludiInAltre, "🚫 Escludi in altre", "Gli articoli di questa uscita non compaiono sulle comande degli altri reparti."),
                    new Label { Text = "Valgono per tutte le sale.", FontSize = 12, TextColor = Ui.C("#90A4AE") },
                    azioni
                }
            };
            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 30, 0, 0),
                BackgroundColor = Colors.White, StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0,
                Content = new ScrollView { Content = corpo }
            };
            Content = riquadro;
            Dimensiona(riquadro, 560);
        }

        /// <summary>Riga con spunta e testo: tutta la riga è toccabile (la spunta da sola è piccola sul tablet).</summary>
        private static View Spunta(CheckBox chk, string titolo, string spiegazione)
        {
            var testo = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 2 };
            testo.Add(new Label { Text = titolo, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#263238") });
            testo.Add(new Label { Text = spiegazione, FontSize = 13, TextColor = Ui.C("#607D8B") });
            var riga = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8, Padding = new Thickness(4, 6), MinimumHeightRequest = 56 };
            riga.Add(chk, 0, 0);
            riga.Add(testo, 1, 0);
            var tocco = new TapGestureRecognizer();
            tocco.Tapped += (s, e) => chk.IsChecked = !chk.IsChecked;
            testo.GestureRecognizers.Add(tocco);
            return Ui.Riquadro(riga, "#F5F5F5", 8);
        }

        private void Conferma()
        {
            string nome = (_nome.Text ?? "").Trim().ToUpperInvariant();
            if (nome.Length == 0) { _nome.Focus(); return; }   // il nome serve: compare in testa alla comanda
            _nome.Unfocus();
            _u.Nome = nome;
            _u.EscludiAltre = _escludiAltre.IsChecked;
            _u.EscludiInAltre = _escludiInAltre.IsChecked;
            Chiudi(_u);
        }

        public override void Indietro() { _nome.Unfocus(); Chiudi(null); }

        public static Task<Uscita> Modifica(Uscita u) => Dialogo.Mostra(new UscitaBox(u));
    }
}
