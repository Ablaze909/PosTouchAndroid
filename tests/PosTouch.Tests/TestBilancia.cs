using PosTouch.Core.Vendita;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    public static class TestBilancia
    {
        public static void Esegui()
        {
            string ean = BarcodeBilancia.Genera("00011", 1234);
            Uguale(13, ean.Length, "etichetta EAN-13");
            var l = BarcodeBilancia.Leggi(ean);
            Ok(l.IsBilancia && l.CheckValido, "riconosciuta come etichetta bilancia valida");
            Uguale("00011", l.CodiceProdotto, "codice articolo");
            Uguale(1234, l.PrezzoCentesimi, "prezzo dall'etichetta");
            var errata = ean[..12] + (ean[12] == '0' ? "1" : "0");
            Ok(!BarcodeBilancia.Leggi(errata).CheckValido, "cifra di controllo sbagliata rifiutata");
        }
    }
}
