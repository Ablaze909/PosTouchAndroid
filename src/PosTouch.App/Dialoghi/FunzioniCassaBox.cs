using PosTouch.App.Controlli;
using PosTouch.App.Servizi;
using PosTouch.Core.Cassa;

namespace PosTouch.App.Dialoghi
{
    /// <summary>Copia di FunzioniCassaDialog del PC (500×600, sfondo #ECEFF1), aperta sopra la schermata.</summary>
    public class FunzioniCassaBox : DialogoBase<bool>
    {
        private readonly Grid _attesa;
        private FunzioniCassa Funzioni => ContestoCassa.FunzioniCassa;

        public FunzioniCassaBox()
        {
            var lista = new VerticalStackLayout();
            lista.Add(Sezione("📊 RAPPORTI FISCALI", 0));
            lista.Add(Tasto("✅ CHIUSURA FISCALE (Z)", "#4CAF50", async () =>
            {
                if (await MessaggioBox.Conferma("CHIUSURA FISCALE", "Eseguire la chiusura fiscale giornaliera (Z)?\n\nL'operazione non può essere annullata."))
                    await EseguiAsync(Funzioni.ChiusuraFiscaleAsync, dopoOk: () =>
                    {
                        try { ContestoCassa.Tavoli.AzzeraEliminacode(); }   // come il PC: l'eliminacode riparte da 1
                        catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Warning($"Azzera eliminacode: {ex.Message}"); }
                    });
            }));
            lista.Add(Tasto("📋 LETTURA GIORNALIERA (X)", "#2196F3", () => EseguiAsync(Funzioni.LetturaGiornalieraAsync)));
            lista.Add(Tasto("📊 RAPPORTO REPARTI (Lettura)", "#2196F3", () => EseguiAsync(Funzioni.RapportoRepartiAsync)));
            lista.Add(Sezione("🖨️ RISTAMPE", 20));
            lista.Add(Tasto("📄 ULTIMO SCONTRINO", "#2196F3", () => EseguiAsync(Funzioni.RistampaUltimoAsync)));
            lista.Add(Tasto("📄 PENULTIMO SCONTRINO", "#2196F3", () => EseguiAsync(Funzioni.RistampaPenultimoAsync)));
            lista.Add(Sezione("🔧 MANUTENZIONE RT", 20));
            lista.Add(Tasto("🔄 RESET RT (Sblocca scontrino aperto)", "#F44336", async () =>
            {
                if (await MessaggioBox.Domanda("RESET RT", "Annullare l'eventuale scontrino rimasto aperto sul registratore?"))
                    await EseguiAsync(Funzioni.SbloccaRTAsync);
            }));

            var chiudi = Tasto("CHIUDI", "#607D8B", () => { Chiudi(true); return Task.CompletedTask; });
            chiudi.Margin = new Thickness(10, 20, 10, 0);

            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(20) };
            corpo.Add(new Label
            {
                Text = "FUNZIONI FISCALI E GESTIONE CASSA", FontSize = 20, FontAttributes = FontAttributes.Bold,
                TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 20)
            }, 0, 0);
            corpo.Add(new ScrollView { Content = lista }, 0, 1);
            corpo.Add(chiudi, 0, 2);

            _attesa = new Grid { BackgroundColor = Ui.C("#99000000"), IsVisible = false };
            _attesa.Add(new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Spacing = 12,
                Children =
                {
                    new ActivityIndicator { IsRunning = true, Color = Colors.White, WidthRequest = 60, HeightRequest = 60 },
                    new Label { Text = "⏳ Operazione in corso...", TextColor = Colors.White, FontSize = 20 }
                }
            });

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#ECEFF1"), StrokeThickness = 0,
                Content = new Grid { Children = { corpo, _attesa } }
            };
            Content = riquadro;
            Dimensiona(riquadro, 500, 680);
        }

        private static Label Sezione(string testo, double sopra) => new Label
        {
            Text = testo, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"),
            Margin = new Thickness(10, sopra, 0, 10)
        };

        /// <summary>FunctionButton del PC: blu, angoli 8, testo 16 grassetto.</summary>
        private static Button Tasto(string testo, string colore, Func<Task> azione)
        {
            var b = Ui.Tasto(testo, colore, "#FFFFFF", 16, 8, async (s, e) => await azione(), 64);
            b.Margin = new Thickness(10);
            return b;
        }

        /// <summary>Durante un'operazione sull'RT il tasto indietro non chiude.</summary>
        public override void Indietro()
        {
            if (!_attesa.IsVisible) Chiudi(true);
        }

        private async Task EseguiAsync(Func<Task<EsitoOperazione>> operazione, Action dopoOk = null)
        {
            if (_attesa.IsVisible) return; // un'operazione alla volta
            _attesa.IsVisible = true;
            EsitoOperazione esito;
            try
            {
                esito = await Task.Run(operazione);
            }
            finally
            {
                _attesa.IsVisible = false;
            }
            if (esito.Ok) dopoOk?.Invoke();
            if (esito.Ok) await MessaggioBox.Successo("OPERAZIONE COMPLETATA", esito.Messaggio);
            else await MessaggioBox.Errore("ERRORE REGISTRATORE", esito.Messaggio);
        }

        public static Task Mostra() => Dialogo.Mostra(new FunzioniCassaBox());
    }
}
