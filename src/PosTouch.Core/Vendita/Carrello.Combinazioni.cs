using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;

namespace PosTouch.Core.Vendita
{
    public partial class Carrello
    {
        // ══════════════ COMBINAZIONI (gusti) ══════════════

        /// <summary>Riga padre di una combinazione, a prezzo 0 finché non viene chiusa (vedi <see cref="CombinazioneInCorso"/>).</summary>
        internal RigaScontrino AggiungiPadreCombinazione(Prodotto p, string descrizione)
        {
            var riga = new RigaScontrino
            {
                Tipo = TipoRiga.Prodotto, ProdottoId = p.Id, Descrizione = descrizione,
                Barcode = string.IsNullOrEmpty(p.Barcode) ? p.Codice : p.Barcode,
                RepartoFiscale = p.RepartoFiscale, CategoriaId = p.CategoriaId, CategoriaNome = p.CategoriaNome ?? "",
                AliquotaIVA = p.AliquotaIVA, PrezzoCentesimi = 0, PrezzoOriginaleCentesimi = p.PrezzoListino(Listino),
                Quantita = 1, CentroProduzioneId = p.CentroStampa
            };
            InserisciArticolo(riga);
            Aggiornato();
            return riga;
        }

        private static bool IsFiglio(RigaScontrino r) => r.Tipo is TipoRiga.Gusto or TipoRiga.Variante;

        /// <summary>Aggiunge una riga figlia (gusto) dopo il padre e tutti i suoi figli.</summary>
        internal void AggiungiFiglio(RigaScontrino padre, RigaScontrino figlio)
        {
            int i = Righe.IndexOf(padre);
            if (i < 0) return;
            int pos = i + 1;
            while (pos < Righe.Count && IsFiglio(Righe[pos])) pos++;
            if (pos >= Righe.Count) Righe.Add(figlio); else Righe.Insert(pos, figlio);
            Aggiornato();
        }

        /// <summary>Inserisce una variante subito sotto la riga indicata (dopo le sue varianti già presenti).</summary>
        internal void InserisciVariante(RigaScontrino target, RigaScontrino variante)
        {
            int pos = Righe.IndexOf(target) + 1;
            if (pos <= 0) return;
            while (pos < Righe.Count && Righe[pos].Tipo == TipoRiga.Variante) pos++;
            if (pos >= Righe.Count) Righe.Add(variante); else Righe.Insert(pos, variante);
        }

        /// <summary>Togli l'ultimo gusto (e le sue varianti) del padre — tasto C durante la combinazione.</summary>
        internal bool TogliUltimoFiglio(RigaScontrino padre)
        {
            var figli = FigliDi(padre);
            var gusto = figli.LastOrDefault(f => f.Tipo == TipoRiga.Gusto);
            if (gusto == null) return false;
            foreach (var f in figli.Skip(figli.IndexOf(gusto))) Righe.Remove(f);
            Aggiornato();
            return true;
        }

        /// <summary>Gusti e varianti subito sotto l'articolo.</summary>
        public List<RigaScontrino> FigliDi(RigaScontrino padre)
        {
            var figli = new List<RigaScontrino>();
            int i = Righe.IndexOf(padre);
            if (i < 0) return figli;
            for (int k = i + 1; k < Righe.Count && IsFiglio(Righe[k]); k++) figli.Add(Righe[k]);
            return figli;
        }

        /// <summary>Articolo a cui appartiene un gusto o una variante.</summary>
        public RigaScontrino PadreDi(RigaScontrino figlio)
        {
            for (int k = Righe.IndexOf(figlio) - 1; k >= 0; k--)
            {
                if (Righe[k].Tipo == TipoRiga.Prodotto) return Righe[k];
                if (!IsFiglio(Righe[k])) break;
            }
            return null;
        }

        /// <summary>
        /// Stacca UN pezzo da un articolo con quantità > 1 (es. 3 pizze, una "senza cipolla"):
        /// la riga originale scende di 1 e sotto il suo blocco nasce una riga da 1 pezzo con le stesse varianti.
        /// Restituisce la nuova riga, o null se non si può (peso, combinazione, comandata, quantità non intera).
        /// </summary>
        public RigaScontrino SeparaUnPezzo(RigaScontrino riga)
        {
            if (riga == null || riga.Tipo != TipoRiga.Prodotto || riga.Comandata || riga.IsAPeso) return null;
            if (riga.Quantita < 2 || riga.Quantita != decimal.Truncate(riga.Quantita)) return null;
            var figli = FigliDi(riga);
            if (figli.Any(f => f.Tipo == TipoRiga.Gusto)) return null;
            var nuova = riga.Copia();
            nuova.Quantita = 1;
            nuova.Selezionata = false;
            riga.Quantita -= 1;
            int pos = Righe.IndexOf(riga) + 1 + figli.Count;
            var blocco = new List<RigaScontrino> { nuova };
            blocco.AddRange(figli.Select(f => { var c = f.Copia(); c.Selezionata = false; return c; }));
            foreach (var r in blocco) { if (pos >= Righe.Count) Righe.Add(r); else Righe.Insert(pos, r); pos++; }
            Aggiornato();
            return nuova;
        }

        /// <summary>Riga a cui è attaccata una variante (l'articolo o il gusto subito sopra).</summary>
        public RigaScontrino ProprietarioDi(RigaScontrino variante)
        {
            for (int k = Righe.IndexOf(variante) - 1; k >= 0; k--)
                if (Righe[k].Tipo != TipoRiga.Variante) return Righe[k];
            return null;
        }

        /// <summary>RimuoviSingolaVariante del PC: toglie la variante e ne scala il prezzo (con il segno giusto).</summary>
        private void RimuoviVariante(RigaScontrino v)
        {
            var proprietario = ProprietarioDi(v);
            var padre = PadreDi(v);
            if (padre?.Comandata == true) { UltimoErrore = "Articolo già inviato in comanda: le varianti non si possono togliere."; return; }
            if (proprietario?.Tipo == TipoRiga.Prodotto && proprietario.IsAPeso)
            {
                if (proprietario.ImportoCentesimi - v.PrezzoOriginaleCentesimi <= 0)
                { UltimoErrore = $"Togliendo la variante l'importo di {proprietario.Descrizione.Trim()} non sarebbe valido."; return; }
                Righe.Remove(v);
                proprietario.SovrapprezzoCent -= v.PrezzoOriginaleCentesimi;
                proprietario.VariantiTesto = Varianti.TogliTesto(proprietario.VariantiTesto, v.Descrizione);
                return;
            }
            if (proprietario?.Tipo == TipoRiga.Prodotto && !RigaAmmessa(proprietario.PrezzoCentesimi - v.PrezzoOriginaleCentesimi, proprietario.Quantita))
            {
                UltimoErrore = $"Togliendo la variante il prezzo di {proprietario.Descrizione.Trim()} non sarebbe valido.\n{UltimoErrore}\n\nCorreggi prima il prezzo dell'articolo.";
                return;
            }
            if (proprietario?.Tipo == TipoRiga.Gusto && padre != null && Combinazioni.Chiusa(padre))
            {
                Combinazioni.Aggiorna(this, padre, () => Righe.Remove(v));   // il prezzo a mano resta, si toglie solo la variante
                return;
            }
            Righe.Remove(v);
            if (proprietario?.Tipo == TipoRiga.Prodotto)
            {
                proprietario.PrezzoCentesimi -= v.PrezzoOriginaleCentesimi;
                proprietario.VariantiTesto = Varianti.TogliTesto(proprietario.VariantiTesto, v.Descrizione);
            }
        }

        /// <summary>Ricalcola totali e lista dopo una modifica fatta da fuori (es. chiusura combinazione).</summary>
        internal void Notifica() => Aggiornato();
    }
}
