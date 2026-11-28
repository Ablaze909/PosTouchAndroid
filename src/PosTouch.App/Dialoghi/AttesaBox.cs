using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>Finestra "attendere" senza tasti: si chiude da sola a lavoro finito (es. ottimizzazione archivi all'uscita).</summary>
    public class AttesaBox : DialogoBase<bool>
    {
        public override void Indietro() { }   // non si interrompe a metà

        private AttesaBox(string titolo, string messaggio)
        {
            var t = StileFinestre.T;
            var corpo = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(6) };
            corpo.Add(new ActivityIndicator { IsRunning = true, Color = Ui.C("#1565C0"), HeightRequest = 40, WidthRequest = 40, HorizontalOptions = LayoutOptions.Center });
            corpo.Add(new Label { Text = titolo, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = t.Testo, HorizontalTextAlignment = TextAlignment.Center });
            corpo.Add(new Label { Text = messaggio, FontSize = 15, TextColor = t.Muto, HorizontalTextAlignment = TextAlignment.Center });
            var riquadro = StileFinestre.Scheda(corpo);
            Content = riquadro;
            Dimensiona(riquadro, 380);
        }

        /// <summary>Mostra la finestra mentre il lavoro gira in sottofondo; la chiude alla fine (anche se il lavoro fallisce).</summary>
        public static async Task Durante(string titolo, string messaggio, Func<Task> lavoro)
        {
            var box = new AttesaBox(titolo, messaggio);
            var mostrata = Dialogo.Mostra(box);
            try { await lavoro(); }
            finally { box.Chiudi(true); await mostrata; }
        }
    }
}
