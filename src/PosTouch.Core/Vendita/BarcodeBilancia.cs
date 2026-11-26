namespace PosTouch.Core.Vendita
{
    /// <summary>Esito della lettura di un'etichetta bilancia.</summary>
    public readonly struct LetturaBilancia
    {
        public bool IsBilancia { get; init; }
        public bool CheckValido { get; init; }
        public string CodiceProdotto { get; init; }
        public int PrezzoCentesimi { get; init; }
    }

    /// <summary>
    /// Etichetta bilancia EAN-13 a prezzo variabile (BarcodeBilancia del PC): 2 CCCCC PPPPP K
    /// - cifra 1 = 2 (prefisso bilancia), cifre 2-6 = codice articolo, cifre 7-11 = prezzo in centesimi,
    ///   cifra 12 libera, cifra 13 = controllo EAN. Esempio 2001230125097 → articolo 00123, € 12,50.
    /// </summary>
    public static class BarcodeBilancia
    {
        public static bool IsBarcodeBilancia(string barcode)
        {
            if (string.IsNullOrEmpty(barcode) || barcode.Length != 13 || barcode[0] != '2') return false;
            foreach (char c in barcode) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>Cifra di controllo EAN-13 calcolata sulle prime 12 cifre.</summary>
        public static int CifraControllo(string dodiciCifre)
        {
            int somma = 0;
            for (int i = 0; i < 12; i++) somma += (dodiciCifre[i] - '0') * (i % 2 == 0 ? 1 : 3);
            return (10 - somma % 10) % 10;
        }

        public static LetturaBilancia Leggi(string barcode)
        {
            if (!IsBarcodeBilancia(barcode)) return new LetturaBilancia { IsBilancia = false };
            return new LetturaBilancia
            {
                IsBilancia = true,
                CodiceProdotto = barcode.Substring(1, 5),
                PrezzoCentesimi = int.Parse(barcode.Substring(6, 5)),
                CheckValido = CifraControllo(barcode) == barcode[12] - '0'
            };
        }

        /// <summary>Genera un'etichetta (per prove ed etichette di test).</summary>
        public static string Genera(string codiceProdotto, int prezzoCentesimi)
        {
            string cod = codiceProdotto.Length > 5 ? codiceProdotto.Substring(0, 5) : codiceProdotto.PadLeft(5, '0');
            string prezzo = prezzoCentesimi.ToString().PadLeft(5, '0');
            if (prezzo.Length > 5) prezzo = prezzo.Substring(0, 5);
            string baseCod = "2" + cod + prezzo + "0";   // 12ª cifra libera (alcune bilance ci mettono un controllo prezzo)
            return baseCod + CifraControllo(baseCod);
        }
    }
}
