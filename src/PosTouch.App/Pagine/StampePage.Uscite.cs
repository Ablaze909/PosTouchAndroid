using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Pagine
{
    public partial class StampePage
    {
        // ══════════════ uscite ══════════════

        private async Task AggiungiUscitaAsync()
        {
            var liberi = Enumerable.Range(1, ConfigStampe.MaxUscite).Where(n => _c.UscitaDa(n) == null).ToList();
            if (liberi.Count == 0) return;
            // prima un numero che nessuna categoria/articolo usa ancora: un'uscita eliminata non "rinasce" con le vecchie categorie
            int numero = liberi.FirstOrDefault(n => UsataDa(n) == 0);
            if (numero == 0)
            {
                numero = liberi[0];
                if (!await MessaggioBox.Conferma("USCITA GIÀ ASSEGNATA",
                        $"L'uscita {numero} è ancora assegnata a {UsataDa(numero)} tra categorie e articoli: la nuova uscita riceverà le loro comande.\n\nContinuare?")) return;
            }
            string nome = await TestoBox.Chiedi($"NOME USCITA {numero} (es. BAR, CUCINA)");
            if (string.IsNullOrWhiteSpace(nome)) return;
            _c.Uscite.Add(new Uscita { Numero = numero, Nome = nome.Trim().ToUpperInvariant() });
            _c.Uscite = _c.Uscite.OrderBy(u => u.Numero).ToList();
            Disegna();
        }

        /// <summary>✏️ Scheda uscita: nome ed esclusioni sulle comande. Si modifica l'uscita in elenco solo con OK.</summary>
        private async Task RinominaAsync(Uscita u)
        {
            var nuova = await UscitaBox.Modifica(u);
            if (nuova == null) return;
            u.Nome = nuova.Nome;
            u.EscludiAltre = nuova.EscludiAltre;
            u.EscludiInAltre = nuova.EscludiInAltre;
            Disegna();
        }

        private async Task EliminaUscitaAsync(Uscita u)
        {
            int usata = UsataDa(u.Numero);
            string avviso = usata > 0 ? $"\n\n⚠️ È usata da {usata} tra categorie e articoli: non andranno più in comanda finché non le assegni a un'altra uscita." : "";
            if (!await MessaggioBox.Conferma("ELIMINA USCITA", $"Eliminare l'uscita {u.Numero} {u.Nome}?{avviso}")) return;
            _c.Uscite.Remove(u);
            _c.Base.Uscite.Remove(u.Numero);
            foreach (var p in _c.PerSala.Values) p.Uscite.Remove(u.Numero);
            Disegna();
        }

        private static int UsataDa(int numero) =>
            ContestoCassa.Catalogo.TutteCategorie.Count(c => c.CentroProduzioneId == numero)
          + ContestoCassa.Catalogo.TuttiProdotti.Count(p => p.CentroProduzioneId == numero);
    }
}
