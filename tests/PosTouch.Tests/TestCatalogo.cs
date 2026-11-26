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
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
