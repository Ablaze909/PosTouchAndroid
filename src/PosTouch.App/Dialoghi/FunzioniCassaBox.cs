using PosTouch.App.Controlli;
using PosTouch.App.Servizi;
using PosTouch.Core.Cassa;

namespace PosTouch.App.Dialoghi
{
    /// <summary>Funzioni fiscali e di cassa (Z, X, reparti, ristampe, reset RT) come elenco nello stile comune delle finestre.</summary>
    public class FunzioniCassaBox : DialogoBase<bool>
    {
        private readonly Grid _attesa;
        private FunzioniCassa Funzioni => ContestoCassa.FunzioniCassa;

        public FunzioniCassaBox()
        {
            var t = StileFinestre.T;
            var lista = new VerticalStackLayout { Spacing = 6 };
            lista.Add(Sezione("RAPPORTI FISCALI"));
            lista.Add(Voce(StileFinestre.IcoGrafico, null, "LETTURA GIORNALIERA (X)", "Totali del giorno, senza chiudere", () => EseguiAsync(Funzioni.LetturaGiornalieraAsync)));
            lista.Add(Voce(StileFinestre.IcoBarre, null, "RAPPORTO REPARTI", "Lettura per reparto", () => EseguiAsync(Funzioni.RapportoRepartiAsync)));
            // Z in fondo ai rapporti e in rosso: è l'unica che non si può annullare
            lista.Add(Voce(StileFinestre.IcoChiusura, Ui.C("#E53935"), "CHIUSURA FISCALE (Z)", "Chiusura giornaliera del registratore", async () =>
            {
                if (!await Accesso.VerificaAsync(PosTouch.Core.Modelli.Permesso.ChiusuraFiscale, "CHIUSURA FISCALE (Z)")) return;
                if (await MessaggioBox.Conferma("CHIUSURA FISCALE", "Eseguire la chiusura fiscale giornaliera (Z)?\n\nL'operazione non può essere annullata."))
                    await EseguiAsync(Funzioni.ChiusuraFiscaleAsync, dopoOk: () =>
                    {
                        try { ContestoCassa.Tavoli.AzzeraEliminacode(); }   // come il PC: l'eliminacode riparte da 1
                        catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Warning($"Azzera eliminacode: {ex.Message}"); }
                    });
            }));
            lista.Add(Sezione("RISTAMPE"));
            lista.Add(Voce(StileFinestre.IcoScontrino, null, "ULTIMO SCONTRINO", "Ristampa", () => EseguiAsync(Funzioni.RistampaUltimoAsync)));
            lista.Add(Voce(StileFinestre.IcoScontrino, null, "PENULTIMO SCONTRINO", "Ristampa", () => EseguiAsync(Funzioni.RistampaPenultimoAsync)));
            lista.Add(Sezione("MANUTENZIONE RT"));
            lista.Add(Voce(StileFinestre.IcoRiavvia, Ui.C("#FB8C00"), "RESET RT", "Sblocca uno scontrino rimasto aperto", async () =>
            {
                if (await MessaggioBox.Domanda("RESET RT", "Annullare l'eventuale scontrino rimasto aperto sul registratore?"))
                    await EseguiAsync(Funzioni.SbloccaRTAsync);
            }));

            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 12 };
            corpo.Add(StileFinestre.Intestazione(StileFinestre.IcoContanti, "FUNZIONI CASSA", "Registratore fiscale e ristampe", () => Indietro()), 0, 0);
            corpo.Add(new ScrollView { Content = lista }, 0, 1);
            corpo.Add(StileFinestre.TastoSecondario("CHIUDI", (s, e) => Indietro()), 0, 2);

            _attesa = new Grid { BackgroundColor = Ui.C("#99000000"), IsVisible = false };
            _attesa.GestureRecognizers.Add(new TapGestureRecognizer());   // durante la chiusura Z i tocchi non passano a CHIUDI
            _attesa.Add(new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Spacing = 12,
                Children =
                {
                    new ActivityIndicator { IsRunning = true, Color = Colors.White, WidthRequest = 60, HeightRequest = 60 },
                    new Label { Text = "Operazione in corso...", TextColor = Colors.White, FontSize = 18 }
                }
            });

            var riquadro = StileFinestre.Scheda(new Grid { Children = { corpo, _attesa } });
            Content = riquadro;
            Dimensiona(riquadro, 460, 640);
        }

        private static Label Sezione(string testo) => new Label
        {
            Text = testo, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = StileFinestre.T.Muto, Margin = new Thickness(4, 8, 0, 0)
        };

        private static bool _tastoInCorso;

        /// <summary>Una riga dell'elenco; un tocco alla volta (un doppio tocco su CHIUSURA Z apriva due conferme).</summary>
        private static View Voce(string icona, Color colore, string titolo, string spiegazione, Func<Task> azione)
            => StileFinestre.RigaElenco(icona, colore, titolo, spiegazione, async () =>
            {
                if (_tastoInCorso) return;
                _tastoInCorso = true;
                try { await azione(); }
                finally { _tastoInCorso = false; }
            });

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
