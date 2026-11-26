namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Apertura/chiusura delle schermate con protezione dal doppio tocco:
    /// un tocco ripetuto mentre una schermata si sta aprendo non ne apre una seconda.
    /// </summary>
    public static class Navigazione
    {
        private static bool _occupato;

        public static Task Apri(Page da, Func<Page> crea) => Esegui(() => da.Navigation.PushAsync(crea()));

        public static Task Chiudi(Page pagina) => Esegui(() => pagina.Navigation.PopAsync());

        private static async Task Esegui(Func<Task> azione)
        {
            if (_occupato) return;
            _occupato = true;
            try { await azione(); }
            catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Error($"Navigazione: {ex.Message}"); }
            finally { _occupato = false; }
        }
    }
}
