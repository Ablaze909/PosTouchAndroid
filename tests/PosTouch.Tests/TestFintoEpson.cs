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
    /// <summary>Driver Epson VERO contro la stampante fiscale finta.</summary>
    public static class TestFintoEpson
    {
        private static List<RigaScontrino> Righe() => new List<RigaScontrino>
        {
            new RigaScontrino { Tipo = TipoRiga.Prodotto, ProdottoId = 1, Descrizione = "CAFFÈ", Quantita = 2, PrezzoCentesimi = 120, PrezzoOriginaleCentesimi = 120, RepartoFiscale = 1 }
        };

        private static ServizioVendita Servizio(string dir, FintoEpson f) =>
            new ServizioVendita(() => new Impostazioni { TipoCassa = TipoCassaFiscale.Epson, IP_RT = "127.0.0.1", Porta_RT = f.Porta },
                                new ArchivioVendite(dir), c => new EpsonConnector("127.0.0.1", f.Porta));

        private static EsitoVendita Vendi(ServizioVendita s, Pagamento p = null) =>
            s.EmettiAsync(Righe(), p ?? Pagamento.Contanti()).GetAwaiter().GetResult();

        public static void Esegui()
        {
            ImpostazioniFiscali.Correnti = new ImpostazioniFiscali { TimeoutConnessioneRT_ms = 2000, TimeoutRispostaRT_ms = 1500 };
            string dir = Path.Combine(Path.GetTempPath(), "postouch_ep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using (var f = new FintoEpson())
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1 && e.NumeroDocumento == 42 && e.NumeroAzzeramento == 7,
                       "Epson: vendita normale con numero documento e azzeramento");
                    Ok(f.Comandi.Any(c => c.StartsWith("1-080") && c.Contains("CAFFE'")), "Epson: riga in ASCII");
                }
                using (var f = new FintoEpson())
                {
                    var e = Vendi(Servizio(dir, f), Pagamento.Misto(100));
                    Ok(e.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1 && f.Comandi.Count(c => c.StartsWith("1-084")) == 2, "Epson: pagamento misto in due passi");
                }
                using (var f = new FintoEpson(GuastoEpson.CoperchioAperto))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Fallito && f.ScontriniEmessi == 0, "Epson coperchio aperto: niente emesso");
                }
                using (var f = new FintoEpson(GuastoEpson.RigaRifiutata))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Fallito && f.ScontriniEmessi == 0 && !f.DocumentoAperto, "Epson riga rifiutata: niente emesso, stampante pulita");
                }
                using (var f = new FintoEpson(GuastoEpson.CartaInEsaurimento))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1, "Epson carta in esaurimento: scontrino uscito, nessuna domanda");
                }
                using (var f = new FintoEpson(GuastoEpson.RispostaChiusuraPersa))
                {
                    var e = Vendi(Servizio(dir, f));
                    Ok(e.Stato == StatoEsito.Incerto && f.ScontriniEmessi == 1, "Epson risposta di chiusura persa: incerto (niente doppio)");
                }
                using (var f = new FintoEpson(GuastoEpson.ChiusuraRifiutata))
                {
                    var s = Servizio(dir, f);
                    var e = Vendi(s);
                    Ok(e.Stato != StatoEsito.Riuscito && f.ScontriniEmessi == 0, "Epson chiusura rifiutata: mai dato per emesso");
                    f.Guasto = GuastoEpson.Nessuno;
                    var e2 = Vendi(s);
                    Ok(e2.Stato == StatoEsito.Riuscito && f.ScontriniEmessi == 1 && f.Annulli == 1, "Epson vendita dopo: documento rimasto aperto annullato, UN solo scontrino");
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
