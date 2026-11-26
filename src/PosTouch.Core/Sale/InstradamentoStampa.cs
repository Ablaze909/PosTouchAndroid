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
            static bool Valida(StampanteRete s) => s != null && !string.IsNullOrWhiteSpace(s.IP);
            var eccezione = cfg.StampantiSala.FirstOrDefault(x => x.SalaId == salaId && x.CentroId == centroId && x.StampanteId > 0);
            var st = eccezione != null ? cfg.StampanteDa(eccezione.StampanteId) : null;
            if (!Valida(st)) st = cfg.StampanteDa(cfg.CentroDa(centroId)?.StampanteId ?? 0);   // eccezione mancante o senza IP
            return Valida(st) ? st : null;
        }
    }
}
