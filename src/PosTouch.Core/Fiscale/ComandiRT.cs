namespace PosTouch.Core.Fiscale
{
    // Copia identica di Models.cs (POS Touch Windows): costanti protocollo 3i XON/XOFF.
    public static class ComandiRT
    {
        // Pagamenti (Tender)
        public const string PagamentoContanti = "1T";
        public const string PagamentoCarta = "3T";

        // Funzioni F
        public const string ChiusuraFiscale = "1F";
        public const string RapportoReparti = "2F";
        public const string StampaCopiaFiscale = "20F";
        public const string VerificaScontrino = "50F";
        public const string AnnulloScontrino = "52F";
        public const string StatoFiscale = "88F";
        public const string StatoRegistratore = "89F";
        public const string StampaUltimoScontrino = "91F";
        public const string StampaPenultimoScontrino = "92F";
        public const string StampaTerzultimoScontrino = "93F";
        public const string AbilitaNonFiscale = "901F";

        // Controllo
        public const string AperturaCassetto = "a";
        public const string ModoVendita = "c";
        public const string ModoLetture = "x";
        public const string ModoAzzeramenti = "z";
    }
}
