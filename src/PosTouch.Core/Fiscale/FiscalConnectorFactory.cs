using PosTouch.Core.Diagnostica;

namespace PosTouch.Core.Fiscale
{
    /// <summary>Tipi di registratore telematico supportati (stessi valori della versione Windows).</summary>
    public enum TipoCassaFiscale
    {
        /// <summary>Protocollo 3i XON/XOFF (porta default 1723)</summary>
        RT3i = 0,
        /// <summary>Protocollo Epson Communication Protocol PDU (porta default 9100)</summary>
        Epson = 1
    }

    public static class FiscalConnectorFactory
    {
        public static IFiscalConnector Create(TipoCassaFiscale tipo, string ip, int porta)
        {
            switch (tipo)
            {
                case TipoCassaFiscale.Epson:
                    LogManager.Info($"Creazione connettore EPSON → {ip}:{porta}");
                    return new EpsonConnector(ip, porta);

                case TipoCassaFiscale.RT3i:
                default:
                    LogManager.Info($"Creazione connettore 3i → {ip}:{porta}");
                    return new RT3iConnector(ip, porta);
            }
        }

        public static int PortaDefault(TipoCassaFiscale tipo) => tipo switch
        {
            TipoCassaFiscale.Epson => 9100,
            _ => 1723
        };

        public static string NomeTipo(TipoCassaFiscale tipo) => tipo switch
        {
            TipoCassaFiscale.Epson => "Epson (Communication Protocol)",
            TipoCassaFiscale.RT3i => "3i (XON/XOFF)",
            _ => "Sconosciuto"
        };
    }
}
