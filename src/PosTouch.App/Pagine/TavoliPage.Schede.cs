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

        private string _firmaSchede;

        private void CostruisciSchede()
        {
            // ogni 10 s: si ridisegna solo se è cambiato qualcosa (sala attiva, sale, stampe in errore, deviazioni)
            int inErrore = Cfg.AbilitaComande ? ContestoCassa.Spooler.InErrore : 0;
            bool deviata = Cfg.AbilitaComande && ContestoCassa.Spooler.Deviazioni().Count > 0;
            // pallino: 🟢 almeno un tavolo/conto occupato, 🔴 tutti occupati, niente se la sala è libera
            List<TavoloAperto> aperti;
            try { aperti = ContestoCassa.Tavoli.Aperti(); } catch { aperti = new List<TavoloAperto>(); }
            var pallini = Sale().ToDictionary(x => x.Id, x => Pallino(x, aperti));
            string firma = string.Join("|", _salaCorrente, ContestoCassa.VersioneImpostazioni, Cfg.AbilitaComande, inErrore, deviata,
                                       string.Join(",", Sale().Select(x => x.Id + ":" + x.Nome + ":" + pallini[x.Id])));
            if (firma == _firmaSchede) return;
            _firmaSchede = firma;
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
                string nome = sala.Id == ConfigSale.SalaConti ? "CONTI" : sala.Nome;
                schede.Add(Scheda(pallini[sala.Id] + nome, attiva ? colore : "#E0E0E0", chiaro,
                                  () => { _salaCorrente = _ultimaSala = id; Aggiorna(); }));
            }
            schede.Add(Scheda("Banco", "#E0E0E0", false, async () => await Navigazione.Chiudi(this)));
            if (Cfg.AbilitaComande)
            {
                int n = inErrore;
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

        private static string Pallino(Sala sala, List<TavoloAperto> aperti)
        {
            int tot = Math.Max(1, sala.NumeroTavoli);
            int occupati = aperti.Count(t => t.SalaId == sala.Id && t.Occupato
                                             && t.Numero >= sala.NumeroIniziale && t.Numero < sala.NumeroIniziale + tot);
            return occupati == 0 ? "" : occupati >= tot ? "🔴 " : "🟢 ";
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
