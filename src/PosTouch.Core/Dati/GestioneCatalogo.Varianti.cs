using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    public partial class GestioneCatalogo
    {
        // ══════════════ VARIANTI ══════════════

        /// <summary>Crea o aggiorna una variante e le categorie in cui è disponibile.</summary>
        public void SalvaVariante(Variante v)
        {
            if (string.IsNullOrWhiteSpace(v.Nome)) throw new ArgumentException("Il nome della variante è obbligatorio.");
            if (v.PrezzoAggiuntaCent < 0 || v.PrezzoRimozioneCent < 0) throw new ArgumentException("I prezzi della variante non possono essere negativi.");
            int id = 0;
            Esegui(conn =>
            {
                using var tx = conn.BeginTransaction();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = v.Id == 0
                        ? @"INSERT INTO Varianti (Nome, Gruppo, PrezzoAggiuntaCent, PrezzoRimozioneCent, Ordine, Attiva, Tipo)
                            VALUES ($n, $g, $pa, $pr, $o, $a, $t); SELECT last_insert_rowid();"
                        : @"UPDATE Varianti SET Nome=$n, Gruppo=$g, PrezzoAggiuntaCent=$pa, PrezzoRimozioneCent=$pr, Ordine=$o, Attiva=$a, Tipo=$t
                            WHERE Id=$id; SELECT $id;";
                    cmd.Parameters.AddWithValue("$n", v.Nome.Trim().ToUpperInvariant());
                    cmd.Parameters.AddWithValue("$g", (v.Gruppo ?? "").Trim().ToUpperInvariant());
                    cmd.Parameters.AddWithValue("$pa", v.PrezzoAggiuntaCent);
                    cmd.Parameters.AddWithValue("$pr", v.PrezzoRimozioneCent);
                    cmd.Parameters.AddWithValue("$o", v.Ordine);
                    cmd.Parameters.AddWithValue("$a", v.Attiva ? 1 : 0);
                    cmd.Parameters.AddWithValue("$t", (int)v.Tipo);
                    cmd.Parameters.AddWithValue("$id", v.Id);
                    id = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM CategorieVarianti WHERE VarianteId=$id";
                    del.Parameters.AddWithValue("$id", id);
                    del.ExecuteNonQuery();
                }
                foreach (int cat in v.Categorie.Distinct())
                {
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT OR IGNORE INTO CategorieVarianti (CategoriaId, VarianteId) VALUES ($c, $v)";
                    ins.Parameters.AddWithValue("$c", cat);
                    ins.Parameters.AddWithValue("$v", id);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
                v.Id = id;   // solo a salvataggio riuscito: con un errore la variante resta "nuova" e si reinserisce
            });
            LogManager.Info($"Variante salvata: {v.Nome} (+{v.PrezzoAggiuntaCent} / -{v.PrezzoRimozioneCent}) su {v.Categorie.Count} categorie");
        }

        public void EliminaVariante(int id)
        {
            Esegui(conn =>
            {
                using var tx = conn.BeginTransaction();
                foreach (var sql in new[] { "DELETE FROM CategorieVarianti WHERE VarianteId=$id", "DELETE FROM Varianti WHERE Id=$id" })
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }
    }
}
