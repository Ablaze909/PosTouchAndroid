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
        public static Task<bool> Apri(Page da, Func<Page> crea) => Esegui(() => da.Navigation.PushAsync(crea(), animated: false));

        /// <summary>Restituisce false se la pagina non è stata chiusa (un'altra navigazione era in corso o errore).</summary>
        public static Task<bool> Chiudi(Page pagina) => Esegui(() => pagina.Navigation.PopAsync(animated: false));

        /// <summary>Torna alla schermata iniziale (launcher), chiudendo tutte le pagine aperte.</summary>
        public static Task<bool> TornaAllInizio(Page pagina) => Esegui(() => pagina.Navigation.PopToRootAsync(animated: false));

        private static async Task<bool> Esegui(Func<Task> azione)
        {
            if (_occupato) return false;
            _occupato = true;
            try { await azione(); return true; }
            catch (Exception ex)
            {
                // prima solo nel log: toccavi il tasto e non succedeva nulla, senza capire perché
                PosTouch.Core.Diagnostica.LogManager.Error($"Navigazione: {ex}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try { await PosTouch.App.Dialoghi.MessaggioBox.Errore("PAGINA NON APERTA", $"La schermata non si è aperta:\n{ex.Message}\n\nRiprova; se si ripete riavvia l'app."); }
                    catch { }
                });
                return false;
            }
            finally { _occupato = false; }
        }
    }
}
