using System;
using System.IO;
using System.Text;
using PosTouch.Core.Dati;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    /// <summary>Import del catalogo: un file sbagliato non deve lasciare nulla di rotto al posto del catalogo.</summary>
    public static class TestCatalogo
    {
        public static void Esegui()
        {
            string dir = Path.Combine(Path.GetTempPath(), "postouch_cat_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var cat = new Catalogo(dir);
                bool rifiutato = false;
                try { cat.Importa(new MemoryStream(Encoding.ASCII.GetBytes("non sono un database"))); }
                catch (InvalidDataException) { rifiutato = true; }
                Ok(rifiutato, "file non valido rifiutato");
                Ok(!File.Exists(Path.Combine(dir, "catalogo.db.bak")), "import fallito: nessuna copia .bak spostata");

                // ── barcode: lettere, più barcode per articolo, pezzi per confezione ──
                var g = new GestioneCatalogo(cat);
                g.SalvaCategoria(new PosTouch.Core.Modelli.Categoria { Nome = "BIBITE", RepartoFiscale = 1, AliquotaIVA = 22, Colore = "#2196F3" });
                int catId = System.Linq.Enumerable.First(cat.TutteCategorie).Id;
                var acqua = new PosTouch.Core.Modelli.Prodotto
                {
                    Descrizione = "ACQUA", PrezzoCentesimi = 100, CategoriaId = catId, Barcode = " ABC-123\r",
                    BarcodeAggiuntivi = { new PosTouch.Core.Modelli.BarcodeConfezione { Barcode = "8001", Pezzi = 6 }, new PosTouch.Core.Modelli.BarcodeConfezione { Barcode = "8002", Pezzi = 24 } }
                };
                g.SalvaProdotto(acqua);
                var (p1, n1) = cat.CercaBarcodeConQuantita("ABC-123");
                var (p6, n6) = cat.CercaBarcodeConQuantita("8001");
                var (p24, n24) = cat.CercaBarcodeConQuantita("8002");
                Ok(p1?.Descrizione == "ACQUA" && n1 == 1, "barcode con lettere: trovato, 1 pezzo");
                Ok(p6?.Descrizione == "ACQUA" && n6 == 6 && p24?.Descrizione == "ACQUA" && n24 == 24, "barcode confezione: 6 e 24 pezzi");
                Uguale(2, System.Linq.Enumerable.First(cat.TuttiProdotti).BarcodeAggiuntivi.Count, "riaperto: i 2 barcode in più ci sono (il principale non si ripete)");
                bool doppio = false;
                try { g.SalvaProdotto(new PosTouch.Core.Modelli.Prodotto { Descrizione = "ALTRA", PrezzoCentesimi = 100, CategoriaId = catId, BarcodeAggiuntivi = { new PosTouch.Core.Modelli.BarcodeConfezione { Barcode = "8001", Pezzi = 2 } } }); }
                catch (ArgumentException) { doppio = true; }
                Ok(doppio && System.Linq.Enumerable.Count(cat.TuttiProdotti) == 1, "barcode di un altro articolo rifiutato, niente salvato");
                bool interno = false;
                try { g.SalvaProdotto(new PosTouch.Core.Modelli.Prodotto { Descrizione = "X", PrezzoCentesimi = 1, CategoriaId = catId, Barcode = "777", BarcodeAggiuntivi = { new PosTouch.Core.Modelli.BarcodeConfezione { Barcode = "777", Pezzi = 6 } } }); }
                catch (ArgumentException) { interno = true; }
                Ok(interno, "stesso barcode due volte nello stesso articolo rifiutato");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
