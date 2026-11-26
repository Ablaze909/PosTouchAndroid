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
