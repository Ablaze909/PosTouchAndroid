using System.ComponentModel;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>Riga della lista prodotti del Back office (con spunta di selezione).</summary>
    public class VoceProdotto : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public Prodotto P { get; init; }
        private bool _selezionato;
        public bool Selezionato
        {
            get => _selezionato;
            set
            {
                _selezionato = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Spunta)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sfondo)));
            }
        }
        public string Spunta => Selezionato ? "☑" : "☐";
        // colori del tema: col tema Scuro la riga resta scura (e la selezionata blu scuro), le scritte leggibili
        public string Sfondo => StilePagine.Scuro ? (Selezionato ? "#1E3A5F" : "#263238") : (Selezionato ? "#E3F2FD" : "#FFFFFF");
        public double Opacita => P.Attivo ? 1.0 : 0.5;
        public string Titolo => (P.IsPreferito ? "⭐ " : "") + P.Descrizione + (P.Attivo ? "" : "  (nascosto)");
        public string Iva => $"{P.AliquotaIVA}%";
    }

    /// <summary>Copia del Back office del PC: schede PRODOTTI e CATEGORIE.</summary>
    public partial class BackOfficePage : PaginaPos
    {
        private List<VoceProdotto> _voci = new();
        private List<Categoria> _filtro = new();
        private int CategoriaFiltro => FiltroCategoria != null && FiltroCategoria.Indice > 0 && FiltroCategoria.Indice < _filtro.Count
            ? _filtro[FiltroCategoria.Indice].Id : 0;
        private bool _soloPreferiti, _soloNascosti;
        private bool _schedaProdotti = true;
        private DateTime _toccoFreccia = DateTime.MinValue;

        public BackOfficePage()
        {
            InitializeComponent();
            MostraScheda(true);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SchedaOperatori.IsVisible = ContestoCassa.Impostazioni.AbilitaGestioneOperatori;
            Ricarica();
        }

        private bool? _listaStretta;

        /// <summary>Sotto i 1000 dp (8"): lista prodotti con meno colonne, così la descrizione si legge.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0) return;
            bool stretta = width < 1000;
            if (_listaStretta == stretta) return;
            _listaStretta = stretta;
            IntestMargine.IsVisible = IntestRep.IsVisible = IntestIVA.IsVisible = IntestBarcode.IsVisible = !stretta;
            IntestazioneProdotti.ColumnDefinitions = stretta
                ? new ColumnDefinitionCollection(new(new GridLength(40)), new(new GridLength(70)), new(GridLength.Star), new(new GridLength(90)), new(new GridLength(130)))
                : new ColumnDefinitionCollection(new(new GridLength(40)), new(new GridLength(80)), new(GridLength.Star), new(new GridLength(90)),
                                                 new(new GridLength(90)), new(new GridLength(150)), new(new GridLength(50)), new(new GridLength(55)), new(new GridLength(140)));
            Grid.SetColumn(IntestCategoria, stretta ? 4 : 5);
            ListaProdotti.ItemTemplate = (DataTemplate)Resources[stretta ? "ProdottoStretto" : "ProdottoLargo"];
        }

        private void Ricarica()
        {
            var cat = ContestoCassa.Catalogo;
            Contatori.Text = $"{cat.TuttiProdotti.Count} Prodotti • {cat.TutteCategorie.Count} Categorie";

            int idScelto = CategoriaFiltro;
            _filtro = new List<Categoria> { new Categoria { Id = 0, Nome = "Tutte" } };
            _filtro.AddRange(cat.TutteCategorie);
            FiltroCategoria.Voci = _filtro.Select(c => c.Nome).ToList();
            int i = _filtro.FindIndex(c => c.Id == idScelto);
            FiltroCategoria.Indice = i >= 0 ? i : 0;

            FiltraProdotti();
            CaricaCategorie();
        }

        // ══════════════ schede ══════════════

        private async void OnSchedaVarianti(object sender, TappedEventArgs e) => await Navigazione.Apri(this, () => new VariantiPage());

        /// <summary>👥 OPERATORI: creazione/modifica operatori, PIN e permessi. Anche chi ha il Back office deve dare il PIN amministratore.</summary>
        private async void OnSchedaOperatori(object sender, TappedEventArgs e)
        {
            try
            {
                if (!ContestoCassa.Impostazioni.AbilitaGestioneOperatori) return;
                var admin = await Accesso.VerificaAmministratoreAsync("GESTIONE OPERATORI");
                if (admin != null) await Navigazione.Apri(this, () => new OperatoriPage(admin));
            }
            catch (Exception ex)
            {
                PosTouch.Core.Diagnostica.LogManager.Error($"Apertura operatori: {ex}");
                await MessaggioBox.Errore("OPERATORI", ex.Message);
            }
        }

        private void OnSchedaProdotti(object sender, TappedEventArgs e) => MostraScheda(true);
        private void OnSchedaCategorie(object sender, TappedEventArgs e) => MostraScheda(false);

        private void MostraScheda(bool prodotti)
        {
            _schedaProdotti = prodotti;
            PannelloProdotti.IsVisible = prodotti;
            PannelloCategorie.IsVisible = !prodotti;
            SchedaProdotti.BackgroundColor = prodotti ? Ui.C("#2196F3") : StilePagine.FondoSpento;
            TestoSchedaProdotti.TextColor = prodotti ? Colors.White : StilePagine.TestoNormale;
            SchedaCategorie.BackgroundColor = prodotti ? StilePagine.FondoSpento : Ui.C("#2196F3");
            TestoSchedaCategorie.TextColor = prodotti ? StilePagine.TestoNormale : Colors.White;
        }

        private async void OnChiudi(object sender, EventArgs e) => await Navigazione.Chiudi(this);

    }
}
