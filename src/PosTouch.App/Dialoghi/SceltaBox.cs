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
            var t = StileFinestre.T;
            for (int i = 0; i < voci.Count; i++)
            {
                int indice = i;
                Button b;
                if (colori != null)
                {
                    b = Ui.Tasto(voci[i], colori[i], "#FFFFFF", fontSize, 10, (s, e) => Chiudi(indice));
                    b.BorderColor = t.Bordo; b.BorderWidth = 1;
                    b.ClassId = StilePagine.ColoreUtente;   // campione: deve restare esattamente il colore che si salva
                }
                else
                {
                    // voce: senza l'emoji iniziale (le icone dei tasti categoria invece restano: sono la scelta)
                    string testo = fontSize >= 24 ? voci[i] : StileFinestre.SenzaEmoji(voci[i]);
                    b = new Button { Text = testo, FontSize = fontSize, FontAttributes = FontAttributes.Bold, TextColor = t.TastoTesto, BackgroundColor = t.Tasto,
                                     BorderColor = t.Bordo, BorderWidth = 1, CornerRadius = 12, Padding = new Thickness(6, 0), LineBreakMode = LineBreakMode.WordWrap };
                    b.Clicked += (s, e) => Chiudi(indice);
                }
                griglia.Add(b, i % colonne, i / colonne);
            }

            var (tit, dettaglio) = StileFinestre.Dividi(titolo);
            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 12 };
            corpo.Add(StileFinestre.Intestazione(StileFinestre.IcoElenco, tit, dettaglio, () => Chiudi(-1)), 0, 0);
            corpo.Add(new ScrollView { Content = griglia }, 0, 1);
            corpo.Add(StileFinestre.TastoSecondario("ANNULLA", (s, e) => Chiudi(-1)), 0, 2);

            var riquadro = StileFinestre.Scheda(corpo);
            Content = riquadro;
            // minimo 460 di larghezza: con 1-2 colonne il titolo andava a capo e le voci lunghe si tagliavano
            Dimensiona(riquadro, Math.Min(760, Math.Max(460, 110 * colonne + 80)), Math.Min(720, 200 + righe * (altezzaTasto + 8)));
        }

        /// <summary>Tasto indietro di Android = ANNULLA (-1), non la prima voce.</summary>
        public override void Indietro() => Chiudi(-1);

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
