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
        public string Sfondo => Selezionato ? "#E3F2FD" : "#FFFFFF";
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
            Ricarica();
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

        private void OnSchedaProdotti(object sender, TappedEventArgs e) => MostraScheda(true);
        private void OnSchedaCategorie(object sender, TappedEventArgs e) => MostraScheda(false);

        private void MostraScheda(bool prodotti)
        {
            _schedaProdotti = prodotti;
            PannelloProdotti.IsVisible = prodotti;
            PannelloCategorie.IsVisible = !prodotti;
            SchedaProdotti.BackgroundColor = Ui.C(prodotti ? "#2196F3" : "#FFFFFF");
            TestoSchedaProdotti.TextColor = Ui.C(prodotti ? "#FFFFFF" : "#333333");
            SchedaCategorie.BackgroundColor = Ui.C(prodotti ? "#FFFFFF" : "#2196F3");
            TestoSchedaCategorie.TextColor = Ui.C(prodotti ? "#333333" : "#FFFFFF");
        }

        private async void OnChiudi(object sender, EventArgs e) => await Navigazione.Chiudi(this);

    }
}
