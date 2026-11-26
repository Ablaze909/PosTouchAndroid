using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Scelta da una griglia di tasti (categoria di destinazione, colore, icona).
    /// Restituisce l'indice scelto, -1 se annullato.
    /// </summary>
    public class SceltaBox : DialogoBase<int>
    {
        private SceltaBox(string titolo, IList<string> voci, IList<string> colori, int colonne, double altezzaTasto, double fontSize)
        {
            var griglia = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (int c = 0; c < colonne; c++) griglia.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            int righe = (voci.Count + colonne - 1) / colonne;
            for (int r = 0; r < righe; r++) griglia.RowDefinitions.Add(new RowDefinition(new GridLength(altezzaTasto)));
            for (int i = 0; i < voci.Count; i++)
            {
                int indice = i;
                string sfondo = colori?[i] ?? "#F5F5F5";
                string testo = colori == null ? "#111111" : "#FFFFFF";
                var b = Ui.Tasto(voci[i], sfondo, testo, fontSize, 8, (s, e) => Chiudi(indice));
                if (colori == null) { b.BorderColor = Ui.C("#E0E0E0"); b.BorderWidth = 1; }
                griglia.Add(b, i % colonne, i / colonne);
            }

            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(25), RowSpacing = 15 };
            corpo.Add(new Label { Text = titolo, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center }, 0, 0);
            corpo.Add(new ScrollView { Content = griglia }, 0, 1);
            corpo.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 55, clic: (s, e) => Chiudi(-1)), 0, 2);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.White, StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, Math.Min(760, 110 * colonne + 80), Math.Min(720, 170 + righe * (altezzaTasto + 8)));
        }

        /// <summary>Tasto indietro di Android = ANNULLA (-1), non la prima voce.</summary>
        protected override bool OnBackButtonPressed()
        {
            Chiudi(-1);
            return true;
        }

        public static Task<int> Testi(string titolo, IList<string> voci, int colonne = 3)
            => Dialogo.Mostra(new SceltaBox(titolo, voci, null, colonne, 60, 16));

        public static Task<int> Colori(string titolo, IList<string> colori)
            => Dialogo.Mostra(new SceltaBox(titolo, colori.Select(_ => "").ToList(), colori, 6, 70, 14));

        public static Task<int> Icone(string titolo, IList<string> icone)
            => Dialogo.Mostra(new SceltaBox(titolo, icone, null, 8, 64, 28));

        /// <summary>Tavolozza colori tasti (stessi toni Material usati nei dialog del PC).</summary>
        public static readonly string[] Tavolozza =
        {
            "#F44336", "#E91E63", "#9C27B0", "#673AB7", "#3F51B5", "#2196F3",
            "#03A9F4", "#00BCD4", "#009688", "#4CAF50", "#8BC34A", "#CDDC39",
            "#FFEB3B", "#FFC107", "#FF9800", "#FF5722", "#795548", "#9E9E9E",
            "#607D8B", "#37474F", "#000000", "#FFFFFF", "#B71C1C", "#1B5E20"
        };

        public static readonly string[] IconeCategorie =
        {
            "", "☕", "🍕", "🍝", "🥩", "🐟", "🥗", "🍰", "🍦", "🥐", "🍺", "🍷", "🥤", "🧃", "🍹", "🍸",
            "🥪", "🍔", "🍟", "🌭", "🥙", "🍳", "🍞", "🧀", "🍎", "🍫", "🍬", "🚬", "🎰", "📰", "🎁", "🛒"
        };
    }
}
