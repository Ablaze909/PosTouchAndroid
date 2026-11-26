using PosTouch.Core.Configurazione;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// Su quale stampante esce un'uscita (centro di produzione): quella della pagina della sala se le
    /// impostazioni per sala sono attive (come POSBY), altrimenti quella della configurazione base.
    /// null = uscita inesistente o senza stampante.
    /// </summary>
    public static class InstradamentoStampa
    {
        public static ParametriStampa Risolvi(ConfigStampe cfg, int salaId, int uscita) => cfg?.StampanteUscita(salaId, uscita);
    }
}
