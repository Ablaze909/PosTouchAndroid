using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Stampa
{
    /// <summary>
    /// Conto non fiscale su termica 80 mm: stessa impaginazione di StampaContoAsync del PC
    /// (intestazione, righe, subtotale/sconto, TOTALE EURO, contanti/resto, data e ora, taglio).
    /// </summary>
    public static class LayoutConto
    {
        private static readonly CultureInfo It = new CultureInfo("it-IT");
        private static string E(decimal v) => v.ToString("F2", It);

        public static byte[] Crea(IList<RigaScontrino> righe, int numero, string intestazione1, string intestazione2,
                                  string intestazione3, bool accorpa, DateTime quando, decimal versato = 0, decimal resto = 0,
                                  bool stampaNumero = false, bool varianti = false, int eliminacode = 0)
        {
            const int L = EscPos.Colonne;
            var p = new EscPos();

            p.Centrato(string.IsNullOrWhiteSpace(intestazione1) ? "CONTO" : intestazione1, true, true);
            if (!string.IsNullOrWhiteSpace(intestazione2)) p.Centrato(intestazione2, false, false);
            if (!string.IsNullOrWhiteSpace(intestazione3)) p.Centrato(intestazione3, false, false);
            p.Linea().Vuota();

            var prodotti = righe.Where(r => r.Tipo == TipoRiga.Prodotto).ToList();
            var daStampare = accorpa ? Accorpa(prodotti) : Esplodi(prodotti);

            p.Sinistra();
            foreach (var r in daStampare)
            {
                string qta = r.IsAPeso ? r.Quantita.ToString("F3", It) : r.Quantita.ToString("F0", It);
                string unita = r.IsAPeso ? $" {r.UnitaPeso}" : "x";
                string prezzo = E(r.ImportoEuro);
                string desc = r.Descrizione ?? "";
                int maxDesc = L - qta.Length - unita.Length - 1 - prezzo.Length - 1;
                if (desc.Length > maxDesc) desc = desc.Substring(0, Math.Max(maxDesc - 3, 1)) + "...";
                p.Testo(EscPos.DueColonne($"{qta}{unita} {desc}", prezzo));
                if (varianti && !string.IsNullOrEmpty(r.VariantiTesto))
                    foreach (var vt in r.VariantiTesto.Split('|'))
                        if (!string.IsNullOrWhiteSpace(vt)) p.Testo($"    {vt.Trim()}");
            }

            var sconti = righe.Where(r => r.Tipo == TipoRiga.Sconto).ToList();
            if (sconti.Count > 0)
            {
                p.Testo(new string('-', L));
                int subtot = prodotti.Sum(r => r.ImportoCentesimi);
                p.Grassetto(true).Testo(EscPos.DueColonne("SUBTOTALE", E(subtot / 100m))).Grassetto(false);
                foreach (var s in sconti)
                    p.Grassetto(true).Testo(EscPos.DueColonne(s.Descrizione.Replace("€", "euro"), E(s.ImportoCentesimi / 100m))).Grassetto(false);
            }

            decimal totale = righe.Where(r => r.ContaNelTotale).Sum(r => r.ImportoCentesimi) / 100m;
            p.Linea().Vuota();
            p.DoppiaAltezza().Grassetto(true)
             .Testo(EscPos.DueColonne("TOTALE EURO", E(totale), 24))
             .Grassetto(false).Normale();

            if (versato > 0 && resto >= 0)
            {
                p.Vuota();
                p.Testo(EscPos.DueColonne("CONTANTI", E(versato)));
                p.Grassetto(true).Testo(EscPos.DueColonne("RESTO", E(resto))).Grassetto(false);
            }

            p.Vuota().Linea().Vuota();
            if (stampaNumero) p.Centrato($"Conto N. {numero}", true, false);
            if (eliminacode > 0)   // comanda da banco: numero da consegnare al cliente
            {
                p.Centrato("*** BANCO - ELIMINACODE ***", true, false);
                p.Centro().Stile(8).Testo($"N. {eliminacode:D3}").ResetStile().Sinistra();
            }
            p.Centrato(quando.ToString("dd/MM/yyyy - HH:mm:ss", It), false, false);
            return p.Byte();   // salto righe e taglio: Finitura (parametri preconti)
        }

        /// <summary>Stampa di prova dalle impostazioni.</summary>
        public static byte[] Prova(string intestazione1, string ip, int porta)
        {
            var p = new EscPos();
            p.Centrato(string.IsNullOrWhiteSpace(intestazione1) ? "POS TOUCH" : intestazione1, true, true);
            p.Linea().Centrato("STAMPA DI PROVA", true, false).Linea().Vuota();
            p.Testo(EscPos.DueColonne("Stampante", $"{ip}:{porta}"));
            p.Testo(EscPos.DueColonne("Accentate", "à è é ì ò ù"));
            p.Testo(EscPos.DueColonne("Allineamento", "1234,56"));
            p.Vuota().Centrato(DateTime.Now.ToString("dd/MM/yyyy - HH:mm:ss", It), false, false);
            return p.Byte();
        }

        // Accorpa: stesso articolo e stesso prezzo in una riga (pesi esclusi), come il PC
        private static List<RigaScontrino> Accorpa(List<RigaScontrino> righe)
        {
            var r = new List<RigaScontrino>();
            foreach (var x in righe)
            {
                var es = !x.IsAPeso && x.ProdottoId.HasValue && string.IsNullOrEmpty(x.VariantiTesto)
                    ? r.FirstOrDefault(y => !y.IsAPeso && y.ProdottoId == x.ProdottoId && y.PrezzoCentesimi == x.PrezzoCentesimi && string.IsNullOrEmpty(y.VariantiTesto))
                    : null;
                if (es != null) es.Quantita += x.Quantita;
                else r.Add(x.Copia());
            }
            return r;
        }

        // Esplodi: una riga per pezzo (pesi e quantità decimali restano intere)
        private static List<RigaScontrino> Esplodi(List<RigaScontrino> righe)
        {
            var r = new List<RigaScontrino>();
            foreach (var x in righe)
            {
                if (!x.IsAPeso && x.Quantita > 1 && x.Quantita == Math.Floor(x.Quantita))
                    for (int i = 0; i < (int)x.Quantita; i++) { var c = x.Copia(); c.Quantita = 1; r.Add(c); }
                else r.Add(x.Copia());
            }
            return r;
        }
    }
}
