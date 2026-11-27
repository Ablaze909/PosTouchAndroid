using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Dati;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Altri barcode dell'articolo (confezioni): ognuno vale N pezzi in vendita. Si salvano con l'articolo
    /// (💾); i controlli definitivi (doppioni con altri articoli) li rifà il salvataggio.
    /// </summary>
    public partial class ProdottoPage
    {
        private void DisegnaBarcode()
        {
            ListaBarcodeAggiuntivi.Clear();
            if (_p.BarcodeAggiuntivi.Count == 0)
            {
                ListaBarcodeAggiuntivi.Add(new Label { Text = "Nessuno. Esempio: il barcode della cassa da 6 → 6 pezzi.", FontSize = 13, TextColor = Ui.C("#78909C") });
                return;
            }
            foreach (var b in _p.BarcodeAggiuntivi.ToList())
            {
                var riga = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(96)), new(new GridLength(48)) }, ColumnSpacing = 6 };
                riga.Add(new Border
                {
                    BackgroundColor = Ui.C("#FAFAFA"), Stroke = new SolidColorBrush(Ui.C("#E0E0E0")), StrokeThickness = 1, Padding = new Thickness(10, 0), HeightRequest = 44,
                    Content = new Label { Text = b.Barcode, FontSize = 16, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.MiddleTruncation }
                }, 0, 0);
                var pezzi = new Button { Text = $"×{b.Pezzi} pz", FontSize = 15, FontAttributes = FontAttributes.Bold, BackgroundColor = Ui.C("#2196F3"), TextColor = Colors.White, CornerRadius = 4, HeightRequest = 44 };
                pezzi.Clicked += async (s, e) =>
                {
                    int? n = await ChiediPezziAsync(b.Barcode, b.Pezzi);
                    if (n == null) return;
                    b.Pezzi = n.Value;
                    DisegnaBarcode();
                };
                var togli = new Button { Text = "🗑", FontSize = 18, BackgroundColor = Ui.C("#E53935"), TextColor = Colors.White, CornerRadius = 4, HeightRequest = 44 };
                togli.Clicked += async (s, e) =>
                {
                    if (!await MessaggioBox.Conferma("TOGLI BARCODE", $"Togliere il barcode {b.Barcode} da questo articolo?\n\n(diventa effettivo con 💾 SALVA)")) return;
                    _p.BarcodeAggiuntivi.Remove(b);
                    DisegnaBarcode();
                };
                riga.Add(pezzi, 1, 0);
                riga.Add(togli, 2, 0);
                ListaBarcodeAggiuntivi.Add(riga);
            }
        }

        private bool _aggiuntaInCorso;

        private async void OnAggiungiBarcode(object sender, EventArgs e)
        {
            if (_aggiuntaInCorso) return;
            _aggiuntaInCorso = true;
            try
            {
                string letto = await TestoBox.Chiedi("📦 BARCODE DELLA CONFEZIONE\nleggilo col lettore o scrivilo", "", GestioneCatalogo.LunghezzaMaxBarcode);
                if (letto == null) return;
                string codice = GestioneCatalogo.NormalizzaBarcode(letto);
                if (codice.Length == 0) return;
                if (codice == GestioneCatalogo.NormalizzaBarcode(CampoBarcode.Text) || _p.BarcodeAggiuntivi.Any(b => b.Barcode == codice))
                {
                    await MessaggioBox.Avviso("BARCODE GIÀ PRESENTE", $"Il barcode {codice} è già di questo articolo.");
                    return;
                }
                var (altro, _) = ContestoCassa.Catalogo.CercaBarcodeConQuantita(codice);
                if (altro != null && altro.Id != _p.Id)
                {
                    await MessaggioBox.Avviso("BARCODE GIÀ USATO", $"Il barcode {codice} è già dell'articolo \"{altro.Descrizione}\".");
                    return;
                }
                int? pezzi = await ChiediPezziAsync(codice, 6);
                if (pezzi == null) return;
                _p.BarcodeAggiuntivi.Add(new BarcodeConfezione { Barcode = codice, Pezzi = pezzi.Value });
                DisegnaBarcode();
            }
            finally { _aggiuntaInCorso = false; }
        }

        /// <summary>Pezzi della confezione: intero da 1 a 9999 (null = annullato).</summary>
        private static async Task<int?> ChiediPezziAsync(string codice, int attuali)
        {
            while (true)
            {
                var v = await TastierinoPrezzo.ChiediQuantita($"📦 PEZZI NELLA CONFEZIONE\n{codice}", attuali);
                if (v == null) return null;
                if (v.Value >= 1 && v.Value <= GestioneCatalogo.PezziMaxConfezione && v.Value == decimal.Truncate(v.Value)) return (int)v.Value;
                await MessaggioBox.Avviso("PEZZI NON VALIDI", $"Scrivi un numero intero da 1 a {GestioneCatalogo.PezziMaxConfezione}.");
            }
        }
    }
}
