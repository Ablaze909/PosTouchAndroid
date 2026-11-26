using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// VAR＋ / VAR﹣ (TrovaTargetVariante e InserisciVariante di ScontrinoHelper del PC).
    /// Correzione rispetto al PC: la riga variante tiene il prezzo CON SEGNO, così togliere una variante
    /// "senza" restituisce il prezzo giusto (sul PC lo scalava di nuovo).
    /// </summary>
    public static class Varianti
    {
        /// <summary>Riga su cui applicare la variante: la selezionata, altrimenti l'ultimo articolo (o l'ultimo gusto durante la combinazione).</summary>
        public static RigaScontrino TrovaTarget(Carrello c, RigaScontrino selezionata, bool combinazioneAperta, out string errore)
        {
            errore = null;
            RigaScontrino target = null;
            if (selezionata != null && (selezionata.Tipo == TipoRiga.Prodotto || selezionata.Tipo == TipoRiga.Gusto))
            {
                if (selezionata.Tipo == TipoRiga.Prodotto && combinazioneAperta)
                { errore = "Seleziona prima un gusto per aggiungere varianti alla combinazione."; return null; }
                if (selezionata.Tipo == TipoRiga.Prodotto && c.FigliDi(selezionata).Any(f => f.Tipo == TipoRiga.Gusto))
                { errore = "Non puoi aggiungere varianti al prodotto combinazione.\n\nSeleziona un gusto specifico."; return null; }
                target = selezionata;
            }
            else
            {
                for (int i = c.Righe.Count - 1; i >= 0 && target == null; i--)
                {
                    var r = c.Righe[i];
                    if (r.Tipo == TipoRiga.Prodotto)
                    {
                        if (combinazioneAperta) { errore = "Aggiungi almeno un gusto prima di applicare varianti."; return null; }
                        if (c.FigliDi(r).Any(f => f.Tipo == TipoRiga.Gusto))
                        { errore = "Seleziona un gusto specifico per aggiungere varianti alla combinazione."; return null; }
                        target = r;
                    }
                    else if (combinazioneAperta && r.Tipo == TipoRiga.Gusto) target = r;
                }
                if (target == null) { errore = "Nessun prodotto a cui applicare la variante."; return null; }
            }
            var articolo = target.Tipo == TipoRiga.Prodotto ? target : c.PadreDi(target);
            if (articolo?.Comandata == true)
            { errore = "Articolo già inviato in comanda: le varianti non si possono più cambiare.\n\nEliminalo (verrà stornato) e battilo di nuovo."; return null; }
            return target;
        }

        /// <summary>Applica la variante. null = ok, altrimenti il motivo.</summary>
        public static string Applica(Carrello c, RigaScontrino target, Variante v, bool aggiunta, bool combinazioneAperta)
        {
            int prezzo = aggiunta ? v.PrezzoAggiuntaCent : v.PrezzoRimozioneCent;
            int conSegno = aggiunta ? prezzo : -prezzo;
            bool preparazione = v.Tipo == TipoVariante.Preparazione;
            string segno = preparazione ? "" : aggiunta ? "+" : "-";
            string testoComanda = $"{segno}{v.Nome}";
            string descrizione = preparazione ? v.Nome : $"{segno} {v.Nome}";

            if (target.Tipo == TipoRiga.Prodotto)
            {
                if (!c.RigaAmmessa(target.PrezzoCentesimi + conSegno, target.Quantita))
                    return $"Con questa variante il prezzo dell'articolo non sarebbe valido.\n{c.UltimoErrore}";
                target.PrezzoCentesimi += conSegno;
                target.VariantiTesto = string.IsNullOrEmpty(target.VariantiTesto) ? testoComanda : target.VariantiTesto + "|" + testoComanda;
            }
            var riga = new RigaScontrino
            {
                Tipo = TipoRiga.Variante, Descrizione = descrizione, Quantita = 0,
                PrezzoCentesimi = conSegno, PrezzoOriginaleCentesimi = conSegno, CategoriaId = target.CategoriaId
            };
            if (target.Tipo == TipoRiga.Gusto && !combinazioneAperta)
            {
                // combinazione già chiusa: serve l'articolo base per ricalcolare, e il prezzo deve restare valido
                var padre = c.PadreDi(target);
                if (padre == null || !padre.ProdottoId.HasValue || Carrello.RisolviProdotto?.Invoke(padre.ProdottoId.Value) == null)
                    return "L'articolo combinazione non è più nel catalogo: la variante non si può ricalcolare.";
                int prima = padre.PrezzoCentesimi;
                string testoPrima = padre.VariantiTesto;
                c.InserisciVariante(target, riga);
                Combinazioni.Ricalcola(c, padre);
                if (!c.RigaAmmessa(padre.PrezzoCentesimi, padre.Quantita))
                {
                    string motivo = c.UltimoErrore;
                    c.Righe.Remove(riga);
                    Combinazioni.Ricalcola(c, padre);
                    if (padre.PrezzoCentesimi != prima) { padre.PrezzoCentesimi = prima; padre.VariantiTesto = testoPrima; }
                    return $"Con questa variante il prezzo della combinazione non sarebbe valido.\n{motivo}";
                }
                c.Notifica();
                return null;
            }
            c.InserisciVariante(target, riga);
            c.Notifica();
            return null;
        }

        /// <summary>RimuoviTestoVarianteDaPadre del PC: toglie una voce dal testo comanda.</summary>
        public static string TogliTesto(string variantiTesto, string descrizione)
        {
            if (string.IsNullOrEmpty(variantiTesto)) return variantiTesto;
            var d = descrizione.Trim().Replace(" ", "");
            var parti = variantiTesto.Split('|').ToList();
            int i = parti.FindIndex(p => p.Trim().Replace(" ", "") == d);
            if (i < 0) i = parti.FindIndex(p => p.Trim().TrimStart('+', '-', ' ') == descrizione.Trim().TrimStart('+', '-', ' '));
            if (i >= 0) parti.RemoveAt(i);
            return parti.Count == 0 ? null : string.Join("|", parti);
        }
    }
}
