using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Stampa;
using PosTouch.Core.Vendita;

namespace PosTouch.Core.Sale
{
    public partial class SessioneTavolo
    {
        // ══════════════ 🔀 SPOSTA / 🔗 UNISCI ══════════════

        /// <summary>
        /// 🔀 SPOSTA: salva il tavolo (storni compresi, le righe nuove restano da comandare) e lo sposta;
        /// se la destinazione è occupata i tavoli si scambiano. Dopo si torna alla sala.
        /// </summary>
        public string Sposta(int salaDest, int numeroDest, string nomeDestinazione)
        {
            if (!Carrello.HaArticoli) throw new InvalidOperationException("Il tavolo è vuoto: non c'è niente da spostare.");
            string da = Titolo;
            EsciSenzaInvio();
            bool scambio;
            try { scambio = _archivio.SpostaTavolo(Tavolo.Id, salaDest, numeroDest); }
            catch
            {
                _chiusa = false;   // spostamento fallito: il tavolo resta aperto a video e deve continuare a salvarsi
                throw;
            }
            return scambio ? $"{da} ↔ {nomeDestinazione}" : $"Spostato da {da} a {nomeDestinazione}.";
        }

        /// <summary>🔗 UNISCI: le righe dei tavoli scelti vengono aggiunte a questo tavolo (comande già fatte restano comandate).</summary>
        public void Unisci(IEnumerable<long> tavoliId)
        {
            SalvaBozza();
            var ids = tavoliId.Where(id => id != Tavolo.Id).Distinct().ToList();
            // righe di ogni tavolo con la SUA portata (calcolata sul tavolo da solo), prima di spostarle
            var mie = Carrello.Righe.ToList();
            SegnaVaiGiaPartiti(mie, Tavolo.PortataCorrente);
            var perTavolo = new List<IList<RigaScontrino>> { mie };
            var aperti = _archivio.Aperti();
            int portataMinima = Tavolo.PortataCorrente;
            foreach (long id in ids)
            {
                var r = _archivio.Righe(id);
                Portate.Assegna(r, Carrello.SeparatoriPortate);
                var altro = aperti.FirstOrDefault(t => t.Id == id);
                int sua = altro?.PortataCorrente ?? 0;
                if (altro != null)
                {
                    // tempi e tazzina: valgono i più recenti / basta che uno dei tavoli abbia il fine pasto
                    if (altro.UltimoOrdine > (Tavolo.UltimoOrdine ?? DateTime.MinValue)) Tavolo.UltimoOrdine = altro.UltimoOrdine;
                    if (altro.UltimoVai > (Tavolo.UltimoVai ?? DateTime.MinValue)) Tavolo.UltimoVai = altro.UltimoVai;
                    Tavolo.FinePasto |= altro.FinePasto;
                }
                SegnaVaiGiaPartiti(r, sua);
                portataMinima = Math.Min(portataMinima, sua);
                perTavolo.Add(r);
            }
            // lista unita calcolata PRIMA di toccare l'archivio: se qualcosa va storto non si perde niente
            var unite = UnioneTavoli.Unisci(perTavolo, Carrello.SeparatoriPortate);
            _archivio.UnisciTavoli(Tavolo.Id, ids);
            // portata corrente = quella del tavolo più indietro: così nessun VAI resta da fare, e i piatti
            // dell'altro tavolo già partiti (VaiPartito) non vengono rimandati in cucina
            Tavolo.PortataCorrente = portataMinima;
            try { Carrello.Carica(unite); }
            catch
            {
                // l'archivio è già unito: a video si rimette quello che c'è nel database, così il prossimo salvataggio
                // non cancella le righe dei tavoli uniti
                Carrello.Carica(_archivio.Righe(Tavolo.Id));
                throw;
            }
            // anche gli storni in sospeso dei tavoli uniti (altrimenti il prossimo salvataggio li cancellerebbe)
            _storniPendenti.Clear();
            _storniPendenti.AddRange(_archivio.StorniPendenti(Tavolo.Id));
            SalvaBozza();   // ordine per portata e portata corrente salvati subito
            LogManager.Info($"UNISCI in {Titolo}: {Carrello.Righe.Count(r => r.Tipo == TipoRiga.Prodotto)} articoli");
        }

        /// <summary>Piatti comandati di portate già avanzate su quel tavolo: il loro VAI è partito (anche per tavoli di versioni precedenti).</summary>
        private static void SegnaVaiGiaPartiti(IEnumerable<RigaScontrino> righe, int portataCorrente)
        {
            foreach (var r in righe)
                if (r.Tipo == TipoRiga.Prodotto && r.Comandata && r.Portata > 0 && r.Portata <= portataCorrente) r.VaiPartito = true;
        }

        // ══════════════ 📋 PRECONTO ══════════════

        /// <summary>Preconto sulla termica (non salvato come conto); il tavolo diventa giallo. Restituisce null se ok.</summary>
        public async Task<string> PrecontoAsync()
        {
            var imp = _impostazioni();
            if (!imp.AbilitaStampanteTermica) return "Stampante termica disabilitata.";
            var righe = Carrello.Fotografia();
            if (!righe.Any(r => r.Tipo == TipoRiga.Prodotto)) return "Non ci sono articoli.";
            if (righe.Where(r => r.ContaNelTotale).Sum(r => r.ImportoCentesimi) <= 0) return "Totale non valido.";
            var pre = imp.StampantePreconti(Tavolo.SalaId);
            if (!pre.Assegnata) return "Nessuna stampante PRECONTI impostata.\n\nImpostala in IMPOSTAZIONI → 🖨️ STAMPE.";
            var dati = Finitura.Applica(LayoutConto.Crea(righe, 0, imp.IntestazioneConto1, imp.IntestazioneConto2, imp.IntestazioneConto3, imp.AccorpaRigheConto, DateTime.Now, varianti: imp.VariantiSuConto,
                                        coperti: imp.StampaMediaPerCoperto ? Coperti(righe) : 0),
                                        pre, imp.Sale.CicalinoComande);
            string errore = await AccessoStampanti.InviaAsync(pre.IP, pre.Porta, dati, TimeSpan.FromSeconds(10), pre.ControlloStato);
            if (errore != null) return $"Impossibile stampare su {pre.Descrizione}.\n{errore}\n\nPreconto NON stampato.";
            Tavolo.Preconto = true;
            _firmaPreconto = FirmaConto();
            SalvaBozza();
            return null;
        }
    }
}
