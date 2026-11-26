using System.Linq;
using PosTouch.Core.Configurazione;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// Su quale stampante esce un centro di produzione: se per quella sala c'è un'eccezione
    /// (Sala × Centro → stampante, come POSBY) si usa quella, altrimenti la stampante predefinita del centro.
    /// </summary>
    public static class InstradamentoStampa
    {
        public static StampanteRete Risolvi(ConfigSale cfg, int salaId, int centroId)
        {
            var eccezione = cfg.StampantiSala.FirstOrDefault(x => x.SalaId == salaId && x.CentroId == centroId && x.StampanteId > 0);
            var st = eccezione != null ? cfg.StampanteDa(eccezione.StampanteId) : null;
            st ??= cfg.StampanteDa(cfg.CentroDa(centroId)?.StampanteId ?? 0);
            return st == null || string.IsNullOrWhiteSpace(st.IP) ? null : st;
        }
    }
}
