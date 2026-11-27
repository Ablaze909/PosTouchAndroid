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
    /// <summary>Emissione dello scontrino con il registratore finto: i casi che decidono incasso e doppi scontrini.</summary>
    public static class TestVendita
    {
        private static List<RigaScontrino> Righe() => new List<RigaScontrino>
        {
            new RigaScontrino { Tipo = TipoRiga.Prodotto, ProdottoId = 1, Descrizione = "CAFFÈ", Quantita = 2, PrezzoCentesimi = 120, PrezzoOriginaleCentesimi = 120, RepartoFiscale = 1 }
        };

        private static EsitoVendita Emetti(string dir, TipoCassaFiscale tipo, Action<RegistratoreFinto> prepara, out RegistratoreFinto finto)
        {
            var (rt, f) = RegistratoreFinto.Crea();
            prepara(f);
            var cfg = new Impostazioni { TipoCassa = tipo };
            var servizio = new ServizioVendita(() => cfg, new ArchivioVendite(dir), _ => rt);
            finto = f;
            return servizio.EmettiAsync(Righe(), Pagamento.Contanti()).GetAwaiter().GetResult();
        }

        public static void Esegui()
        {
            string dir = Path.Combine(Path.GetTempPath(), "postouch_vend_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var ok = Emetti(dir, TipoCassaFiscale.RT3i, f => { }, out var f1);
                Ok(ok.Stato == StatoEsito.Riuscito && ok.NumeroDocumento == 42, "scontrino emesso con numero documento");
                Ok(f1.Comandi.Any(c => c.StartsWith("VendiRepartoAsync(1,CAFFE'")), "descrizione senza accenti al registratore");
                Ok(f1.Comandi.Contains("ChiudiContantiAutoAsync"), "contanti esatti: chiusura a saldo");

                var rifiuto = Emetti(dir, TipoCassaFiscale.RT3i, f => f.Rifiuta.Add("VendiRepartoAsync"), out var f2);
                Ok(rifiuto.Stato == StatoEsito.Fallito && f2.Comandi.Contains("RecuperaScontrinoApertoAsync"), "riga rifiutata: scontrino annullato, vendita a video");
                Ok(!f2.Comandi.Any(c => c.StartsWith("Chiudi")), "riga rifiutata: nessuna chiusura inviata");

                var chiusura = Emetti(dir, TipoCassaFiscale.RT3i, f => f.Rifiuta.Add("ChiudiContantiAutoAsync"), out _);
                Ok(chiusura.Stato == StatoEsito.Incerto && chiusura.DaRegistrare != null, "chiusura senza risposta: decide l'operatore (niente doppio scontrino)");

                var aperto = Emetti(dir, TipoCassaFiscale.RT3i, f => { f.FooterConfermato = false; f.StatoDopoChiusura = true; }, out _);
                Ok(aperto.Stato == StatoEsito.Fallito, "senza conferma e scontrino ancora aperto: annullato");

                var ignoto = Emetti(dir, TipoCassaFiscale.RT3i, f => { f.FooterConfermato = false; f.StatoDopoChiusura = null; }, out _);
                Ok(ignoto.Stato == StatoEsito.Incerto, "3i senza conferma e stato illeggibile: incerto");

                var cadutaAnnullo = Emetti(dir, TipoCassaFiscale.RT3i, f => { f.FooterConfermato = false; f.StatoDopoChiusura = true; f.Lancia.Add("RecuperaScontrinoApertoAsync"); f.LanciaDopo["RecuperaScontrinoApertoAsync"] = 1; }, out _);
                Ok(cadutaAnnullo.Stato == StatoEsito.Incerto, "rete caduta mentre si annulla lo scontrino aperto: incerto, mai \"emesso\"");

                var cadutaFooter = Emetti(dir, TipoCassaFiscale.RT3i, f => f.Lancia.Add("LeggiTxFooterAsync"), out _);
                Ok(cadutaFooter.Stato == StatoEsito.Incerto, "rete caduta leggendo la conferma: incerto, mai \"emesso\"");

                var chiuso = Emetti(dir, TipoCassaFiscale.RT3i, f => { f.FooterConfermato = false; f.StatoDopoChiusura = false; }, out _);
                Ok(chiuso.Stato == StatoEsito.Riuscito, "senza conferma ma scontrino chiuso: emesso");

                var epson = Emetti(dir, TipoCassaFiscale.Epson, f => { f.FooterConfermato = false; f.StatoDopoChiusura = null; }, out _);
                Ok(epson.Stato == StatoEsito.Riuscito, "Epson: stato non verificabile non blocca");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
