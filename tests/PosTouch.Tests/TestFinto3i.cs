using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Fiscale;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    /// <summary>
    /// Driver 3i VERO contro il registratore finto: carta, coperchio, rete, footer.
    /// La regola che conta: mai uno scontrino perso dato per emesso, mai un doppio scontrino.
    /// </summary>
    public static class TestFinto3i
    {
        private static List<RigaScontrino> Righe(int n = 1) => Enumerable.Range(0, n).Select(i =>
            new RigaScontrino { Tipo = TipoRiga.Prodotto, ProdottoId = i + 1, Descrizione = i == 0 ? "CAFFÈ" : "CORNETTO", Quantita = 2, PrezzoCentesimi = 120, PrezzoOriginaleCentesimi = 120, RepartoFiscale = 1 }).ToList();

        private static ServizioVendita Servizio(string dir, Finto3i f) =>
            new ServizioVendita(() => new Impostazioni { TipoCassa = TipoCassaFiscale.RT3i, IP_RT = "127.0.0.1", Porta_RT = f.Porta },
                                new ArchivioVendite(dir), c => new RT3iConnector("127.0.0.1", f.Porta));

        private static EsitoVendita Vendi(ServizioVendita s, Pagamento p = null, int righe = 1) =>
            s.EmettiAsync(Righe(righe), p ?? Pagamento.Contanti()).GetAwaiter().GetResult();

        public static void Esegui()
        {
            // tempi brevi: il footer si aspetta "risposta + 5 s"
            ImpostazioniFiscali.Correnti = new ImpostazioniFiscali { TimeoutConnessioneRT_ms = 2000, TimeoutRispostaRT_ms = 1500 };
            string dir = Path.Combine(Path.GetTempPath(), "postouch_3i_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using (var f = new Finto3i())
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Riuscito && e.NumeroDocumento == 42 && f.ScontriniEmessi == 1, "vendita normale: emesso, numero dal TX_FOOTER");
                    Ok(f.Comandi.Any(c => c.StartsWith("\"CAFFE'\"2*120H1R")), "riga inviata in ASCII con quantità");
                }
                using (var f = new Finto3i())
                {
                    var e = Vendi(Servizio(dir, f), Pagamento.Misto(100));
                    Ok(e.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1 && f.Comandi.Contains("100H1T"), "pagamento misto: contanti parziali poi carta");
                }
                using (var f = new Finto3i(Guasto3i.CoperchioAperto))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Fallito && f.ScontriniEmessi == 0, "coperchio aperto: niente emesso, vendita a video");
                    Ok(e.Messaggio.Contains("COPERCHIO"), "coperchio aperto: messaggio chiaro");
                }
                using (var f = new Finto3i(Guasto3i.RigaRifiutata))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Fallito && f.ScontriniEmessi == 0 && !f.ScontrinoAperto, "riga rifiutata: niente emesso, RT pulito");
                }
                using (var f = new Finto3i(Guasto3i.CoperchioSulleRighe))
                {
                    var s = Servizio(dir, f);
                    var e = Vendi(s, righe: 2);
                    Ok(e.Stato == StatoEsito.Fallito && f.ScontriniEmessi == 0, "coperchio aperto a metà: niente emesso, vendita a video");
                    f.Guasto = Guasto3i.Nessuno;   // il cassiere richiude il coperchio
                    var e2 = Vendi(s);
                    Ok(e2.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1 && !f.ScontrinoAperto, "richiuso il coperchio: scontrino a metà annullato, nuovo emesso");
                }
                using (var f = new Finto3i(Guasto3i.ChiusuraRifiutata))
                {
                    var s = Servizio(dir, f);
                    var e = Vendi(s);
                    Ok(e.Stato == StatoEsito.Incerto && f.ScontrinoAperto, "pagamento rifiutato: incerto, scontrino aperto sull'RT");
                    f.Guasto = Guasto3i.Nessuno;
                    var e2 = Vendi(s);
                    Ok(f.Annulli == 1 && e2.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1, "vendita dopo: l'RT viene sbloccato e si emette UN solo scontrino");
                }
                using (var f = new Finto3i(Guasto3i.FooterPerso))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1, "footer perso ma scontrino chiuso: emesso (non rifatto)");
                }
                using (var f = new Finto3i(Guasto3i.CartaFinitaInChiusura))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Fallito && f.ScontriniEmessi == 0 && f.Annulli == 1 && !f.ScontrinoAperto,
                       "carta finita in chiusura: scontrino rimasto aperto annullato, vendita a video");
                }
                using (var f = new Finto3i(Guasto3i.ReteCadeDopoChiusura))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Incerto && f.ScontriniEmessi == 1, "rete caduta dopo la chiusura: incerto (l'operatore conferma, niente doppio)");
                }
                using (var f = new Finto3i(Guasto3i.ReteCadeSulleRighe))
                {
                    var e = Vendi(Servizio(dir, f), righe: 2);
                    Ok(e.Stato != StatoEsito.Riuscito && f.ScontriniEmessi == 0, "rete caduta sulle righe: mai dato per emesso");
                }
                using (var f = new Finto3i(Guasto3i.FooterNonSupportato))
                {
                    var s = Servizio(dir, f);
                    var e1 = Vendi(s); var e2 = Vendi(s); var e3 = Vendi(s);
                    Ok(e1.Emesso && e2.Emesso && e3.Emesso && f.ScontriniEmessi == 3, "RT senza TX_FOOTER: le vendite escono lo stesso");
                    Uguale(2, f.Comandi.Count(c => c.EndsWith("901F")), "TX_FOOTER richiesto al massimo due volte");
                }
            }
            finally
            {
                ImpostazioniFiscali.Correnti = new ImpostazioniFiscali();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
