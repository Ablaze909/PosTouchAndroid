using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    /// <summary>tavoli.db vero (SQLite): tavolo aperto anche vuoto, storni persistenti, coda in ordine, eliminacode.</summary>
    public static class TestArchivio
    {
        public static void Esegui()
        {
            string dir = Path.Combine(Path.GetTempPath(), "postouch_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var a = new ArchivioTavoli(dir);
                var t = a.ApriTavolo(1, 5);
                Ok(t != null && t.Id > 0, "apre il tavolo 5");
                var riga = new RigaScontrino { Tipo = TipoRiga.Prodotto, ProdottoId = 1, Descrizione = "PIZZA", Quantita = 2, PrezzoCentesimi = 600, CentroProduzioneId = 3 };
                a.Salva(t, new List<RigaScontrino> { riga });
                Uguale(1, a.Righe(t.Id).Count, "riga salvata");
                Uguale(1200, a.Aperti().Single(x => x.Numero == 5).TotaleCent, "totale del tavolo nella mappa");

                a.Salva(t, new List<RigaScontrino>(), storniPendenti: new[] { riga });
                Ok(a.Aperti().Any(x => x.Id == t.Id), "salvataggio continuo vuoto: il tavolo resta aperto");
                Uguale(1, a.StorniPendenti(t.Id).Count, "storno pendente salvato");
                Uguale(0, a.Righe(t.Id).Count, "lo storno non conta come riga del conto");

                var job = new JobStampa { Tipo = TipoJob.Comanda, Descrizione = "COMANDA T5", Stampante = "PIZZERIA", IP = "10.0.0.1", Porta = 9100, Dati = new byte[] { 1, 2, 3 } };
                a.Salva(t, new List<RigaScontrino> { riga }, new[] { job });
                var coda = a.CodaDaStampare(true);
                Ok(coda.Count == 1 && coda[0].SalaId == 1 && coda[0].NumeroTavolo == 5, "stampa in coda con il suo tavolo");
                a.EsitoJob(coda[0].Id, false, "spenta", true);
                Ok(a.TavoliConErroreStampa().Contains("1:5"), "tavolo segnato con errore di stampa");

                a.SpostaTavolo(t.Id, 1, 9);
                Ok(a.TavoliConErroreStampa().Contains("1:9") && !a.TavoliConErroreStampa().Contains("1:5"), "l'errore di stampa segue il tavolo spostato");
                Ok(a.Aperti().Single(x => x.Id == t.Id).Numero == 9, "tavolo spostato al 9");

                a.Salva(t, new List<RigaScontrino>(), storniPendenti: new[] { riga });
                var soloStorni = a.Aperti().Single(x => x.Id == t.Id);
                Ok(soloStorni.Occupato && soloStorni.Articoli == 0, "tavolo con soli storni da inviare: resta occupato in mappa");

                int e1 = a.ProssimoEliminacode(), e2 = a.ProssimoEliminacode();
                Uguale(e1 + 1, e2, "eliminacode progressivo");
                a.AzzeraEliminacode();
                Uguale(1, a.ProssimoEliminacode(), "dopo la chiusura riparte da 1");

                a.Salva(t, new List<RigaScontrino>(), liberaSeVuoto: true);
                Ok(!a.Aperti().Any(x => x.Id == t.Id), "uscendo vuoto il tavolo si libera");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
