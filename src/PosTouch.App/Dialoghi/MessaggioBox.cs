using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Messaggi (informazione, avviso, errore, domanda, conferma) nello stile comune delle finestre:
    /// icona nel suo colore, titolo, testo che scorre se lungo, uno o due tasti.
    /// </summary>
    public class MessaggioBox : DialogoBase<bool>
    {
        private readonly bool _bloccaIndietro;
        public override bool UnicoPerTipo => false;
        /// <summary>Domande fiscali: il tasto indietro non deve valere "NO" (rischio doppio scontrino).</summary>
        public override void Indietro() { if (!_bloccaIndietro) base.Indietro(); }

        private MessaggioBox(string titolo, string messaggio, string icona, string tastoSinistro, string tastoDestro,
                             string coloreSinistro, string coloreDestro, bool bloccaIndietro = false)
        {
            _bloccaIndietro = bloccaIndietro;
            var t = StileFinestre.T;
            var (glifo, colore) = icona switch
            {
                "✅" => (StileFinestre.IcoOk, Ui.C("#2E7D32")),
                "❌" => (StileFinestre.IcoErrore, Ui.C("#D32F2F")),
                "⚠️" => (StileFinestre.IcoAvviso, Ui.C("#EF6C00")),
                "❓" => (StileFinestre.IcoDomanda, Ui.C("#1565C0")),
                _ => (StileFinestre.IcoInfo, Ui.C("#1565C0"))
            };
            var corpo = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(6, 6, 6, 0) };
            // icona in un cerchio del suo colore, chiaro
            corpo.Add(new Border
            {
                WidthRequest = 56, HeightRequest = 56, HorizontalOptions = LayoutOptions.Center, StrokeThickness = 0,
                BackgroundColor = colore.WithAlpha(0.15f), StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                Content = new Label { Text = glifo, FontFamily = StileFinestre.FontIcone, FontSize = 30, TextColor = colore,
                                      HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            });
            corpo.Add(new Label { Text = StileFinestre.SenzaEmoji(titolo), FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = t.Testo,
                                  HorizontalTextAlignment = TextAlignment.Center });
            _testo = new ScrollView
            {
                MaximumHeightRequest = 380,
                Content = new Label { Text = messaggio, FontSize = 15, TextColor = t.Muto, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1.3 }
            };
            corpo.Add(_testo);

            View tasti;
            // il tasto principale ha il colore del messaggio (rosso per le conferme critiche, verde per SÌ…)
            var principale = StileFinestre.TastoPrincipale(tastoDestro, (s, e) => Chiudi(true),
                                                           tastoSinistro == null && coloreDestro == "#2196F3" ? Ui.C("#1565C0") : Ui.C(coloreDestro));
            if (tastoSinistro != null)
                tasti = StileFinestre.Azioni(StileFinestre.TastoSecondario(tastoSinistro, (s, e) => Chiudi(false)), principale);
            else tasti = principale;
            tasti.Margin = new Thickness(0, 8, 0, 0);
            corpo.Add(tasti);

            var riquadro = StileFinestre.Scheda(corpo);
            Content = riquadro;
            Dimensiona(riquadro, 420);
        }

        private readonly ScrollView _testo;

        /// <summary>Su schermi bassi il testo scorre e i tasti restano sempre visibili.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (height > 0) _testo.MaximumHeightRequest = Math.Max(80, Math.Min(380, height - 24 - 230));
        }

        public static Task Info(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "ℹ️", null, "OK", null, "#2196F3"));
        public static Task Successo(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "✅", null, "OK", null, "#2E7D32"));
        public static Task Errore(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "❌", null, "OK", null, "#D32F2F"));
        public static Task Avviso(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "⚠️", null, "OK", null, "#455A64"));
        /// <summary>Domanda NO / SÌ.</summary>
        public static Task<bool> Domanda(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "❓", "NO", "SÌ", "#9E9E9E", "#2E7D32"));
        /// <summary>Domanda su un esito fiscale incerto: si risponde solo con i tasti (indietro disattivato).</summary>
        public static Task<bool> DomandaFiscale(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "❓", "NO", "SÌ", "#9E9E9E", "#2E7D32", bloccaIndietro: true));
        /// <summary>Conferma critica ANNULLA / CONFERMA (rosso).</summary>
        /// <summary>
        /// Chiusura con modifiche: SALVA (true), NON SALVARE (false), indietro/annulla = resta nella pagina (null).
        /// Un doppio indietro non butta più le modifiche.
        /// </summary>
        public static async Task<bool?> SalvaPrimaDiChiudere(string messaggio)
        {
            int k = await SceltaBox.Testi($"💾 MODIFICHE NON SALVATE — {messaggio}", new List<string> { "💾 SALVA", "✖ NON SALVARE" }, 2);
            return k == 0 ? true : k == 1 ? false : (bool?)null;
        }

        public static Task<bool> Conferma(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "⚠️", "ANNULLA", "CONFERMA", "#9E9E9E", "#D32F2F"));
    }
}
