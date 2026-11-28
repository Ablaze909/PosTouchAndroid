using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>Copia di CategoriaDialogTouch del PC.</summary>
    public partial class CategoriaPage : PaginaPos
    {
        /// <summary>Campi da scrivere anche in basso: la pagina si accorcia sopra la tastiera.</summary>
        protected override bool AccorciaConTastiera => true;

        private static readonly int[] Aliquote = { 4, 5, 10, 22 };
        private readonly Categoria _cat;
        private int _reparto;

        /// <param name="esistente">null = nuova categoria</param>
        /// <summary>Riquadro alle misure del PC, ristretto se lo schermo è più piccolo.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            _larghezza = width; _altezza = height;
            Ridimensiona();
        }

        private double _larghezza, _altezza;

        // tastiera aperta: il riquadro si accorcia (scorre dentro) e resta tutto sopra la tastiera, tasti compresi
        protected override void TastieraCambiata() => Ridimensiona();

        private void Ridimensiona()
        {
            if (_larghezza <= 0 || _altezza <= 0) return;
            Riquadro.WidthRequest = Math.Min(780, _larghezza - 24);
            Riquadro.HeightRequest = Math.Max(200, Math.Min(560, _altezza - SpazioTastiera - 24));
        }

        private List<PosTouch.Core.Configurazione.Uscita> _centri = new();

        public CategoriaPage(Categoria esistente)
        {
            InitializeComponent();
            _cat = esistente == null
                ? new Categoria { Colore = "#4CAF50", ColoreTesto = "#000000", AliquotaIVA = 10 }
                : new Categoria
                {
                    Id = esistente.Id, Nome = esistente.Nome, RepartoFiscale = esistente.RepartoFiscale, AliquotaIVA = esistente.AliquotaIVA,
                    Colore = esistente.Colore, ColoreTesto = esistente.ColoreTesto, Icona = esistente.Icona, Ordine = esistente.Ordine,
                    Attiva = esistente.Attiva, CentroProduzioneId = esistente.CentroProduzioneId, EscludiComandaBanco = esistente.EscludiComandaBanco,
                    Subito = esistente.Subito, FinePasto = esistente.FinePasto
                };

            Titolo.Text = esistente == null ? "➕ NUOVA CATEGORIA" : "✏️ MODIFICA CATEGORIA";
            PannelloAttiva.IsVisible = BtnElimina.IsVisible = esistente != null;
            SwAttiva.IsToggled = _cat.Attiva;
            CampoNome.Text = _cat.Nome;
            _reparto = _cat.RepartoFiscale;
            // centro comande: "Nessuna stampa" + centri configurati
            _centri = UsciteConAttuale(_cat.CentroProduzioneId);
            SceltaCentro.Voci = new[] { "🚫 Nessuna stampa" }.Concat(_centri.Select(c => c.Nome)).ToList();
            int ic = _centri.FindIndex(c => c.Numero == _cat.CentroProduzioneId);
            SceltaCentro.Indice = ic >= 0 ? ic + 1 : 0;
            ChkEscludiBanco.IsChecked = _cat.EscludiComandaBanco;
            RigaEscludiBanco.IsVisible = ContestoCassa.Impostazioni.Stampe?.ComandeDaBanco == true;
            ChkSubito.IsChecked = _cat.Subito;
            ChkFinePasto.IsChecked = _cat.FinePasto;
            RigaFinePasto.IsVisible = ContestoCassa.Impostazioni.Sale.AbilitaSale || _cat.FinePasto;
            RigaSubito.IsVisible = ContestoCassa.Impostazioni.Sale.AbilitaPortate || _cat.Subito;   // se già attivo resta visibile per poterlo togliere
            SceltaIva.Voci = Aliquote.Select(a => $"{a}%").ToList();
            int iva = Array.IndexOf(Aliquote, _cat.AliquotaIVA);
            SceltaIva.Indice = iva >= 0 ? iva : 2;
            AggiornaTutto();
        }

        private void AggiornaTutto()
        {
            TestoIcona.Text = string.IsNullOrWhiteSpace(_cat.Icona) ? "—" : _cat.Icona;
            TestoIcona.TextColor = StilePagine.TestoValore(!string.IsNullOrWhiteSpace(_cat.Icona));
            TestoReparto.Text = _reparto > 0 ? _reparto.ToString() : "Tocca per scegliere";
            TestoReparto.TextColor = StilePagine.TestoValore(_reparto > 0);
            TestoColore.Text = _cat.Colore;
            TestoColoreTesto.Text = _cat.ColoreTesto;
            AnteprimaColore.BackgroundColor = Ui.ColoreSicuro(_cat.Colore, "#4CAF50");
            AnteprimaColoreTesto.BackgroundColor = Ui.ColoreSicuro(_cat.ColoreTesto, "#000000");
            OnAnteprima(null, null);
        }

        private void OnAnteprima(object sender, EventArgs e)
        {
            AnteprimaTasto.BackgroundColor = Ui.ColoreSicuro(_cat.Colore, "#4CAF50");
            AnteprimaNome.TextColor = Ui.ColoreSicuro(_cat.ColoreTesto, "#000000");
            string nome = string.IsNullOrWhiteSpace(CampoNome.Text) ? "CATEGORIA" : CampoNome.Text.ToUpperInvariant();
            AnteprimaNome.Text = string.IsNullOrWhiteSpace(_cat.Icona) ? nome : $"{_cat.Icona} {nome}";
        }

        private void OnAttivaCambiata(object sender, ToggledEventArgs e)
        {
            TestoAttiva.Text = e.Value ? "ATTIVA" : "NASCOSTA";
            TestoAttiva.TextColor = Ui.C(e.Value ? "#4CAF50" : "#F44336");
        }

        private async void OnIcona(object sender, TappedEventArgs e)
        {
            var icone = SceltaBox.IconeCategorie.Select(i => i.Length == 0 ? "—" : i).ToList();
            int i = await SceltaBox.Icone("Icona categoria", icone);
            if (i < 0) return;
            _cat.Icona = SceltaBox.IconeCategorie[i];
            AggiornaTutto();
        }

        private async void OnReparto(object sender, TappedEventArgs e)
        {
            string r = await TastierinoPrezzo.ChiediCodice("🧾 REPARTO FISCALE (1-99)", _reparto > 0 ? _reparto.ToString() : null);
            if (r == null) return;
            if (int.TryParse(r, out int rep) && rep >= 1 && rep <= 99) _reparto = rep;
            else await MessaggioBox.Avviso("REPARTO NON VALIDO", "Il reparto deve essere un numero da 1 a 99.");
            AggiornaTutto();
        }

        private async void OnColore(object sender, TappedEventArgs e)
        {
            int i = await SceltaBox.Colori("Colore Sfondo", SceltaBox.Tavolozza);
            if (i < 0) return;
            _cat.Colore = SceltaBox.Tavolozza[i];
            AggiornaTutto();
        }

        private async void OnColoreTesto(object sender, TappedEventArgs e)
        {
            int i = await SceltaBox.Colori("Colore Testo", SceltaBox.Tavolozza);
            if (i < 0) return;
            _cat.ColoreTesto = SceltaBox.Tavolozza[i];
            AggiornaTutto();
        }

        /// <summary>
        /// Uscite di stampa (🖨️ STAMPE) per la scelta in categoria e articolo; se l'attuale non esiste più
        /// (es. catalogo del PC con un centro non impostato) resta in elenco, così salvando non si perde.
        /// </summary>
        internal static List<PosTouch.Core.Configurazione.Uscita> UsciteConAttuale(int attuale)
        {
            var st = ContestoCassa.Impostazioni.Stampe ?? new PosTouch.Core.Configurazione.ConfigStampe();
            var l = st.Uscite.Select(u => new PosTouch.Core.Configurazione.Uscita { Numero = u.Numero, Nome = $"{u.Numero} - {u.Nome}" }).ToList();
            if (attuale > 0 && l.All(u => u.Numero != attuale))
                l.Add(new PosTouch.Core.Configurazione.Uscita { Numero = attuale, Nome = $"{attuale} - (non impostata)" });
            return l;
        }

        private bool _salvataggio;

        /// <summary>SALVA protetto dal doppio tocco (un secondo INSERT creerebbe un duplicato).</summary>
        private async void OnSalva(object sender, EventArgs e)
        {
            if (_salvataggio) return;
            _salvataggio = true;
            try { await SalvaAsync(); }
            finally { _salvataggio = false; }
        }

        private async Task SalvaAsync()
        {
            await Ui.ChiudiTastieraAsync(this);   // consegna l'ultima parola scritta
            if (string.IsNullOrWhiteSpace(CampoNome.Text))
            {
                await MessaggioBox.Avviso("CAMPO OBBLIGATORIO", "Inserire il nome della categoria.");
                return;
            }
            if (_reparto < 1 || _reparto > 99)
            {
                await MessaggioBox.Avviso("CAMPO OBBLIGATORIO", "Scegliere il reparto fiscale (1-99).");
                return;
            }
            _cat.Nome = CampoNome.Text.Trim().ToUpperInvariant();
            _cat.RepartoFiscale = _reparto;
            _cat.AliquotaIVA = Aliquote[Math.Max(0, SceltaIva.Indice)];
            _cat.CentroProduzioneId = SceltaCentro.Indice > 0 ? _centri[SceltaCentro.Indice - 1].Numero : 0;
            _cat.EscludiComandaBanco = ChkEscludiBanco.IsChecked;
            _cat.Subito = ChkSubito.IsChecked;
            _cat.FinePasto = ChkFinePasto.IsChecked;
            _cat.Attiva = SwAttiva.IsToggled || _cat.Id == 0;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.SalvaCategoria(_cat));
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", ex.Message);
            }
        }

        private async void OnAnnulla(object sender, EventArgs e) => await Navigazione.Chiudi(this);

        private async void OnElimina(object sender, EventArgs e)
        {
            if (!await MessaggioBox.Conferma("🗑️ ELIMINA CATEGORIA", $"Eliminare la categoria {_cat.Nome}?")) return;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.EliminaCategoria(_cat.Id));
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex)
            {
                await MessaggioBox.Avviso("NON ELIMINABILE", ex.Message + "\n\nIn alternativa disattivala (NASCOSTA).");
            }
        }
    }
}
