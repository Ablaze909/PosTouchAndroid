using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Scelta varianti in vendita (VarianteSelectionDialog del PC, ma a tutto schermo):
    /// varianti divise per GRUPPO, tasti grandi che si accendono/spengono, più varianti in un colpo, ✅ APPLICA.
    /// Restituisce le varianti scelte nell'ordine in cui sono state toccate (null = annullato).
    /// </summary>
    public class VariantiBox : DialogoBase<List<Variante>>
    {
        private static readonly CultureInfo It = new CultureInfo("it-IT");
        private readonly List<Variante> _scelte = new List<Variante>();
        private readonly bool _aggiunta;
        private readonly Label _riepilogo = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), VerticalOptions = LayoutOptions.Center };
        private readonly Button _applica;

        /// <summary>Tessere più grandi sugli schermi larghi (10"–15"); 1 sugli 8".</summary>
        private static double Scala
        {
            get
            {
                try
                {
                    var d = DeviceDisplay.MainDisplayInfo;
                    double larghezzaDp = Math.Max(d.Width, d.Height) / Math.Max(1, d.Density);
                    return Math.Clamp(larghezzaDp / 1280, 1, 1.3);
                }
                catch { return 1; }
            }
        }

        private VariantiBox(IList<Variante> varianti, bool aggiunta, string prodotto)
        {
            _aggiunta = aggiunta;
            string colore = aggiunta ? "#E65100" : "#B71C1C";

            // ── testata ──
            var testa = new VerticalStackLayout { Padding = new Thickness(20, 12), BackgroundColor = Ui.C(colore) };
            testa.Add(new Label { Text = aggiunta ? "＋ AGGIUNGI VARIANTI" : "﹣ RIMUOVI VARIANTI", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White });
            testa.Add(new Label { Text = prodotto, FontSize = 14, TextColor = Ui.C("#FFE0B2"), LineBreakMode = LineBreakMode.TailTruncation });

            // ── gruppi e tasti ──
            var lista = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12, 6) };
            bool perGruppo = PosTouch.App.Servizi.ContestoCassa.Impostazioni.VariantiPerGruppo;
            var gruppi = perGruppo
                ? varianti.GroupBy(v => v.GruppoDisplay).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                : varianti.GroupBy(v => "");   // flag spento: un solo elenco, senza titoli
            foreach (var g in gruppi)
            {
                if (perGruppo) lista.Add(new Label
                {
                    Text = g.Key.ToUpperInvariant(), FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#E65100"),
                    BackgroundColor = Ui.C("#FFF3E0"), Padding = new Thickness(10, 5), Margin = new Thickness(0, 6, 0, 2)
                });
                var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                var ordinate = perGruppo ? g.OrderBy(v => v.Ordine).ThenBy(v => v.Nome, StringComparer.OrdinalIgnoreCase)
                                         : g.OrderBy(v => v.Nome, StringComparer.OrdinalIgnoreCase);
                foreach (var v in ordinate)
                    flex.Add(Tasto(v));
                lista.Add(flex);
            }

            // ── piede ──
            _applica = Ui.Tasto("✅ APPLICA", "#4CAF50", fontSize: 16, altezza: 54, clic: (s, e) => Chiudi(_scelte.ToList()));
            var piede = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(170)), new(new GridLength(190)) }, ColumnSpacing = 10, Padding = new Thickness(14, 8, 14, 14) };
            piede.Add(_riepilogo, 0, 0);
            piede.Add(Ui.Tasto("❌ ANNULLA", "#9E9E9E", fontSize: 16, altezza: 54, clic: (s, e) => Chiudi(null)), 1, 0);
            piede.Add(_applica, 2, 0);
            AggiornaRiepilogo();

            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
            corpo.Add(testa, 0, 0);
            corpo.Add(new ScrollView { Content = lista }, 0, 1);
            corpo.Add(piede, 0, 2);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.White, StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0, Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 1000, 640);   // su tablet più piccoli si restringe da solo
        }

        private int PrezzoDi(Variante v) => _aggiunta ? v.PrezzoAggiuntaCent : v.PrezzoRimozioneCent;
        private string Euro(int cent) => (cent / 100m).ToString("0.00", It);

        /// <summary>Tasto variante: nome grande, prezzo sotto (verde in aggiunta, rosso in rimozione). Tocco = acceso/spento.</summary>
        private View Tasto(Variante v)
        {
            int p = PrezzoDi(v);
            var nome = new Label { Text = v.Nome, FontSize = 16 * Scala, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, MaxLines = 2, LineBreakMode = LineBreakMode.WordWrap };
            var prezzo = new Label
            {
                Text = p > 0 ? $"{(_aggiunta ? "+" : "-")}€ {Euro(p)}" : "gratis", FontSize = 13 * Scala, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center
            };
            var stack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 2, Children = { nome, prezzo } };
            var b = new Border
            {
                WidthRequest = 176 * Scala, HeightRequest = 72 * Scala, Margin = new Thickness(4), Padding = new Thickness(6, 4),
                StrokeShape = new RoundRectangle { CornerRadius = 10 }, StrokeThickness = 2, Content = stack
            };
            void Colora()
            {
                bool on = _scelte.Contains(v);
                string acceso = _aggiunta ? "#2E7D32" : "#C62828";
                b.BackgroundColor = Ui.C(on ? acceso : "#FFFFFF");
                b.Stroke = new SolidColorBrush(Ui.C(on ? acceso : "#E0E0E0"));
                nome.TextColor = Ui.C(on ? "#FFFFFF" : "#263238");
                prezzo.TextColor = Ui.C(on ? "#FFFFFF" : p > 0 ? acceso : "#90A4AE");
            }
            Colora();
            Ui.Tocco(b, () =>
            {
                if (!_scelte.Remove(v)) _scelte.Add(v);
                Colora();
                AggiornaRiepilogo();
            });
            return b;
        }

        private void AggiornaRiepilogo()
        {
            int tot = _scelte.Sum(PrezzoDi);
            _riepilogo.Text = _scelte.Count == 0 ? "Tocca una o più varianti"
                : $"{_scelte.Count} scelte" + (tot > 0 ? $"   {(_aggiunta ? "+" : "-")}€ {Euro(tot)} al pezzo" : "");
            _applica.IsEnabled = _scelte.Count > 0;
            _applica.Opacity = _scelte.Count > 0 ? 1 : 0.5;
        }

        public static Task<List<Variante>> Scegli(IList<Variante> varianti, bool aggiunta, string prodotto)
            => Dialogo.Mostra(new VariantiBox(varianti, aggiunta, prodotto));
    }
}
