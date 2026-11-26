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
            var perTavolo = new List<IList<RigaScontrino>> { Carrello.Righe.ToList() };
            foreach (long id in ids)
            {
                var r = _archivio.Righe(id);
                Portate.Assegna(r, Carrello.SeparatoriPortate);
                perTavolo.Add(r);
            }
            // lista unita calcolata PRIMA di toccare l'archivio: se qualcosa va storto non si perde niente
            var unite = UnioneTavoli.Unisci(perTavolo, Carrello.SeparatoriPortate);
            _archivio.UnisciTavoli(Tavolo.Id, ids);
            // ogni articolo nella sua portata; la portata corrente resta quella di questo tavolo (come il PC):
            // meglio un VAI ripetuto per il tavolo unito che un VAI mai partito per questo
            Carrello.Carica(unite);
            // anche gli storni in sospeso dei tavoli uniti (altrimenti il prossimo salvataggio li cancellerebbe)
            _storniPendenti.Clear();
            _storniPendenti.AddRange(_archivio.StorniPendenti(Tavolo.Id));
            SalvaBozza();   // ordine per portata e portata corrente salvati subito
            LogManager.Info($"UNISCI in {Titolo}: {Carrello.Righe.Count(r => r.Tipo == TipoRiga.Prodotto)} articoli");
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
