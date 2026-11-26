using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Servizi;
using PosTouch.Core.Sale;

namespace PosTouch.App.Dialoghi
{
    /// <summary>🖨️ CODA STAMPA (CodaStampaDialog del PC): comande non ancora uscite, RIPROVA o ELIMINA.</summary>
    public class CodaStampaBox : DialogoBase<bool>
    {
        private readonly VerticalStackLayout _lista = new VerticalStackLayout { Spacing = 6 };

        public CodaStampaBox()
        {
            var titolo = new Label { Text = "🖨️ CODA STAMPA COMANDE", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center };
            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("🔄 RIPROVA TUTTE", "#1976D2", altezza: 56, clic: (s, e) => { ContestoCassa.Spooler.RiprovaTutto(); Chiudi(true); }), 0, 0);
            azioni.Add(Ui.Tasto("CHIUDI", "#607D8B", altezza: 56, clic: (s, e) => Chiudi(true)), 1, 0);

            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(20), RowSpacing = 14 };
            corpo.Add(titolo, 0, 0);
            corpo.Add(new ScrollView { Content = _lista }, 0, 1);
            corpo.Add(azioni, 0, 2);
            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#ECEFF1"), StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0,
                Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 760, 600);
            Carica();
        }

        private void Carica()
        {
            _lista.Children.Clear();
            var coda = ContestoCassa.Tavoli.CodaDaStampare(true);
            if (coda.Count == 0)
            {
                _lista.Add(new Label { Text = "✅ Nessuna stampa in coda.", FontSize = 17, TextColor = Ui.C("#2E7D32"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 40) });
                return;
            }
            foreach (var j in coda)
            {
                bool errore = j.Stato == StatoJob.Errore;
                var testo = new VerticalStackLayout { Spacing = 2 };
                testo.Add(Ui.Testo(j.Descrizione, 15, "#111111", true));
                testo.Add(Ui.Testo($"{j.Creato:HH:mm}  •  {j.Stampante} ({j.IP}:{j.Porta})  •  tentativi {j.Tentativi}", 12, "#607D8B"));
                if (!string.IsNullOrEmpty(j.UltimoErrore)) testo.Add(Ui.Testo($"⚠️ {j.UltimoErrore}", 12, "#C62828"));
                var elimina = Ui.Tasto("🗑️", "#F44336", fontSize: 18, altezza: 48);
                elimina.WidthRequest = 56;
                long id = j.Id;
                string descr = j.Descrizione;
                elimina.Clicked += async (s, e) =>
                {
                    if (!await MessaggioBox.Conferma("ELIMINA STAMPA", $"{descr}\n\nLa stampa NON uscirà più.\nAvvisa a voce il reparto.")) return;
                    ContestoCassa.Tavoli.EliminaJob(id);
                    Carica();
                };
                var riga = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
                riga.Add(testo, 0, 0);
                riga.Add(elimina, 1, 0);
                _lista.Add(Ui.Riquadro(riga, errore ? "#FFEBEE" : "#FFFFFF", 8, new Thickness(12, 8)));
            }
        }

        public static Task Mostra() => Dialogo.Mostra(new CodaStampaBox());
    }
}
