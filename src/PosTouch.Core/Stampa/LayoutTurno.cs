using System;
using System.Globalization;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Stampa
{
    /// <summary>Riepilogo di chiusura turno sulla stampante RIEPILOGHI (non fiscale).</summary>
    public static class LayoutTurno
    {
        private static readonly CultureInfo It = new CultureInfo("it-IT");
        private static string E(int cent) => "€ " + (cent / 100m).ToString("N2", It);

        public static byte[] Crea(RiepilogoTurno r, string intestazione)
        {
            var t = r.Turno;
            var p = new EscPos();
            p.Centrato(string.IsNullOrWhiteSpace(intestazione) ? "POS TOUCH" : intestazione, true, false);
            p.Linea().Centrato("CHIUSURA TURNO", true, true).Linea();
            p.Testo(EscPos.DueColonne("Operatore", t.OperatoreNome));
            p.Testo(EscPos.DueColonne("Apertura", t.Apertura.ToString("dd/MM/yyyy HH:mm", It)));
            p.Testo(EscPos.DueColonne("Chiusura", (t.Chiusura ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm", It)));
            p.Linea('-');
            p.Testo(EscPos.DueColonne($"Scontrini ({r.Scontrini})", E(r.TotaleScontriniCent)));
            p.Testo(EscPos.DueColonne("  di cui contanti", E(r.ContantiCent)));
            p.Testo(EscPos.DueColonne("  di cui carta", E(r.CartaCent)));
            if (r.Conti > 0) p.Testo(EscPos.DueColonne($"Conti non fiscali ({r.Conti})", E(r.TotaleContiCent)));
            p.Linea('-');
            if (t.ContantiContatiCent is int contati)
            {
                p.Testo(EscPos.DueColonne("Contanti contati", E(contati)));
                int d = r.DifferenzaCent ?? 0;
                p.Grassetto(true).Testo(EscPos.DueColonne(d == 0 ? "Cassa in pari" : d > 0 ? "ECCEDENZA" : "AMMANCO", E(Math.Abs(d)))).Grassetto(false);
            }
            else p.Testo("Contanti non contati");
            p.Vuota().Centrato(DateTime.Now.ToString("dd/MM/yyyy - HH:mm:ss", It), false, false);
            return p.Byte();
        }
    }
}
