using System;
using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    public enum PoliticaPrezzoCombinazione { PiuCaro = 0, Media = 1, Fisso = 2 }

    /// <summary>
    /// Articolo a combinazione (pizza multi-gusto), stessa logica di ScontrinoHelper del PC:
    /// riga padre a prezzo 0, poi N gusti scelti fra gli articoli della stessa categoria, poi CHIUDI:
    /// il prezzo del padre si calcola con la politica dell'articolo (più caro / media / fisso).
    /// Finché è aperta lo scontrino non si può chiudere.
    /// </summary>
    public class CombinazioneInCorso
    {
        private readonly Carrello _carrello;

        public Prodotto Base { get; }
        public RigaScontrino Padre { get; }
        public int Totali { get; }
        public int Mancanti { get; private set; }
        /// <summary>Articoli selezionabili come gusto (stessa categoria, non combinazioni).</summary>
        public IReadOnlyList<Prodotto> Gusti { get; }
        private readonly List<Prodotto> _scelti = new List<Prodotto>();

        private CombinazioneInCorso(Carrello carrello, Prodotto prodBase, IReadOnlyList<Prodotto> gusti)
        {
            _carrello = carrello;
            Base = prodBase;
            Gusti = gusti;
            Totali = Math.Clamp(prodBase.NumeroGusti, 2, 8);
            Mancanti = Totali;
            // solo il nome (niente "(1/3+1/3+...)"): i gusti sono già scritti sotto, su scontrino, conto e comanda
            Padre = carrello.AggiungiPadreCombinazione(prodBase, prodBase.Descrizione);
        }

        public static string Frazione(int n) => n == 2 ? "½" : n == 3 ? "⅓" : n == 4 ? "¼" : $"1/{n}";

        /// <summary>Inizia la combinazione; null + errore se nella categoria non ci sono gusti.</summary>
        public static CombinazioneInCorso Inizia(Carrello carrello, Prodotto prodBase, IEnumerable<Prodotto> stessaCategoria, out string errore)
        {
            var gusti = stessaCategoria.Where(p => p.Id != prodBase.Id && !p.IsCombinazione && p.Attivo).ToList();
            if (gusti.Count == 0)
            {
                errore = "Non ci sono prodotti nella stessa categoria da usare come gusti.\n\nAggiungi prodotti nella categoria di questo articolo.";
                return null;
            }
            errore = null;
            return new CombinazioneInCorso(carrello, prodBase, gusti);
        }

        public bool Completa => Mancanti <= 0;

        public string Banner => Mancanti > 0
            ? $"🍕 GUSTO {Totali - Mancanti + 1}/{Totali} — seleziona dalla griglia"
            : $"🍕 {Totali}/{Totali} GUSTI OK — premi CHIUDI COMBINAZIONE";

        /// <summary>null = ok, altrimenti il motivo.</summary>
        public string AggiungiGusto(Prodotto gusto)
        {
            if (gusto.IsCombinazione) return "Non puoi selezionare un altro prodotto combinazione come gusto.";
            if (Mancanti <= 0)
                return $"Hai già selezionato tutti i {Totali} gusti.\n\nPremi \"✅ CHIUDI COMBINAZIONE\" per confermare\noppure \"❌ ANNULLA\" per ricominciare.";
            _carrello.AggiungiFiglio(Padre, new RigaScontrino
            {
                Tipo = TipoRiga.Gusto, Descrizione = $"  {Frazione(Totali)} {gusto.Descrizione}", Quantita = 1,
                PrezzoCentesimi = 0, PrezzoOriginaleCentesimi = gusto.PrezzoListino(_carrello.Listino),   // prezzo del gusto (listino del conto) per i ricalcoli
                CategoriaId = Base.CategoriaId, ProdottoId = gusto.Id
            });
            _scelti.Add(gusto);
            Mancanti--;
            return null;
        }

        /// <summary>Tasto C: toglie l'ultimo gusto scelto.</summary>
        public bool TogliUltimoGusto()
        {
            if (_scelti.Count == 0 || !_carrello.TogliUltimoFiglio(Padre)) return false;
            _scelti.RemoveAt(_scelti.Count - 1);
            Mancanti++;
            return true;
        }

        /// <summary>✅ CHIUDI COMBINAZIONE: null = ok, altrimenti il motivo.</summary>
        public string Chiudi()
        {
            if (Mancanti > 0)
                return $"Hai selezionato {Totali - Mancanti} di {Totali} gusti.\n\nSeleziona i gusti mancanti prima di chiudere.";
            int prezzo = Combinazioni.Ricalcola(_carrello, Padre, Base);
            if (prezzo <= 0)
            {
                Padre.PrezzoCentesimi = 0; Padre.VariantiTesto = null;
                return "Il prezzo della combinazione è € 0,00: controlla i prezzi dei gusti o dell'articolo.";
            }
            return null;
        }

        /// <summary>❌ ANNULLA: toglie padre e gusti.</summary>
        public void Annulla() => _carrello.Rimuovi(new[] { Padre });

        /// <summary>La combinazione è ancora nello scontrino? (può essere stata cancellata con C/elimina)</summary>
        public bool Presente => _carrello.Righe.Contains(Padre);

        public static int Prezzo(Prodotto prodBase, IList<int> prezziGusti) => Combinazioni.PrezzoBase(prodBase, prezziGusti);
    }
}
