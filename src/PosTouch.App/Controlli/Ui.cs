using Microsoft.Maui.Controls.Shapes;

namespace PosTouch.App.Controlli
{
    /// <summary>Piccoli costruttori per tasti e testi nello stile POS Touch (usati da popup e pagine create da codice).</summary>
    public static class Ui
    {
        // colori già convertiti: ogni "#RRGGBB" si legge una volta sola (prima a ogni tasto, riga e cambio categoria).
        // Color è immutabile, quindi lo stesso oggetto si può riusare ovunque (anche da altri thread).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Color> _colori = new(StringComparer.OrdinalIgnoreCase);

        public static Color C(string hex)
        {
            if (hex == null) return Color.FromArgb(hex);   // stesso errore di prima
            if (_colori.TryGetValue(hex, out var c)) return c;
            c = Color.FromArgb(hex);
            if (_colori.Count < 2000) _colori[hex] = c;
            return c;
        }

        public static Color ColoreSicuro(string hex, string predefinito)
        {
            if (string.IsNullOrWhiteSpace(hex)) return C(predefinito);
            string h = hex.Trim();
            if (_colori.TryGetValue(h, out var c)) return c;
            if (!Color.TryParse(h, out c)) return C(predefinito);
            if (_colori.Count < 2000) _colori[h] = c;
            return c;
        }

        public static Button Tasto(string testo, string sfondo, string colTesto = "#FFFFFF", double fontSize = 16,
                                   int raggio = 8, EventHandler clic = null, double altezza = -1)
        {
            var b = new Button
            {
                Text = testo,
                BackgroundColor = C(sfondo),
                TextColor = C(colTesto),
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = raggio,
                Padding = new Thickness(4, 0),
                BorderWidth = 0
            };
            if (altezza > 0) b.HeightRequest = altezza;
            if (clic != null) b.Clicked += clic;
            return b;
        }

        public static Label Testo(string testo, double fontSize = 14, string colore = "#111111", bool grassetto = false,
                                  TextAlignment allinea = TextAlignment.Start)
            => new Label
            {
                Text = testo,
                FontSize = fontSize,
                TextColor = C(colore),
                FontAttributes = grassetto ? FontAttributes.Bold : FontAttributes.None,
                HorizontalTextAlignment = allinea,
                LineBreakMode = LineBreakMode.WordWrap
            };

        public static Border Riquadro(View contenuto, string sfondo, double raggio = 0, Thickness? padding = null,
                                      string bordo = null, double spessore = 0)
            => new Border
            {
                Content = contenuto,
                BackgroundColor = C(sfondo),
                StrokeShape = new RoundRectangle { CornerRadius = raggio },
                Stroke = bordo == null ? null : new SolidColorBrush(C(bordo)),
                StrokeThickness = bordo == null ? 0 : spessore,
                Padding = padding ?? new Thickness(0)
            };

        /// <summary>
        /// Chiude la tastiera di Android e fa arrivare all'app il testo ancora "in scrittura" (la parola sottolineata
        /// dei suggerimenti): da chiamare prima di leggere o salvare i campi. Prima, premendo OK/SALVA a tastiera aperta,
        /// l'ultima parola poteva mancare (es. barcode non salvato).
        /// </summary>
        public static async Task ChiudiTastieraAsync(Element radice)
        {
            if (radice is not IVisualTreeElement albero) return;
            bool chiusa = false;
            foreach (var campo in albero.GetVisualTreeDescendants().OfType<InputView>().Where(x => x.IsFocused).ToList())
            {
                try { await campo.HideSoftInputAsync(CancellationToken.None); } catch { }
                campo.Unfocus();
                chiusa = true;
            }
            if (chiusa) await Task.Delay(120);   // il tempo di consegnare il testo e di far scendere la tastiera
        }

        public static void Tocco(View vista, Action azione)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => { Lampeggia(vista); azione(); };
            vista.GestureRecognizers.Add(tap);
        }

        /// <summary>
        /// Dito tenuto premuto (~0,6 s) sulla vista: azione separata dal tocco normale.
        /// Il tocco che arriva alzando il dito va ignorato: chi usa anche Tocco controlla <see cref="ToccoDaIgnorare"/>.
        /// </summary>
        public static void TenutoPremuto(View vista, Action azione)
        {
            // La pressione lunga di Android non arriva: il tocco normale (TapGestureRecognizer) si prende l'evento.
            // Si misura da soli: dito giù → dopo 600 ms senza alzarlo né spostarlo = pressione lunga.
            IDispatcherTimer timer = null;
            Point? inizio = null;
            bool scattato = false;
            void Ferma() { timer?.Stop(); inizio = null; }
            var p = new PointerGestureRecognizer();
            p.PointerPressed += (s, e) =>
            {
                inizio = e.GetPosition(vista) ?? new Point();
                scattato = false;
                timer ??= vista.Dispatcher?.CreateTimer();
                if (timer == null) return;
                timer.Interval = TimeSpan.FromMilliseconds(600);
                timer.IsRepeating = false;
                timer.Tick -= Scatta; timer.Tick += Scatta;
                timer.Start();
            };
            p.PointerMoved += (s, e) =>
            {
                // dito che scorre la mappa: non è una pressione lunga
                var q = e.GetPosition(vista);
                if (inizio.HasValue && q.HasValue && (Math.Abs(q.Value.X - inizio.Value.X) > 12 || Math.Abs(q.Value.Y - inizio.Value.Y) > 12)) Ferma();
            };
            p.PointerReleased += (s, e) =>
            {
                // dito alzato dopo una pressione lunga (anche dopo secondi): il tocco che segue non deve aprire il tavolo
                if (scattato)
                {
                    UltimaPressioneLunga = DateTime.Now;
                    // se il tocco non arriva affatto, il prossimo tocco vero non va perso
                    vista.Dispatcher?.DispatchDelayed(TimeSpan.FromMilliseconds(500), () => _ignoraTocco = false);
                }
                scattato = false;
                Ferma();
            };
            p.PointerExited += (s, e) => Ferma();
            vista.GestureRecognizers.Add(p);

            void Scatta(object s, EventArgs e)
            {
                timer?.Stop();
                if (inizio == null) return;
                inizio = null;
                scattato = true;
                UltimaPressioneLunga = DateTime.Now;
                _ignoraTocco = true;
#if ANDROID
                try { (vista.Handler?.PlatformView as Android.Views.View)?.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress); } catch { }
#endif
                azione();
            }
        }

        /// <summary>Ora dell'ultima pressione lunga (o del dito alzato dopo di essa).</summary>
        public static DateTime UltimaPressioneLunga { get; private set; } = DateTime.MinValue;
        private static bool _ignoraTocco;

        /// <summary>
        /// true per il tocco che arriva alzando il dito dopo una pressione lunga (una volta sola, entro 15 s):
        /// quel tocco non deve aprire niente.
        /// </summary>
        public static bool ToccoDaIgnorare()
        {
            bool si = _ignoraTocco && (DateTime.Now - UltimaPressioneLunga).TotalSeconds < 15;
            _ignoraTocco = false;
            return si;
        }

        private static readonly HashSet<View> _inLampeggio = new HashSet<View>();

        /// <summary>Riscontro del tocco: la vista si scurisce un attimo (come i tasti), così si capisce che il tocco è arrivato.</summary>
        private static void Lampeggia(View vista)
        {
            try
            {
                var d = vista.Dispatcher;
                if (d == null) return;   // senza dispatcher non si potrebbe rimettere com'era
                if (!_inLampeggio.Add(vista)) return;   // secondo tocco rapido: non si scurisce di nuovo (resterebbe grigia)
                double prima = vista.Opacity;
                double scuro = prima * 0.6;
                vista.Opacity = scuro;
                d.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
                {
                    _inLampeggio.Remove(vista);
                    if (Math.Abs(vista.Opacity - scuro) < 0.001) vista.Opacity = prima;   // se intanto qualcuno l'ha cambiata, non si tocca
                });
            }
            catch { }   // solo estetica: mai un errore per questo
        }

        public static Grid Griglia(int righe, int colonne, double spazio = 0)
        {
            var g = new Grid { RowSpacing = spazio, ColumnSpacing = spazio };
            for (int i = 0; i < righe; i++) g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            for (int i = 0; i < colonne; i++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            return g;
        }
    }
}
