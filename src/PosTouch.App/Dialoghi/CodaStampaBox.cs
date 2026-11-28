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
            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("🔄 RIPROVA TUTTE", "#1976D2", altezza: 56, clic: (s, e) => { ContestoCassa.Spooler.RiprovaTutto(); Chiudi(true); }), 0, 0);
            azioni.Add(Ui.Tasto("🔀 FORZA DEVIAZIONE", "#6A1B9A", altezza: 56, clic: async (s, e) => { await ForzaDeviazioneAsync(); Carica(); }), 1, 0);
            azioni.Add(Ui.Tasto("✖ CHIUDI", "#78909C", altezza: 56, clic: (s, e) => Chiudi(true)), 2, 0);

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
            // deviazioni attive (restano fino a quando non le togli)
            var deviazioni = ContestoCassa.Spooler.Deviazioni();
            if (deviazioni.Count > 0)
            {
                var testo = new VerticalStackLayout { Spacing = 2 };
                testo.Add(Ui.Testo("🔀 DEVIAZIONI ATTIVE", 15, "#6A1B9A", true));
                foreach (var d in deviazioni)
                    testo.Add(Ui.Testo($"{d.NomeOrigine} ({d.IPOrigine}) → {d.NomeDestinazione} ({d.IPDestinazione})   dal {d.Attivata:dd/MM HH:mm}", 13, "#4A148C"));
                var togli = Ui.Tasto("↩ RIPRISTINA", "#8E24AA", fontSize: 14, altezza: 48, clic: async (s, e) =>
                {
                    if (!await MessaggioBox.Conferma("RIPRISTINA STAMPANTI", "Togliere tutte le deviazioni?\nLe stampe torneranno alle stampanti normali.")) return;
                    ContestoCassa.Spooler.RimuoviDeviazioni();
                    Carica();
                });
                togli.Padding = new Thickness(12, 0);
                var riga = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
                riga.Add(testo, 0, 0); riga.Add(togli, 1, 0);
                _lista.Add(Ui.Riquadro(riga, "#F3E5F5", 8, new Thickness(12, 8)));
            }
            var coda = ContestoCassa.Tavoli.CodaDaStampare(true);
            if (coda.Count == 0)
            {
                _lista.Add(new Label { Text = "✅ Nessuna stampa in coda.", FontSize = 17, TextColor = Ui.C("#2E7D32"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 40) });
                return;
            }
            var stampantiFerme = new HashSet<string>();   // stampanti con una stampa in errore già vista (più vecchia)
            foreach (var j in coda)
            {
                bool errore = j.Stato == StatoJob.Errore;
                string chiaveSt = $"{j.IP}:{j.Porta}";
                bool dietro = !errore && stampantiFerme.Contains(chiaveSt);   // aspetta che esca quella in errore prima di lei
                if (errore) stampantiFerme.Add(chiaveSt);
                var testo = new VerticalStackLayout { Spacing = 2 };
                testo.Add(Ui.Testo(j.Descrizione, 15, "#111111", true));
                testo.Add(Ui.Testo($"{j.Creato:HH:mm}  •  {j.Stampante} ({j.IP}:{j.Porta})  •  tentativi {j.Tentativi}", 12, "#607D8B"));
                if (!string.IsNullOrEmpty(j.UltimoErrore)) testo.Add(Ui.Testo($"⚠️ {PosTouch.Core.Diagnostica.ErroriRete.Traduci(j.UltimoErrore)}", 12, "#C62828"));   // anche le stampe in coda da versioni vecchie
                if (dietro) testo.Add(Ui.Testo("⏸ In attesa: prima deve uscire la stampa in errore di questa stampante (🔁 le riprova insieme)", 12, "#E65100"));
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
                var devia = Ui.Tasto("🔀", "#6A1B9A", fontSize: 18, altezza: 48);
                devia.WidthRequest = 56;
                devia.Clicked += async (s, e) =>
                {
                    var st = await ScegliStampanteAsync($"🔀 DEVIA: {descr}");
                    if (st == null) return;
                    ContestoCassa.Spooler.DeviaJob(id, $"{st.Nome} (deviata)", st.IP, st.Porta);
                    Carica();
                };
                var riprova = Ui.Tasto("🔁", "#1976D2", fontSize: 18, altezza: 48);
                riprova.WidthRequest = 56;
                riprova.Clicked += (s, e) => { ContestoCassa.Spooler.RiprovaJob(id); Carica(); };   // solo questa stampa, subito
                var riga = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 10 };
                riga.Add(testo, 0, 0);
                riga.Add(riprova, 1, 0);
                riga.Add(devia, 2, 0);
                riga.Add(elimina, 3, 0);
                _lista.Add(Ui.Riquadro(riga, errore ? "#FFEBEE" : dietro ? "#FFF3E0" : "#FFFFFF", 8, new Thickness(12, 8)));
            }
        }

        private static async Task<PosTouch.Core.Configurazione.StampanteRete> ScegliStampanteAsync(string titolo)
        {
            var stampanti = (ContestoCassa.Impostazioni.Stampe ?? new PosTouch.Core.Configurazione.ConfigStampe()).StampantiConNome();
            if (stampanti.Count == 0) { await MessaggioBox.Avviso("STAMPANTI", "Nessuna stampante nell'elenco IP di IMPOSTAZIONI → 🖨️ STAMPE."); return null; }
            int i = await SceltaBox.Testi(titolo, stampanti.Select(s => s.Nome).ToList(), 2);
            return i < 0 ? null : stampanti[i];
        }

        /// <summary>🔀 FORZA DEVIAZIONE (come il PC): tutto ciò che va alla stampante A esce su B finché non la ripristini.</summary>
        private static async Task ForzaDeviazioneAsync()
        {
            var da = await ScegliStampanteAsync("🔀 Stampante GUASTA (da deviare)");
            if (da == null) return;
            var a = await ScegliStampanteAsync($"🔀 {da.Nome} → stampa su:");
            if (a == null) return;
            if (a.IP == da.IP && a.Porta == da.Porta) { await MessaggioBox.Avviso("DEVIAZIONE", "Scegli una stampante diversa."); return; }
            ContestoCassa.Spooler.AttivaDeviazione(new DeviazioneStampa
            {
                NomeOrigine = da.Nome, IPOrigine = da.IP, PortaOrigine = da.Porta,
                NomeDestinazione = a.Nome, IPDestinazione = a.IP, PortaDestinazione = a.Porta
            });
            await MessaggioBox.Successo("DEVIAZIONE ATTIVA", $"Le stampe per {da.Nome} escono su {a.Nome},\nanche quelle già in coda.\n\nRipristina da 🖨️ CODA quando la stampante è riparata.");
        }

        public static Task Mostra() => Dialogo.Mostra(new CodaStampaBox());
    }
}
