using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Dialogo come le finestre ShowDialog() del PC: sfondo scuro semitrasparente sopra la schermata,
    /// riquadro al centro, risultato restituito con <see cref="Risultato"/>.
    /// Viene mostrato come livello dentro la pagina (non come finestra Android separata),
    /// così lo schermo intero resta attivo.
    /// </summary>
    public abstract class DialogoBase<T> : ContentView
    {
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>();
        private bool _chiuso;
        internal PaginaPos Pagina { get; set; }
        internal Action AllaChiusura { get; set; }

        protected DialogoBase()
        {
            BackgroundColor = Color.FromArgb("#99000000");
            HorizontalOptions = LayoutOptions.Fill;
            VerticalOptions = LayoutOptions.Fill;
            // i tocchi sullo sfondo scuro non devono arrivare alla schermata sotto
            GestureRecognizers.Add(new TapGestureRecognizer());
        }

        public Task<T> Risultato => _tcs.Task;

        protected void Chiudi(T valore)
        {
            if (_chiuso) return;
            _chiuso = true;
            AllaChiusura?.Invoke();
            _tcs.TrySetResult(valore);
        }

        /// <summary>Tasto indietro di Android = ANNULLA.</summary>
        public virtual void Indietro() => Chiudi(default);

        /// <summary>
        /// true (default): un secondo dialogo dello stesso tipo mentre questo è aperto viene ignorato (doppio tocco).
        /// I messaggi (MessaggioBox) mettono false: non vanno MAI persi, si sovrappongono.
        /// </summary>
        public virtual bool UnicoPerTipo => true;

        private View _riquadro;
        private double _larghezza, _altezza;

        /// <summary>
        /// Misure di progetto del riquadro (quelle del PC). Se lo schermo è più piccolo
        /// il riquadro si restringe per restare sempre tutto visibile.
        /// </summary>
        protected void Dimensiona(View riquadro, double larghezza, double altezza = -1)
        {
            _riquadro = riquadro;
            _larghezza = larghezza;
            _altezza = altezza;
            riquadro.WidthRequest = larghezza;
            if (altezza > 0) riquadro.HeightRequest = altezza;
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (_riquadro == null || width <= 0 || height <= 0) return;
            _riquadro.WidthRequest = Math.Min(_larghezza, width - 24);
            if (_altezza > 0) _riquadro.HeightRequest = Math.Min(_altezza, height - 24);
            else _riquadro.MaximumHeightRequest = height - 24;
        }
    }

    public static class Dialogo
    {
        private static readonly List<Type> _aperti = new List<Type>();

        /// <summary>Pagina attualmente in primo piano (dove aprire il dialogo).</summary>
        private static PaginaPos PaginaAttiva()
        {
            var finestra = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (finestra == null) return null;
            var nav = finestra.Navigation;
            Page cima = nav.ModalStack.LastOrDefault() ?? nav.NavigationStack.LastOrDefault() ?? finestra;
            if (cima is NavigationPage np) cima = np.CurrentPage;
            return cima as PaginaPos;
        }

        /// <summary>
        /// Mostra il dialogo e attende la risposta. Un secondo dialogo dello stesso tipo chiesto
        /// mentre uno è già aperto (es. doppio tocco su TOTALE) viene ignorato.
        /// </summary>
        public static async Task<T> Mostra<T>(DialogoBase<T> dialogo)
        {
            var pagina = PaginaAttiva();
            var tipo = dialogo.GetType();
            if (pagina == null) return default;
            if (dialogo.UnicoPerTipo && _aperti.Contains(tipo)) return default;

            _aperti.Add(tipo);
            var indietroPrecedente = pagina.IndietroDialogo;
            dialogo.Pagina = pagina;
            Func<bool> indietroQuesto = null;
            indietroQuesto = () => { dialogo.Indietro(); return true; };
            pagina.IndietroDialogo = indietroQuesto;
            dialogo.AllaChiusura = () =>
            {
                pagina.RimuoviLivello(dialogo);
                // ripristina il tasto indietro solo se questo era il dialogo in cima
                if (pagina.IndietroDialogo == indietroQuesto) pagina.IndietroDialogo = indietroPrecedente;
            };
            pagina.AggiungiLivello(dialogo);
            try
            {
                return await dialogo.Risultato;
            }
            finally
            {
                _aperti.Remove(tipo);
            }
        }
    }
}
