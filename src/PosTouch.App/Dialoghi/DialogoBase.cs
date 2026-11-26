namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Finestra modale sopra la pagina (come i dialog ShowDialog() del PC): sfondo scuro semitrasparente,
    /// riquadro al centro, risultato restituito con <see cref="Risultato"/>.
    /// </summary>
    public abstract class DialogoBase<T> : ContentPage
    {
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>();
        private bool _chiuso;

        protected DialogoBase()
        {
            BackgroundColor = Color.FromArgb("#99000000");
            NavigationPage.SetHasNavigationBar(this, false);
        }

        public Task<T> Risultato => _tcs.Task;

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

        protected async void Chiudi(T valore)
        {
            if (_chiuso) return;
            _chiuso = true;
            try { await Navigation.PopModalAsync(false); } catch { }
            _tcs.TrySetResult(valore);
        }

        /// <summary>Tasto indietro di Android = ANNULLA.</summary>
        protected override bool OnBackButtonPressed()
        {
            Chiudi(default);
            return true;
        }
    }

    public static class Dialogo
    {
        public static async Task<T> Mostra<T>(DialogoBase<T> dialogo)
        {
            var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (nav == null) return default;
            await nav.PushModalAsync(dialogo, false);
            return await dialogo.Risultato;
        }
    }
}
