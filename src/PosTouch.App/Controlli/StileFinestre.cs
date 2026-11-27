using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Servizi;

namespace PosTouch.App.Controlli
{
    /// <summary>Colori delle finestre per un tema.</summary>
    public sealed record Tavolozza(Color Scheda, Color Testo, Color Muto, Color Bordo, Color Tasto, Color TastoTesto,
                                   Color Campo, Color CampoTesto, Color Secondario, Color SecondarioTesto, Color Icona);

    /// <summary>
    /// Stile unico delle finestre (tastierini, messaggi, scelte…): scheda arrotondata, intestazione con icona,
    /// titolo e dettaglio, valore in un campo, tasti uguali, ANNULLA a sinistra e OK verde a destra.
    /// Tema Scuro → finestre scure; Classico e Chiaro → finestre bianche.
    /// Solo grafica: le finestre restano quelle di prima, con le stesse funzioni.
    /// </summary>
    public static class StileFinestre
    {
        public const string FontIcone = "MDI";
        public static readonly Color Verde = Color.FromArgb("#2E7D32");

        private static readonly Tavolozza Chiara = new(Ui.C("#FFFFFF"), Ui.C("#263238"), Ui.C("#607D8B"), Ui.C("#E0E6EA"), Ui.C("#F5F7F8"), Ui.C("#263238"),
                                                       Ui.C("#F5F7F8"), Ui.C("#263238"), Ui.C("#ECEFF1"), Ui.C("#37474F"), Ui.C("#00897B"));
        private static readonly Tavolozza Scura = new(Ui.C("#263238"), Ui.C("#ECEFF1"), Ui.C("#90A4AE"), Ui.C("#37474F"), Ui.C("#37474F"), Ui.C("#FFFFFF"),
                                                      Ui.C("#1B2429"), Ui.C("#FFFFFF"), Ui.C("#455A64"), Ui.C("#ECEFF1"), Ui.C("#80CBC4"));

        /// <summary>Tavolozza del tema scelto in Impostazioni (1 = Scuro).</summary>
        public static Tavolozza T => (ContestoCassa.Impostazioni?.TemaInterfaccia ?? 0) == 1 ? Scura : Chiara;

        private static string G(int c) => char.ConvertFromUtf32(c);
        public static readonly string IcoChiudi = G(0xF0156), IcoCancella = G(0xF0B5C), IcoCoperti = G(0xF0849), IcoQuantita = G(0xF0423),
            IcoPrezzo = G(0xF04FC), IcoEuro = G(0xF01AD), IcoBarcode = G(0xF0072), IcoLucchetto = G(0xF0341), IcoReparto = G(0xF19DC),
            IcoSconto = G(0xF046F), IcoPersona = G(0xF0004), IcoMatita = G(0xF03EB), IcoInfo = G(0xF02FD), IcoAvviso = G(0xF002A),
            IcoErrore = G(0xF015A), IcoOk = G(0xF05E1), IcoDomanda = G(0xF0625), IcoGrafico = G(0xF154E), IcoBarre = G(0xF0128),
            IcoScontrino = G(0xF0824), IcoRiavvia = G(0xF0709), IcoChiusura = G(0xF097F), IcoElenco = G(0xF0279), IcoStampante = G(0xF042A),
            IcoDevia = G(0xF04E1), IcoCestino = G(0xF09E7), IcoRiprova = G(0xF0450), IcoTavolo = G(0xF05BC), IcoContanti = G(0xF0116),
            IcoCarta = G(0xF019B), IcoPercento = G(0xF1278);

        /// <summary>Testo senza emoji iniziali ("🍽️ QUANTI COPERTI?" → "QUANTI COPERTI?").</summary>
        public static string SenzaEmoji(string t)
        {
            if (string.IsNullOrEmpty(t)) return t ?? "";
            int i = 0;
            while (i < t.Length && !char.IsLetterOrDigit(t[i]) && t[i] != '(' && t[i] != '%' && t[i] != '€') i++;
            return t[i..].Trim();
        }

        /// <summary>"💶 PREZZO — MARGHERITA" o "TITOLO\ndettaglio" → (titolo, dettaglio).</summary>
        public static (string titolo, string dettaglio) Dividi(string testo)
        {
            var t = SenzaEmoji(testo ?? "");
            int a = t.IndexOf('\n');
            if (a > 0) return (t[..a].Trim(), t[(a + 1)..].Replace('\n', ' ').Trim());
            int b = t.IndexOf(" — ", StringComparison.Ordinal);
            if (b > 0) return (t[..b].Trim(), t[(b + 3)..].Trim());
            return (t, "");
        }

        /// <summary>Icona scelta dal titolo della finestra.</summary>
        public static string IconaPer(string titolo)
        {
            var t = (titolo ?? "").ToUpperInvariant();
            if (t.Contains("COPERT")) return IcoCoperti;
            if (t.Contains("QUANTIT") || t.Contains("PEZZI")) return IcoQuantita;
            if (t.Contains("SCONTO")) return IcoSconto;
            if (t.Contains("PREZZO") || t.Contains("COSTO") || t.Contains("FONDO") || t.Contains("CONTANTI") || t.Contains("€")) return IcoPrezzo;
            if (t.Contains("BARCODE") || t.Contains("EAN")) return IcoBarcode;
            if (t.Contains("PIN") || t.Contains("PASSWORD")) return IcoLucchetto;
            if (t.Contains("REPARTO")) return IcoReparto;
            if (t.Contains("NOME") || t.Contains("CLIENTE") || t.Contains("OPERATORE")) return IcoPersona;
            if (t.Contains("STAMP") || t.Contains("COMANDA")) return IcoStampante;
            if (t.Contains("TAVOLO")) return IcoTavolo;
            return IcoMatita;
        }

        /// <summary>Scheda della finestra: arrotondata, ombra, colore del tema.</summary>
        public static Border Scheda(View contenuto, double padding = 16)
        {
            var t = T;
            return new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = t.Scheda, StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.35f, Radius = 20, Offset = new Point(0, 8) },
                Padding = new Thickness(padding),
                Content = contenuto
            };
        }

        /// <summary>Intestazione: icona in un riquadro, titolo e sotto il dettaglio, X per chiudere (se serve).</summary>
        public static View Intestazione(string icona, string titolo, string dettaglio = null, Action chiudi = null)
        {
            var t = T;
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
            g.Add(new Border
            {
                WidthRequest = 38, HeightRequest = 38, BackgroundColor = t.Secondario, StrokeThickness = 0, VerticalOptions = LayoutOptions.Center,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = icona, FontFamily = FontIcone, FontSize = 22, TextColor = t.Icona, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            }, 0, 0);
            var testi = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 1 };
            testi.Add(new Label { Text = titolo, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = t.Testo, LineBreakMode = LineBreakMode.WordWrap });
            if (!string.IsNullOrWhiteSpace(dettaglio))
                testi.Add(new Label { Text = dettaglio, FontSize = 13, TextColor = t.Muto, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 });
            g.Add(testi, 1, 0);
            if (chiudi != null)
            {
                var x = new Button { Text = IcoChiudi, FontFamily = FontIcone, FontSize = 22, TextColor = t.Muto, BackgroundColor = Colors.Transparent,
                                     WidthRequest = 44, HeightRequest = 44, Padding = 0, CornerRadius = 10, VerticalOptions = LayoutOptions.Center };
                SemanticProperties.SetDescription(x, "Chiudi");
                x.Clicked += (s, e) => chiudi();
                g.Add(x, 2, 0);
            }
            return g;
        }

        /// <summary>Campo del valore: sfondo leggero, nota piccola a sinistra, valore grande a destra.</summary>
        public static Border Campo(Label valore, Label nota = null)
        {
            var t = T;
            valore.FontSize = 32; valore.FontAttributes = FontAttributes.Bold; valore.TextColor = t.CampoTesto;
            valore.HorizontalTextAlignment = TextAlignment.End; valore.VerticalOptions = LayoutOptions.End;
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 10 };
            if (nota != null)
            {
                nota.FontSize = 12; nota.TextColor = t.Muto; nota.VerticalOptions = LayoutOptions.End; nota.Margin = new Thickness(0, 0, 0, 6);
                g.Add(nota, 0, 0);
            }
            g.Add(valore, 1, 0);
            return new Border
            {
                BackgroundColor = t.Campo, Stroke = new SolidColorBrush(t.Bordo), StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(14, 8), Content = g
            };
        }

        /// <summary>Tasto del tastierino (cifra).</summary>
        public static Button TastoCifra(string testo, EventHandler clic)
        {
            var t = T;
            var b = new Button
            {
                Text = testo, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = t.TastoTesto, BackgroundColor = t.Tasto,
                BorderColor = t.Bordo, BorderWidth = 1, CornerRadius = 10, Padding = 0, HeightRequest = 50
            };
            b.Clicked += clic;
            return b;
        }

        /// <summary>Tasto con icona (cancella ⌫ e simili), colore secondario.</summary>
        public static Button TastoIcona(string icona, string descrizione, EventHandler clic, double altezza = 50)
        {
            var t = T;
            var b = new Button
            {
                Text = icona, FontFamily = FontIcone, FontSize = 24, TextColor = t.SecondarioTesto, BackgroundColor = t.Secondario,
                CornerRadius = 10, Padding = 0, HeightRequest = altezza
            };
            SemanticProperties.SetDescription(b, descrizione);
            b.Clicked += clic;
            return b;
        }

        /// <summary>Tasto secondario (ANNULLA, CHIUDI).</summary>
        public static Button TastoSecondario(string testo, EventHandler clic, double altezza = 50)
        {
            var t = T;
            var b = new Button { Text = testo, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = t.SecondarioTesto, BackgroundColor = t.Secondario,
                                 CornerRadius = 12, HeightRequest = altezza, Padding = new Thickness(6, 0) };
            b.Clicked += clic;
            return b;
        }

        /// <summary>Tasto principale (OK, APPLICA…), verde se non si indica un colore.</summary>
        public static Button TastoPrincipale(string testo, EventHandler clic, Color colore = null, double altezza = 50)
        {
            var b = new Button { Text = testo, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, BackgroundColor = colore ?? Verde,
                                 CornerRadius = 12, HeightRequest = altezza, Padding = new Thickness(6, 0) };
            b.Clicked += clic;
            return b;
        }

        /// <summary>ANNULLA a sinistra, OK a destra (un po' più largo).</summary>
        public static Grid Azioni(Button annulla, Button ok)
        {
            var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(1.3, GridUnitType.Star)) }, ColumnSpacing = 10 };
            g.Add(annulla, 0, 0);
            g.Add(ok, 1, 0);
            return g;
        }

        /// <summary>Tastierino 3×4 con le cifre e due tasti speciali in basso (es. "," o "C", e cancella).</summary>
        public static Grid Tastierino(Action<string> cifra, View sinistraBasso, View destraBasso, List<Button> raccolta = null)
        {
            var g = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
            for (int c = 0; c < 3; c++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int r = 0; r < 4; r++) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            string[] cifre = { "7", "8", "9", "4", "5", "6", "1", "2", "3" };
            for (int i = 0; i < cifre.Length; i++)
            {
                string c = cifre[i];
                var b = TastoCifra(c, (s, e) => cifra(c));
                raccolta?.Add(b);
                g.Add(b, i % 3, i / 3);
            }
            var zero = TastoCifra("0", (s, e) => cifra("0"));
            raccolta?.Add(zero);
            g.Add(sinistraBasso, 0, 3);
            g.Add(zero, 1, 3);
            g.Add(destraBasso, 2, 3);
            if (sinistraBasso is Button bs) raccolta?.Add(bs);
            if (destraBasso is Button bd) raccolta?.Add(bd);
            return g;
        }

        /// <summary>Riga di un elenco (funzioni, scelte): icona a sinistra, titolo, sotto la spiegazione.</summary>
        public static View RigaElenco(string icona, Color coloreIcona, string titolo, string spiegazione, Func<Task> azione)
        {
            var t = T;
            var g = new Grid { ColumnDefinitions = { new(new GridLength(36)), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10, Padding = new Thickness(12, 8), MinimumHeightRequest = 52 };
            g.Add(new Label { Text = icona, FontFamily = FontIcone, FontSize = 24, TextColor = coloreIcona ?? t.Icona, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center }, 0, 0);
            var testi = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 1 };
            testi.Add(new Label { Text = titolo, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = t.Testo });
            if (!string.IsNullOrWhiteSpace(spiegazione)) testi.Add(new Label { Text = spiegazione, FontSize = 12, TextColor = t.Muto });
            g.Add(testi, 1, 0);
            g.Add(new Label { Text = G(0xF0142), FontFamily = FontIcone, FontSize = 20, TextColor = t.Muto, VerticalOptions = LayoutOptions.Center }, 2, 0);   // chevron-right
            var riga = new Border { BackgroundColor = t.Tasto, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = g };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                riga.Opacity = 0.6;
                riga.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(140), () => riga.Opacity = 1);
                await azione();
            };
            riga.GestureRecognizers.Add(tap);
            return riga;
        }
    }
}
