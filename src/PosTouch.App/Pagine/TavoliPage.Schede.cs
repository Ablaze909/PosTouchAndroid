using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Sale;

namespace PosTouch.App.Pagine
{
    public partial class TavoliPage
    {
        // ══════════════ barra schede (CostruisciTabBar del PC) ══════════════

        private void CostruisciSchede()
        {
            _schede.Children.Clear();
            _schede.ColumnDefinitions.Clear();
            var schede = new List<Button>();
            int i = 0;
            foreach (var sala in Sale())
            {
                bool attiva = sala.Id == _salaCorrente;
                string colore = sala.Id == ConfigSale.SalaConti ? ColoreConti : ColoriSale[i++ % ColoriSale.Length];
                bool chiaro = attiva;   // stessi colori della banda "dove sono" in vendita
                int id = sala.Id;
                schede.Add(Scheda(sala.Id == ConfigSale.SalaConti ? "CONTI" : sala.Nome, attiva ? colore : "#E0E0E0", chiaro,
                                  () => { _salaCorrente = _ultimaSala = id; Aggiorna(); }));
            }
            schede.Add(Scheda("Banco", "#E0E0E0", false, async () => await Navigazione.Chiudi(this)));
            if (Cfg.AbilitaComande)
            {
                int n = ContestoCassa.Spooler.InErrore;
                bool deviata = ContestoCassa.Spooler.Deviazioni().Count > 0;
                _coda = Scheda((n > 0 ? $"🖨️ CODA ({n})" : "🖨️ CODA") + (deviata ? " 🔀" : ""), n > 0 ? "#C62828" : "#37474F", true,
                               async () => { await CodaStampaBox.Mostra(); Aggiorna(); });
                schede.Add(_coda);
            }
            for (int k = 0; k < schede.Count; k++)
            {
                _schede.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _schede.Add(schede[k], k, 0);
            }
        }

        private static Button Scheda(string testo, string sfondo, bool testoChiaro, Action clic)
        {
            var b = Ui.Tasto(testo, sfondo, testoChiaro ? "#FFFFFF" : "#424242", 16, 0, altezza: 48);
            b.Padding = new Thickness(4, 0);
            b.LineBreakMode = LineBreakMode.TailTruncation;
            b.Clicked += (s, e) => clic();
            return b;
        }

        /// <summary>Le sale (se abilitate) e poi i CONTI clienti (sala 0), come la barra del PC.</summary>
        private List<Sala> Sale()
        {
            var l = Cfg.AbilitaSale ? Cfg.Sale.ToList() : new List<Sala>();
            if (Cfg.AbilitaContiClienti)
                l.Add(new Sala { Id = ConfigSale.SalaConti, Nome = "CONTI", NumeroTavoli = Math.Clamp(Cfg.NumeroSlotContiClienti, 1, 200), NumeroIniziale = 1 });
            return l;
        }
    }
}
