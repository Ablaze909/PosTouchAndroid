using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// Barcode degli articoli: lettere e cifre (CODE39/128 hanno lettere), più barcode per articolo
    /// e pezzi per confezione (tabella ProdottiBarcode del PC). Un barcode vale per un solo articolo.
    /// </summary>
    public partial class GestioneCatalogo
    {
        public const int LunghezzaMaxBarcode = 48;
        public const int PezziMaxConfezione = 9999;

        /// <summary>Spazi ai lati e caratteri invisibili tolti (i lettori a volte mandano INVIO o TAB).</summary>
        public static string NormalizzaBarcode(string codice)
            => new string((codice ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();

        /// <summary>Controlli che non serve il database: lunghezza, pezzi, doppioni nello stesso articolo.</summary>
        private static List<BarcodeConfezione> ControllaBarcode(string principale, IEnumerable<BarcodeConfezione> altri)
        {
            if (principale.Length > LunghezzaMaxBarcode) throw new ArgumentException($"Barcode troppo lungo (massimo {LunghezzaMaxBarcode} caratteri).");
            var visti = new HashSet<string>(StringComparer.Ordinal);
            if (principale.Length > 0) visti.Add(principale);
            var ris = new List<BarcodeConfezione>();
            foreach (var b in altri ?? Enumerable.Empty<BarcodeConfezione>())
            {
                string codice = NormalizzaBarcode(b?.Barcode);
                if (codice.Length == 0) continue;
                if (codice.Length > LunghezzaMaxBarcode) throw new ArgumentException($"Barcode \"{codice}\" troppo lungo (massimo {LunghezzaMaxBarcode} caratteri).");
                if (b.Pezzi < 1 || b.Pezzi > PezziMaxConfezione) throw new ArgumentException($"Pezzi della confezione {codice} non validi (da 1 a {PezziMaxConfezione}).");
                if (!visti.Add(codice)) throw new ArgumentException($"Il barcode {codice} è inserito due volte.");
                ris.Add(new BarcodeConfezione { Barcode = codice, Pezzi = b.Pezzi });
            }
            return ris;
        }

        /// <summary>Nessun barcode di questo articolo può essere già di un altro (articoli eliminati sul PC esclusi).</summary>
        private static void ControllaBarcodeLiberi(SqliteConnection conn, SqliteTransaction tx, int id, string principale, List<BarcodeConfezione> altri)
        {
            var tutti = new List<string>();
            if (principale.Length > 0) tutti.Add(principale);
            tutti.AddRange(altri.Select(a => a.Barcode));
            foreach (var codice in tutti)
            {
                using var chk = conn.CreateCommand();
                chk.Transaction = tx;
                chk.CommandText = @"SELECT Descrizione FROM Prodotti WHERE TRIM(Barcode)=$b AND Id<>$id AND IFNULL(Attivo,1) >= 0
                                    UNION ALL
                                    SELECT p.Descrizione FROM ProdottiBarcode b JOIN Prodotti p ON p.Id = b.ProdottoId
                                    WHERE TRIM(b.Barcode)=$b AND b.ProdottoId<>$id AND IFNULL(p.Attivo,1) >= 0
                                    LIMIT 1";
                chk.Parameters.AddWithValue("$b", codice);
                chk.Parameters.AddWithValue("$id", id);
                if (chk.ExecuteScalar() is string altro)
                    throw new ArgumentException($"Il barcode {codice} è già usato da \"{altro}\".");
            }
        }

        /// <summary>
        /// Riscrive i barcode dell'articolo nella tabella del PC: il principale (1 pezzo, come fa il PC) e gli aggiuntivi.
        /// </summary>
        private static void SalvaBarcodeAggiuntivi(SqliteConnection conn, SqliteTransaction tx, int id, string principale, List<BarcodeConfezione> altri)
        {
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM ProdottiBarcode WHERE ProdottoId=$id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }
            var righe = new List<BarcodeConfezione>();
            if (principale.Length > 0) righe.Add(new BarcodeConfezione { Barcode = principale, Pezzi = 1 });
            righe.AddRange(altri);
            foreach (var b in righe)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT OR IGNORE INTO ProdottiBarcode (ProdottoId, Barcode, QuantitaConfezione) VALUES ($id, $b, $q)";
                ins.Parameters.AddWithValue("$id", id);
                ins.Parameters.AddWithValue("$b", b.Barcode);
                ins.Parameters.AddWithValue("$q", b.Pezzi);
                ins.ExecuteNonQuery();
            }
        }
    }
}
