using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Sale;

namespace PosTouch.App.Pagine
{
    public partial class TavoliPage
    {
        // ══════════════ griglia tavoli (CaricaGrigliaSlot del PC) ══════════════

        private string _firmaTavoli;

        private void CostruisciTavoli()
        {
            var sala = Sale().FirstOrDefault(x => x.Id == _salaCorrente);
            if (sala == null) { _griglia.Children.Clear(); _firmaTavoli = null; _info.Text = "Nessuna sala configurata: IMPOSTAZIONI → SALE E COMANDE"; return; }
            var aperti = ContestoCassa.Tavoli.Aperti().Where(t => t.SalaId == sala.Id && t.Occupato).ToDictionary(t => t.Numero);
            var errori = ContestoCassa.Tavoli.TavoliConErroreStampa();

            // aggiornamento ogni 10 s: si ridisegna solo se qualcosa è cambiato davvero (prestazioni)
            string firma = $"{sala.Id}|{sala.NumeroIniziale}|{sala.NumeroTavoli}|{string.Join(",", errori)}|" +
                           string.Join(";", aperti.Values.OrderBy(t => t.Numero).Select(t =>
                               $"{t.Numero}:{t.TotaleCent}:{t.Preconto}:{t.PortataCorrente}:{t.PortataMassima}:{t.DaComandare}:{t.StorniDaInviare}:{t.NomeCliente}"));
            if (firma == _firmaTavoli && _griglia.Children.Count > 0) return;
            _firmaTavoli = firma;

            _griglia.BatchBegin();
            _griglia.Children.Clear();
            for (int k = 0; k < Math.Max(1, sala.NumeroTavoli); k++)
            {
                int numero = sala.NumeroIniziale + k;
                aperti.TryGetValue(numero, out var t);
                bool errore = t != null && errori.Contains($"{sala.Id}:{numero}");
                _griglia.Add(sala.Id == ConfigSale.SalaConti ? SlotConto(sala, numero, t) : SlotTavolo(sala, numero, t, errore));
            }
            _griglia.BatchCommit();
            _info.Text = sala.Id == ConfigSale.SalaConti
                ? $"Conti sospesi — {aperti.Count} / {sala.NumeroTavoli} occupati"
                : $"{sala.Nome} — {aperti.Count} / {sala.NumeroTavoli} tavoli occupati";
        }

        private static string Euro(int cent) => $"€ {cent / 100m:N2}";
        private static Label Testo(string t, double size, string colore, bool grassetto = false, double sopra = 0) => new Label
        {
            Text = t, FontSize = size, TextColor = Ui.C(colore), FontAttributes = grassetto ? FontAttributes.Bold : FontAttributes.None,
            HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(0, sopra, 0, 0)
        };

        /// <summary>Tavolo di una sala: icona tavolo, "Tav.N", totale, stato portate.</summary>
        private View SlotTavolo(Sala sala, int numero, TavoloAperto t, bool erroreStampa)
        {
            bool occupato = t != null;
            bool inCorso = occupato && t.PortataMassima > 0 && t.PortataCorrente < t.PortataMassima;
            bool completate = occupato && t.PortataMassima > 0 && t.PortataCorrente >= t.PortataMassima;
            var stack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            string sfondo;

            if (erroreStampa)
            {
                sfondo = "#212121";
                stack.Add(Testo($"Tav.{numero}", 15, "#FFFFFF", true));
                stack.Add(Testo("⚠ ERRORE", 14, "#FF5252", true, 4));
                stack.Add(Testo("STAMPA", 13, "#FF8A80", true));
                stack.Add(Testo(Euro(t.TotaleCent), 11, "#BDBDBD", false, 2));
                var g = new Grid();
                g.Add(new BoxView { Color = Ui.C("#D32F2F"), HeightRequest = 6, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 8, 0, 0) });
                g.Add(new BoxView { Color = Ui.C("#D32F2F"), HeightRequest = 6, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 8) });
                g.Add(stack);
                return Slot(g, sfondo, t, () => ApriAsync(sala, numero));
            }

            // verde e rosso un po' più scuri del PC: il testo bianco si legge anche su 8"
            if (!occupato) sfondo = "#ECEFF1";
            else if (t.Preconto) sfondo = "#FDD835";
            else if (inCorso) sfondo = "#E53935";
            else sfondo = "#43A047";

            stack.Add(IconaTavolo(occupato));
            stack.Add(Testo($"Tav.{numero}", 16, !occupato ? "#546E7A" : t.Preconto ? "#333333" : "#FFFFFF", true, 4));
            if (occupato)
            {
                stack.Add(Testo(t.Preconto ? $"📋 {Euro(t.TotaleCent)}" : Euro(t.TotaleCent), 14, t.Preconto ? "#333333" : "#FFFFFF", true, 2));
                string stato = t.Preconto ? "PRECONTO" : inCorso ? $"⏩ {t.PortataCorrente}/{t.PortataMassima}" : completate ? "✅ PORTATE OK" : t.Apertura.ToString("dd/MM HH:mm");
                stack.Add(Testo(stato, 12, t.Preconto ? "#BF360C" : "#FFFFFF", t.Preconto || t.PortataMassima > 0));
            }
            else stack.Add(Testo("libero", 12, "#607D8B", false, 2));
            return Slot(stack, sfondo, t, () => ApriAsync(sala, numero));
        }

        /// <summary>Conto cliente sospeso: 🧑/👤, nome, totale.</summary>
        private View SlotConto(Sala sala, int numero, TavoloAperto t)
        {
            bool occupato = t != null;
            var stack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            stack.Add(new Label { Text = occupato ? "🧑" : "👤", FontSize = 36, HorizontalOptions = LayoutOptions.Center, Opacity = occupato ? 1.0 : 0.4 });
            if (occupato)
            {
                string nome = string.IsNullOrWhiteSpace(t.NomeCliente) ? $"Cliente {numero}" : t.NomeCliente;
                if (nome.Length > 12) nome = nome[..12] + "…";
                stack.Add(Testo(nome, 13, t.Preconto ? "#333333" : "#1B5E20", true, 2));
                stack.Add(Testo(t.Preconto ? $"📋 {Euro(t.TotaleCent)}" : Euro(t.TotaleCent), 14, t.Preconto ? "#333333" : "#2E7D32", true, 2));
                stack.Add(Testo(t.Preconto ? "PRECONTO" : t.Apertura.ToString("dd/MM HH:mm"), 12, t.Preconto ? "#BF360C" : "#2E7D32", t.Preconto));
            }
            else
            {
                stack.Add(Testo($"Cliente {numero}", 13, "#546E7A", false, 3));
                stack.Add(Testo("vuoto", 12, "#607D8B"));
            }
            string sfondo = !occupato ? "#ECEFF1" : t.Preconto ? "#FDD835" : "#A5D6A7";
            return Slot(stack, sfondo, t, () => ApriAsync(sala, numero));
        }

        /// <summary>Lato dei riquadri: 130 come il PC su 8", più grandi sugli schermi larghi (vedi OnSizeAllocated).</summary>
        private static double LatoSlot = 130;

        /// <summary>Riquadro 130×130 angoli 12 (PC). Bordo arancio = articoli ancora da comandare.</summary>
        private static View Slot(View contenuto, string sfondo, TavoloAperto t, Func<Task> apri)
        {
            var b = new Border
            {
                WidthRequest = LatoSlot, HeightRequest = LatoSlot, Margin = new Thickness(6),
                BackgroundColor = Ui.C(sfondo),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Stroke = new SolidColorBrush(Ui.C(t?.DaComandare == true || t?.StorniDaInviare == true ? "#FF6F00" : sfondo)),
                StrokeThickness = t?.DaComandare == true || t?.StorniDaInviare == true ? 4 : 0,
                Content = contenuto
            };
            Ui.Tocco(b, async () => { b.Opacity = 0.8; try { await apri(); } finally { b.Opacity = 1; } });
            return b;
        }

        /// <summary>Icona tavolo disegnata come sul PC (CreaIconaTavolo): piano + 2 gambe + piedini, 40×32.</summary>
        private static View IconaTavolo(bool occupato)
        {
            var c = Ui.C(occupato ? "#FFFFFF" : "#90A4AE");
            var a = new AbsoluteLayout { WidthRequest = 40, HeightRequest = 32, HorizontalOptions = LayoutOptions.Center };
            void R(double x, double y, double w, double h, double raggio)
            {
                var r = new Border { BackgroundColor = c, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = raggio } };
                AbsoluteLayout.SetLayoutBounds(r, new Rect(x, y, w, h));
                a.Add(r);
            }
            R(1, 6, 38, 6, 2);    // piano
            R(5, 12, 4, 16, 0);   // gamba sinistra
            R(31, 12, 4, 16, 0);  // gamba destra
            R(3, 28, 8, 3, 1);    // piedino sinistro
            R(29, 28, 8, 3, 1);   // piedino destro
            return a;
        }

        private async Task ApriAsync(Sala sala, int numero)
        {
            if (_apertura) return;
            _apertura = true;
            try
            {
                // conto cliente libero: chiedi il nome (come il PC)
                string nome = null;
                if (sala.Id == ConfigSale.SalaConti && !ContestoCassa.Tavoli.Aperti().Any(t => t.SalaId == sala.Id && t.Numero == numero && t.Occupato))
                {
                    nome = await TestoBox.Chiedi("👤 NOME CLIENTE");
                    if (string.IsNullOrWhiteSpace(nome)) return;
                }
                // prima torna alla vendita: se la mappa non si chiude (navigazione in corso) il tavolo NON si apre
                if (!await Navigazione.Chiudi(this)) return;
                SessioneTavolo s;
                try { s = ContestoCassa.ApriTavolo(sala.Id, numero, nome?.ToUpperInvariant()); }
                catch (Exception ex) { await MessaggioBox.Errore("TAVOLO", $"Impossibile aprire il tavolo:\n{ex.Message}"); return; }
                await ContestoCassa.PaginaVendita.ApriTavoloAsync(s);
            }
            finally { _apertura = false; }
        }
    }
}
