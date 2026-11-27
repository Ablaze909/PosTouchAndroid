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
            // stile comune anche per le finestre costruite alla vecchia maniera (colori del tema)
            DescendantAdded += (s, e) => { try { StileFinestre.Adatta(e.Element); } catch { } };
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
            if (riquadro is Border scheda) StileFinestre.StileScheda(scheda);   // scheda del tema (anche per le finestre vecchie)
            // misura giusta già al primo disegno (prima: grande come sul PC e poi ristretto → "si ingrandiva e si riduceva")
            var (w, h) = SpazioDisponibile();
            riquadro.WidthRequest = w > 0 ? Math.Min(larghezza, w - 24) : larghezza;
            if (altezza > 0) riquadro.HeightRequest = h > 0 ? Math.Min(altezza, h - 24) : altezza;
            else if (h > 0) riquadro.MaximumHeightRequest = h - 24;
        }

        /// <summary>Spazio della finestra (dp) meno la tastiera a schermo, se aperta; (0,0) se non si sa ancora.</summary>
        private static (double w, double h) SpazioDisponibile()
        {
            try
            {
                var f = Application.Current?.Windows.FirstOrDefault();
                if (f == null || f.Width <= 0 || f.Height <= 0) return (0, 0);
                return (f.Width, f.Height - Servizi.TastieraSchermo.Altezza);
            }
            catch { return (0, 0); }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (_riquadro == null || width <= 0 || height <= 0) return;
            // si riassegna solo se cambia davvero: niente ridisegni inutili
            double w = Math.Min(_larghezza, width - 24);
            if (Math.Abs(_riquadro.WidthRequest - w) > 0.5) _riquadro.WidthRequest = w;
            if (_altezza > 0)
            {
                double h = Math.Min(_altezza, height - 24);
                if (Math.Abs(_riquadro.HeightRequest - h) > 0.5) _riquadro.HeightRequest = h;
            }
            else if (Math.Abs(_riquadro.MaximumHeightRequest - (height - 24)) > 0.5) _riquadro.MaximumHeightRequest = height - 24;
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
