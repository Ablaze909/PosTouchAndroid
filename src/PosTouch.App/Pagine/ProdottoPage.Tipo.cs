using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    public partial class ProdottoPage
    {
        // ══════════════ tipo articolo (uno solo: standard / coperto / a peso / combinazione) ══════════════

        private enum TipoArticolo { Standard, Coperto, Peso, Combinazione }
        private TipoArticolo _tipo;

        private void OnTipoStandard(object s, EventArgs e) => ImpostaTipo(TipoArticolo.Standard);
        private void OnTipoCoperto(object s, EventArgs e) => ImpostaTipo(TipoArticolo.Coperto);
        private async void OnTipoPeso(object s, EventArgs e)
        {
            bool nuovo = _tipo != TipoArticolo.Peso;
            ImpostaTipo(TipoArticolo.Peso);
            if (nuovo) await OpzioniPesoAsync();
        }

        private async void OnTipoCombinazione(object s, EventArgs e)
        {
            bool nuovo = _tipo != TipoArticolo.Combinazione;
            ImpostaTipo(TipoArticolo.Combinazione);
            if (nuovo) await OpzioniCombinazioneAsync();
        }

        private async void OnOpzioniTipo(object s, EventArgs e)
        {
            if (_tipo == TipoArticolo.Peso) await OpzioniPesoAsync();
            else if (_tipo == TipoArticolo.Combinazione) await OpzioniCombinazioneAsync();
        }

        /// <summary>⚖️ Prezzo al Kg / Etto / g.</summary>
        private async Task OpzioniPesoAsync()
        {
            int i = await SceltaBox.Testi("⚖️ PREZZO AL…", Unita.Select(u => $"Prezzo al {u}").ToList(), 3);
            if (i >= 0) SceltaUnita.Indice = i;
            AggiornaRiepilogoTipo();
        }

        /// <summary>🍕 Numero di gusti, regola del prezzo, varianti proporzionali (come il PC).</summary>
        private async Task OpzioniCombinazioneAsync()
        {
            int g = await SceltaBox.Testi("🍕 QUANTI GUSTI?", Gusti.Select(x => $"{x} gusti").ToList(), 5);
            if (g < 0) { AggiornaRiepilogoTipo(); return; }
            SceltaGusti.Indice = g;
            int p = await SceltaBox.Testi("🍕 PREZZO DELLA COMBINAZIONE", Politiche, 1);
            if (p >= 0) SceltaPolitica.Indice = p;
            ChkProporzionali.IsChecked = await MessaggioBox.Domanda("🍕 VARIANTI PROPORZIONALI",
                "Il prezzo delle varianti va diviso per il numero di gusti?\n\n(es. +1,00 € di mozzarella su ½ pizza = +0,50 €)");
            AggiornaRiepilogoTipo();
        }

        private void AggiornaRiepilogoTipo()
        {
            RiepilogoTipo.Text = _tipo switch
            {
                TipoArticolo.Peso => $"Prezzo al {Unita[Math.Max(0, SceltaUnita.Indice)]}",
                TipoArticolo.Combinazione => $"{Gusti[Math.Max(0, SceltaGusti.Indice)]} gusti · {Politiche[Math.Max(0, SceltaPolitica.Indice)]}" +
                                             (ChkProporzionali.IsChecked ? " · varianti proporzionali" : ""),
                TipoArticolo.Coperto => "🍽️ Coperto (scegli l'articolo in SALE E COMANDE)",
                _ => "📦 Articolo normale"
            };
        }

        /// <summary>Accende il tasto del tipo scelto e mostra le sue opzioni (unità di peso / gusti e prezzo).</summary>
        private void ImpostaTipo(TipoArticolo t)
        {
            _tipo = t;
            void Colora(Button b, TipoArticolo x, string colore)
            {
                bool on = t == x;
                b.BackgroundColor = Color.FromArgb(on ? colore : "#ECEFF1");
                b.TextColor = Color.FromArgb(on ? "#FFFFFF" : "#546E7A");
            }
            Colora(BtnTipoStandard, TipoArticolo.Standard, "#546E7A");
            Colora(BtnTipoCoperto, TipoArticolo.Coperto, "#00897B");
            Colora(BtnTipoPeso, TipoArticolo.Peso, "#6D4C41");
            Colora(BtnTipoCombinazione, TipoArticolo.Combinazione, "#E65100");
            // le opzioni si scelgono in finestre (i pannelli che si aprono dentro la scheda non sempre si ridisegnano)
            PannelloPeso.IsVisible = false;
            PannelloCombinazione.IsVisible = false;
            // riga sempre visibile (niente cambi di altezza): il tasto si accende solo per peso e combinazione
            BtnOpzioniTipo.IsEnabled = t == TipoArticolo.Peso || t == TipoArticolo.Combinazione;
            BtnOpzioniTipo.Opacity = BtnOpzioniTipo.IsEnabled ? 1 : 0.3;
            AggiornaRiepilogoTipo();
            AiutoTipo.Text = t switch
            {
                TipoArticolo.Coperto => "Si sceglie come coperto della sala (SALE E COMANDE): viene chiesto all'apertura del tavolo.",
                TipoArticolo.Peso => "In vendita chiede il peso; il prezzo è per unità di peso.",
                TipoArticolo.Combinazione => "In vendita si scelgono i gusti (es. pizza ½ + ½); il prezzo segue la regola scelta.",
                _ => "Articolo normale."
            };
        }
    }
}
