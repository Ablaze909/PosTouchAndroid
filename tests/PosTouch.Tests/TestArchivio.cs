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

                // numeri: la giornata cambia alle 5, non a mezzanotte; la chiusura azzera anche il progressivo comande
                Uguale("2026-11-27", ArchivioTavoli.GiornataLavorativa(new DateTime(2026, 11, 28, 1, 30, 0)), "all'1:30 è ancora la giornata di ieri");
                Uguale("2026-11-28", ArchivioTavoli.GiornataLavorativa(new DateTime(2026, 11, 28, 5, 1, 0)), "dalle 5 è la giornata nuova");
                a.ProssimoProgressivoComanda(); a.ProssimoProgressivoComanda();
                a.AzzeraEliminacode();
                Uguale(1, a.ProssimoProgressivoComanda(), "chiusura fiscale: anche il progressivo comande riparte da 1");

                // UNISCI tra tavoli a punti diversi: il VAI stampa solo i piatti non ancora partiti
                var imp = new PosTouch.Core.Configurazione.Impostazioni();
                imp.Sale.AbilitaPortate = true;
                imp.Sale.SeparatoriPortate = new List<string> { "SEGUE", "SEGUE 2" };
                RigaScontrino Sep() => new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.Descrizione("SEGUE"), Quantita = 0, Portata = 1 };
                RigaScontrino Piatto(string d) => new RigaScontrino { Tipo = TipoRiga.Prodotto, ProdottoId = 7, Descrizione = d, Quantita = 1, PrezzoCentesimi = 1000, CentroProduzioneId = 2, Portata = 1, Comandata = true };
                var t3 = a.ApriTavolo(1, 3);
                a.Salva(t3, new List<RigaScontrino> { Sep(), Piatto("CARBONARA") });
                var t6 = a.ApriTavolo(1, 6);
                t6.PortataCorrente = 1;   // il 6 ha già fatto VAI SEGUE
                a.Salva(t6, new List<RigaScontrino> { Sep(), Piatto("TAGLIATA") });
                Uguale(1, a.Aperti().Single(x => x.Id == t6.Id).PortataCorrente, "portata corrente del 6 salvata");
                var s3 = new SessioneTavolo(a, new SpoolerComande(a), () => imp, 1, 3);
                s3.Unisci(new[] { t6.Id });
                Uguale(0, s3.Tavolo.PortataCorrente, "unito: portata corrente del tavolo più indietro");
                var av = s3.PreparaAvanza();
                Ok(av.Possibile && av.Messaggio.Contains("CARBONARA") && !av.Messaggio.Contains("TAGLIATA"), "VAI dopo UNISCI: solo i piatti non ancora partiti");
                Ok(a.Righe(t3.Id).Any(r => r.Descrizione == "TAGLIATA" && r.VaiPartito), "piatto già partito ricordato nell'archivio");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
