using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;

namespace PosTouch.Core.Stampa
{
    /// <summary>Dati di una stampa per un centro di produzione.</summary>
    public class DatiComanda
    {
        public string NomeCentro { get; set; } = "COMANDA";
        public int CentroId { get; set; }
        public string NomeTavolo { get; set; } = "";
        public string NomeSala { get; set; } = "";
        public int Progressivo { get; set; }
        public int Coperti { get; set; }
        public bool Riordino { get; set; }
        /// <summary>Righe di questo centro.</summary>
        public List<RigaScontrino> Righe { get; set; } = new List<RigaScontrino>();
        /// <summary>Riepilogo degli altri centri (senza portate), nome centro → righe.</summary>
        public Dictionary<string, List<RigaScontrino>> AltriCentri { get; set; }
        /// <summary>Con le portate: tutte le righe nuove di tutti i centri con i separatori, nell'ordine di uscita.</summary>
        public List<RigaScontrino> FlussoPortate { get; set; }
        public string NomeSeparatore { get; set; } = "SEGUE";
        public DateTime Quando { get; set; } = DateTime.Now;
        /// <summary>Operatore del turno (vuoto = gestione operatori spenta).</summary>
        public string Operatore { get; set; } = OperatoreStampa.Corrente();
    }

    /// <summary>
    /// Comanda, RIORDINO (aggiunte), STORNO e VAI portata: stessa impaginazione di StampanteTermica del PC
    /// (StampaComandaAsync, StampaStornoComandaAsync, StampaVaiPortataAsync).
    /// </summary>
    public static class LayoutComanda
    {
        private const int L = EscPos.Colonne;
        private static readonly string Trattini = new string('-', L);
        private static readonly CultureInfo It = new CultureInfo("it-IT");

        private static string Qta(RigaScontrino r) => r.IsAPeso ? $"{r.Quantita.ToString("F3", It)} {r.UnitaPeso}" : $"{(int)r.Quantita}";
        private static int Pezzi(IEnumerable<RigaScontrino> righe) => righe.Where(r => r.Tipo == TipoRiga.Prodotto).Sum(r => r.IsAPeso ? 1 : (int)r.Quantita);
        private static string Tavolo(DatiComanda d) => (d.NomeTavolo ?? "").ToUpperInvariant();

        // ══════════════ COMANDA / RIORDINO ══════════════

        /// <summary>StampaVariantiComanda del PC: gusti/varianti sotto l'articolo; "*testo" = nota evidenziata.</summary>
        private static void Varianti(EscPos p, RigaScontrino r, int stileVariante)
        {
            if (string.IsNullOrEmpty(r.VariantiTesto)) return;
            foreach (var parte in r.VariantiTesto.Split('|'))
            {
                var t = parte.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("*")) p.Grassetto(true).Inverso(true).Testo($" *** {t.Substring(1).Trim()} *** ").Inverso(false).Grassetto(false);
                else p.Stile(stileVariante).Testo($" {t}").ResetStile();
            }
        }

        public static byte[] Comanda(DatiComanda d, int stileProdotto, int stileSeparatore, int cicalino, bool accorpa, int stileVariante = 5)
        {
            var p = new EscPos();
            p.Normale().Sinistra();
            p.Testo(EscPos.DueColonne(d.NomeCentro.ToUpperInvariant(), d.Quando.ToString("dd/MM/yy HH:mm", It)));
            string op = string.IsNullOrEmpty(d.Operatore) ? "" : $"Op: {d.Operatore}";
            if (d.Progressivo > 0 || op.Length > 0) p.Testo(EscPos.DueColonne(d.Progressivo > 0 ? $"Com.{d.Progressivo}" : "", op));

            if (d.Riordino)
            {
                p.Centro().Grassetto(true).DoppiaDimensione().Inverso(true)
                 .Testo(" *** RIORDINO *** ")
                 .Inverso(false).Normale().Grassetto(false).Sinistra();
            }

            string sx = d.Coperti > 0 ? $"Coperti {d.Coperti}" : "";
            string dx = string.IsNullOrWhiteSpace(d.NomeSala) ? "" : d.NomeSala.ToUpperInvariant();
            if (sx.Length > 0 || dx.Length > 0)
                p.Grassetto(true).DoppiaAltezza().Testo(EscPos.DueColonne(sx, dx)).Normale().Grassetto(false);

            p.Normale().Testo(Trattini);
            p.Centro().Grassetto(true).DoppiaDimensione().Testo(Tavolo(d)).Normale().Grassetto(false);
            p.Sinistra().Testo(Trattini);

            if (d.FlussoPortate != null && d.FlussoPortate.Count > 0)
            {
                // con le portate: separatori, articoli di questo centro grandi, degli altri centri piccoli
                foreach (var art in accorpa ? AccorpaFlusso(d.FlussoPortate) : Esplodi(d.FlussoPortate))
                {
                    if (art.Tipo == TipoRiga.Portata)
                    {
                        string nome = Portate.Nome(art).ToUpperInvariant();
                        if (nome.Contains("SUBITO"))
                        {
                            p.Normale().Sinistra().Grassetto(true)
                             .Testo("================================")
                             .DoppiaAltezza().Testo("     !! SUBITO !!").Normale()
                             .Testo("================================")
                             .Grassetto(false);
                        }
                        else
                        {
                            p.Stile(stileSeparatore).Centro().Testo("---------" + nome + "---------").ResetStile().Sinistra();
                        }
                    }
                    else if (art.CentroProduzioneId == d.CentroId)
                    {
                        p.Stile(stileProdotto).Testo($"{Qta(art)}  {art.Descrizione}").ResetStile();
                        Varianti(p, art, stileVariante);
                    }
                    else
                    {
                        p.Normale().Testo($"  {Qta(art)} {art.Descrizione}");
                    }
                }
            }
            else
            {
                foreach (var r in accorpa ? Accorpa(d.Righe) : Esplodi(d.Righe))
                {
                    p.Stile(stileProdotto).Testo($"{Qta(r)}  {r.Descrizione}").ResetStile();
                    Varianti(p, r, stileVariante);
                }

                if (d.AltriCentri != null && d.AltriCentri.Count > 0)
                {
                    p.Normale();
                    foreach (var altro in d.AltriCentri)
                        foreach (var r in accorpa ? Accorpa(altro.Value) : Esplodi(altro.Value))
                            p.Testo($"  {Qta(r)} {r.Descrizione}");
                }
            }

            p.Normale().Testo(Trattini);
            p.Grassetto(true).Testo($"Pezzi: {Pezzi(d.Righe)}").Grassetto(false);
            return p.Byte();   // salto righe, cicalino e taglio: Finitura (parametri dell'uscita)
        }

        // ══════════════ TICKET ELIMINACODE (comanda da banco) ══════════════

        /// <summary>StampaTicketEliminacodeAsync del PC: numero grande da consegnare al cliente.</summary>
        public static byte[] TicketEliminacode(int numero, string intestazione, DateTime quando)
        {
            var p = new EscPos();
            if (!string.IsNullOrWhiteSpace(intestazione)) p.Centrato(intestazione, true, false);
            p.Centro().Grassetto(true).Testo("IL TUO NUMERO").Grassetto(false);
            p.Centro().Stile(9).Testo(numero.ToString("D3")).ResetStile();
            p.Centro().Testo(quando.ToString("dd/MM/yyyy HH:mm", It)).Sinistra();
            return p.Byte();
        }

        // ══════════════ STORNO ══════════════

        public static byte[] Storno(DatiComanda d, int stileProdotto, int stileVariante = 5)
        {
            var p = new EscPos();
            p.Normale().Grassetto(true).Sinistra()
             .Testo(new string('*', L))
             .Testo("***" + new string(' ', L - 6) + "***");
            p.Centro().DoppiaDimensione().Inverso(true).Testo("  *** STORNO ***  ").Inverso(false).Normale();
            p.Sinistra()
             .Testo("***" + new string(' ', L - 6) + "***")
             .Testo(new string('*', L)).Grassetto(false);

            p.Normale().Testo(EscPos.DueColonne(d.NomeCentro.ToUpperInvariant(), d.Quando.ToString("dd/MM/yy HH:mm", It)));
            if (!string.IsNullOrEmpty(d.Operatore)) p.Testo($"Op: {d.Operatore}");
            if (!string.IsNullOrWhiteSpace(d.NomeSala)) p.Grassetto(true).Testo(d.NomeSala.ToUpperInvariant()).Grassetto(false);

            p.Testo(Trattini);
            p.Centro().Grassetto(true).DoppiaDimensione().Testo(Tavolo(d)).Normale().Grassetto(false);
            p.Sinistra().Testo(Trattini);

            foreach (var r in d.Righe.Where(x => x.Tipo == TipoRiga.Prodotto))
            {
                p.Stile(stileProdotto).Testo($"- {Qta(r)}  {r.Descrizione}").ResetStile();
                Varianti(p, r, stileVariante);
                p.Testo("      ^^^ STORNATO ^^^");
            }

            p.Normale().Testo(Trattini);
            p.Grassetto(true).Inverso(true).Testo($"  STORNATI: {Pezzi(d.Righe)} pezzi  ").Inverso(false).Grassetto(false);
            p.Grassetto(true)
             .Testo(new string('*', L))
             .Testo("***           FINE STORNO" + new string(' ', L - 28) + "***")
             .Testo(new string('*', L))
             .Grassetto(false);
            return p.Byte();
        }

        // ══════════════ VAI PORTATA ══════════════

        public static byte[] Vai(DatiComanda d, int stileProdotto, int cicalino, int stileVariante = 5)
        {
            var p = new EscPos();
            p.Normale().Grassetto(true).Sinistra().Testo(new string('=', L));
            p.Centro().DoppiaDimensione().Inverso(true).Testo($" VAI {d.NomeSeparatore.ToUpperInvariant()} ").Inverso(false).Normale();
            p.Sinistra().Testo(new string('=', L)).Grassetto(false);

            p.Testo(EscPos.DueColonne(d.NomeCentro.ToUpperInvariant(), d.Quando.ToString("dd/MM/yy HH:mm", It)));
            if (!string.IsNullOrEmpty(d.Operatore)) p.Testo($"Op: {d.Operatore}");
            if (!string.IsNullOrWhiteSpace(d.NomeSala)) p.Grassetto(true).Testo(d.NomeSala.ToUpperInvariant()).Grassetto(false);

            p.Testo(Trattini);
            p.Centro().Grassetto(true).DoppiaDimensione().Testo(Tavolo(d)).Normale().Grassetto(false);
            p.Sinistra().Testo(Trattini);

            foreach (var r in d.Righe.Where(x => x.Tipo == TipoRiga.Prodotto))
            {
                p.Stile(stileProdotto).Testo($" {Qta(r)}  {r.Descrizione}").ResetStile();
                Varianti(p, r, stileVariante);
            }

            if (d.AltriCentri != null && d.AltriCentri.Count > 0)
            {
                p.Normale();
                foreach (var altro in d.AltriCentri)
                    foreach (var r in altro.Value.Where(x => x.Tipo == TipoRiga.Prodotto))
                        p.Testo($"  {Qta(r)} {r.Descrizione}");
            }

            p.Testo(Trattini);
            p.Centro().Grassetto(true).Inverso(true).Testo($"  MARCIA: {Pezzi(d.Righe)} pezzi  ").Inverso(false).Grassetto(false);
            p.Grassetto(true).Testo(new string('=', L)).Grassetto(false);
            p.Sinistra();
            return p.Byte();
        }

        // ══════════════ accorpa / esplodi (come il PC) ══════════════

        private static List<RigaScontrino> Accorpa(IEnumerable<RigaScontrino> righe)
        {
            var ris = new List<RigaScontrino>();
            foreach (var r in righe.Where(x => x.Tipo == TipoRiga.Prodotto))
            {
                var es = PuoAccorpare(r)
                    ? ris.FirstOrDefault(x => PuoAccorpare(x) && x.ProdottoId == r.ProdottoId && x.PrezzoCentesimi == r.PrezzoCentesimi)
                    : null;
                if (es != null) es.Quantita += r.Quantita; else ris.Add(r.Copia());
            }
            return ris;
        }

        /// <summary>Accorpa dentro ogni sezione portata, lasciando i separatori al loro posto.</summary>
        private static List<RigaScontrino> AccorpaFlusso(IEnumerable<RigaScontrino> flusso)
        {
            var ris = new List<RigaScontrino>();
            int inizioSezione = 0;
            foreach (var r in flusso)
            {
                if (r.Tipo == TipoRiga.Portata) { ris.Add(r); inizioSezione = ris.Count; continue; }
                RigaScontrino es = null;
                if (PuoAccorpare(r))
                    for (int i = inizioSezione; i < ris.Count; i++)
                    {
                        var x = ris[i];
                        if (x.Tipo == TipoRiga.Prodotto && PuoAccorpare(x) && x.ProdottoId == r.ProdottoId &&
                            x.PrezzoCentesimi == r.PrezzoCentesimi && x.CentroProduzioneId == r.CentroProduzioneId) { es = x; break; }
                    }
                if (es != null) es.Quantita += r.Quantita; else ris.Add(r.Copia());
            }
            return ris;
        }

        private static bool PuoAccorpare(RigaScontrino r) => !r.IsAPeso && r.ProdottoId.HasValue && string.IsNullOrEmpty(r.VariantiTesto);

        private static List<RigaScontrino> Esplodi(IEnumerable<RigaScontrino> righe)
        {
            var ris = new List<RigaScontrino>();
            foreach (var r in righe.Where(x => x.Tipo == TipoRiga.Prodotto || x.Tipo == TipoRiga.Portata))
            {
                if (r.Tipo == TipoRiga.Prodotto && !r.IsAPeso && string.IsNullOrEmpty(r.VariantiTesto) && r.Quantita > 1 && r.Quantita == Math.Floor(r.Quantita))
                    for (int i = 0; i < (int)r.Quantita; i++) { var c = r.Copia(); c.Quantita = 1; ris.Add(c); }
                else ris.Add(r.Copia());
            }
            return ris;
        }
    }
}
