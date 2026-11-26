using System.Collections.Generic;
using PosTouch.Core.Configurazione;

namespace PosTouch.Core.Stampa
{
    /// <summary>
    /// Applica a una stampa i parametri della sua uscita (scheda "Configura" come POSBY):
    /// stampa capovolta, righe di salto iniziali e finali, cicalino e taglio carta.
    /// I layout producono solo il contenuto (INIT + testo), la finitura la decide la configurazione.
    /// </summary>
    public static class Finitura
    {
        private static readonly byte[] Init = { 0x1B, 0x40 };
        private static readonly byte[] Capovolta = { 0x1B, 0x7B, 0x01 };
        private static readonly byte[] TaglioTotale = { 0x1D, 0x56, 0x41, 0x00 };
        private static readonly byte[] TaglioParziale = { 0x1D, 0x56, 0x42, 0x00 };

        /// <param name="tipoCicalino">Comando beep della stampante (1=BEL, 2=Epson ESC(A, 3=ESC B, 4=DLE DC4), dalle opzioni comande.</param>
        public static byte[] Applica(byte[] contenuto, ParametriStampa p, int tipoCicalino)
        {
            p ??= new ParametriStampa();
            var b = new List<byte>(Init);
            if (p.Inversione) b.AddRange(Capovolta);
            for (int i = 0; i < p.RigheSaltoIniziale; i++) b.Add(0x0A);

            int inizio = contenuto != null && contenuto.Length >= 2 && contenuto[0] == 0x1B && contenuto[1] == 0x40 ? 2 : 0;   // l'INIT c'è già
            if (contenuto != null) for (int i = inizio; i < contenuto.Length; i++) b.Add(contenuto[i]);

            for (int i = 0; i < p.RigheSaltoFinale; i++) b.Add(0x0A);
            if (p.Cicalino > 0)
            {
                var beep = new EscPos().Cicalino(tipoCicalino > 0 ? tipoCicalino : 1).Byte();   // EscPos inizia con INIT: lo salto
                for (int n = 0; n < p.Cicalino; n++) for (int i = 2; i < beep.Length; i++) b.Add(beep[i]);
            }
            if (p.Taglio == 2) b.AddRange(TaglioTotale);
            else if (p.Taglio == 1) b.AddRange(TaglioParziale);
            return b.ToArray();
        }
    }
}
