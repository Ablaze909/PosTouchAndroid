namespace PosTouch.App.Controlli
{
    /// <summary>
    /// Pagina base di tutte le schermate. Il contenuto viene messo dentro una griglia "ospite"
    /// su cui i dialoghi (messaggi, tastierini, calcolo resto…) si aprono come livelli sovrapposti,
    /// come le finestre del PC. Niente finestre Android separate → lo schermo intero non si perde.
    /// </summary>
    public class PaginaPos : ContentPage
    {
        private Grid _ospite;
        private bool _incarto;
        private readonly List<View> _livelli = new List<View>();

        /// <summary>Gestore del tasto indietro del dialogo in cima (impostato dal dialogo stesso).</summary>
        internal Func<bool> IndietroDialogo { get; set; }

        public PaginaPos()
        {
            NavigationPage.SetHasNavigationBar(this, false);
        }

        protected override void OnPropertyChanged(string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            if (propertyName != nameof(Content) || _incarto) return;
            if (Content == null || ReferenceEquals(Content, _ospite)) return;

            // Avvolge il contenuto definito nello XAML nella griglia ospite (una sola volta, prima del disegno)
            _incarto = true;
            var originale = Content;
            _ospite = new Grid();
            Content = _ospite;
            _ospite.Add(originale);
            _incarto = false;
        }

        internal void AggiungiLivello(View livello)
        {
            if (_ospite == null) return;
            _livelli.Add(livello);
            _ospite.Add(livello);
        }

        internal void RimuoviLivello(View livello)
        {
            _livelli.Remove(livello);
            _ospite?.Remove(livello);
        }

        internal bool HaLivelli => _livelli.Count > 0;

        /// <summary>Tasto indietro: prima chiude il dialogo aperto, poi chiede alla pagina (<see cref="Indietro"/>).</summary>
        protected sealed override bool OnBackButtonPressed()
        {
            if (_livelli.Count > 0)
            {
                IndietroDialogo?.Invoke();
                return true;
            }
            return Indietro();
        }

        /// <summary>Comportamento del tasto indietro della pagina (predefinito: torna alla schermata precedente).</summary>
        protected virtual bool Indietro() => base.OnBackButtonPressed();
    }
}
