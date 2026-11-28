using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// Ottimizzazione degli archivi all'uscita dall'app (tasto porta). Riscrive i file togliendo lo spazio lasciato
    /// dai dati modificati o tolti (VACUUM): più piccoli e più veloci da leggere. NON cancella niente:
    /// vendite, conti, storni e tavoli restano tutti. Se qualcosa non va (file occupato, poco spazio) quel file
    /// resta com'è e si riprova alla prossima uscita.
    /// </summary>
    public static class Compattazione
    {
        public static readonly string[] Archivi = { "vendite.db", "tavoli.db", "operatori.db", Catalogo.NomeFile };

        /// <summary>Si compatta solo se lo spazio vuoto è almeno questa quota del file (altrimenti non vale l'attesa).</summary>
        private const double QuotaMinima = 0.10;

        /// <summary>Esito per il log: quanti file compattati e quanto spazio recuperato.</summary>
        public static string Esegui(string cartellaDati)
        {
            var sw = Stopwatch.StartNew();
            int fatti = 0; long recuperati = 0;
            var note = new List<string>();
            foreach (var nome in Archivi)
            {
                string percorso = Path.Combine(cartellaDati ?? "", nome);
                if (!File.Exists(percorso)) continue;
                try
                {
                    long prima = DimensioneTotale(percorso);
                    var cs = new SqliteConnectionStringBuilder { DataSource = percorso, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
                    using (var c = new SqliteConnection(cs))
                    {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA busy_timeout=3000;";   // file occupato (stampa in corso): si salta, non si aspetta
                        cmd.ExecuteNonQuery();
                    }
                    long pagine = Scalare(c, "PRAGMA page_count;");
                    long libere = Scalare(c, "PRAGMA freelist_count;");
                    // spazio su disco: VACUUM ha bisogno di circa una copia del file
                    if (pagine > 0 && (double)libere / pagine >= QuotaMinima && SpazioSufficiente(percorso, prima))
                    {
                        using var v = c.CreateCommand();
                        v.CommandText = "VACUUM;";
                        v.ExecuteNonQuery();
                        fatti++;
                    }
                    using (var o = c.CreateCommand())
                    {
                        // il diario WAL torna a zero e le statistiche delle ricerche si aggiornano
                        o.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize;";
                        o.ExecuteNonQuery();
                    }
                    }   // file chiuso prima di misurarlo
                    recuperati += Math.Max(0, prima - DimensioneTotale(percorso));
                }
                catch (Exception ex)
                {
                    note.Add($"{nome}: {ex.Message}");
                    LogManager.Warning($"COMPATTAZIONE {nome} saltata: {ex.Message}");
                }
            }
            string esito = $"COMPATTAZIONE: {fatti} archivi riscritti, {recuperati / 1024} KB recuperati in {sw.ElapsedMilliseconds} ms"
                           + (note.Count > 0 ? $" (saltati: {note.Count})" : "");
            LogManager.Info(esito);
            return esito;
        }

        private static long Scalare(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }

        private static long DimensioneTotale(string percorso)
        {
            long n = 0;
            foreach (var f in new[] { percorso, percorso + "-wal", percorso + "-shm" })
                try { if (File.Exists(f)) n += new FileInfo(f).Length; } catch { }
            return n;
        }

        private static bool SpazioSufficiente(string percorso, long dimensione)
        {
            try
            {
                var radice = Path.GetPathRoot(Path.GetFullPath(percorso));
                var d = new DriveInfo(string.IsNullOrEmpty(radice) ? "/" : radice);
                return d.AvailableFreeSpace > dimensione * 2 + 20L * 1024 * 1024;
            }
            catch { return true; }   // non si sa: si prova, VACUUM fallisce da solo senza danni se manca spazio
        }
    }
}
