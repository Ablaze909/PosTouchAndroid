namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Apertura/chiusura delle schermate con protezione dal doppio tocco:
    /// un tocco ripetuto mentre una schermata si sta aprendo non ne apre una seconda.
    /// </summary>
    public static class Navigazione
    {
        private static bool _occupato;

        /// <summary>Restituisce false se la navigazione non è avvenuta (un'altra era in corso o errore).</summary>
        public static Task<bool> Apri(Page da, Func<Page> crea) => Esegui(() => da.Navigation.PushAsync(crea()));

        /// <summary>Restituisce false se la pagina non è stata chiusa (un'altra navigazione era in corso o errore).</summary>
        public static Task<bool> Chiudi(Page pagina) => Esegui(() => pagina.Navigation.PopAsync());

        private static async Task<bool> Esegui(Func<Task> azione)
        {
            if (_occupato) return false;
            _occupato = true;
            try { await azione(); return true; }
            catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Error($"Navigazione: {ex.Message}"); return false; }
            finally { _occupato = false; }
        }
    }
}
