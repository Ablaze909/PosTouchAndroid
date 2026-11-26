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
            // cassetto: fondo + incassi in contanti + versamenti − prelievi
            if (t.FondoInizialeCent is int fondo) p.Testo(EscPos.DueColonne("Fondo cassa iniziale", E(fondo)));
            foreach (var m in r.Movimenti)
                p.Testo(EscPos.DueColonne($"{(m.Tipo == TipoMovimento.Prelievo ? "Prelievo" : "Versamento")} {m.DataOra:HH:mm} {m.Causale}".Trim(),
                                          (m.Tipo == TipoMovimento.Prelievo ? "- " : "+ ") + E(m.ImportoCent)));
            p.Grassetto(true).Testo(EscPos.DueColonne("Contanti attesi", E(r.ContantiAttesiCent))).Grassetto(false);
            if (t.ContantiContatiCent is int contati)
            {
                p.Testo(EscPos.DueColonne("Contanti contati", E(contati)));
                int d = r.DifferenzaCent ?? 0;
                p.Grassetto(true).Testo(EscPos.DueColonne(d == 0 ? "Cassa in pari" : d > 0 ? "ECCEDENZA" : "AMMANCO", E(Math.Abs(d)))).Grassetto(false);
            }
            else p.Testo("Contanti non contati");
            if (t.FondoLasciatoCent is int lasciato)
            {
                p.Testo(EscPos.DueColonne("Fondo lasciato", E(lasciato)));
                if (r.DaVersareCent is int dv) p.Grassetto(true).Testo(EscPos.DueColonne("Da togliere dal cassetto", E(dv))).Grassetto(false);
            }
            p.Vuota().Centrato(DateTime.Now.ToString("dd/MM/yyyy - HH:mm:ss", It), false, false);
            return p.Byte();
        }
    }
}
