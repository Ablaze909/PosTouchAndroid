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
        // ══════════════ CATEGORIE ══════════════

        public void SalvaCategoria(Categoria c)
        {
            if (string.IsNullOrWhiteSpace(c.Nome)) throw new ArgumentException("Il nome della categoria è obbligatorio.");
            if (c.RepartoFiscale < 1 || c.RepartoFiscale > 99) throw new ArgumentException("Reparto RT non valido (1-99).");

            Esegui(conn =>
            {
                // Nome unico (anche tra le categorie nascoste)
                using (var chk = conn.CreateCommand())
                {
                    chk.CommandText = "SELECT Id, IFNULL(Attiva,1) FROM Categorie WHERE Nome = $n COLLATE NOCASE AND Id <> $id";
                    chk.Parameters.AddWithValue("$n", c.Nome.Trim());
                    chk.Parameters.AddWithValue("$id", c.Id);
                    using var r = chk.ExecuteReader();
                    if (r.Read())
                    {
                        throw new ArgumentException($"Esiste già una categoria \"{c.Nome.Trim()}\"" +
                            (r.GetInt32(1) == 0 ? " (nascosta)." : "."));
                    }
                }

                using var cmd = conn.CreateCommand();
                if (c.Id == 0)
                {
                    // Id esplicito sotto 8000: nel pos.db del PC 8000-8999 sono Gratta e Vinci e 9000+ tabacchi;
                    // l'autoincremento darebbe Id riservati (categoria nuova invisibile nella vendita)
                    cmd.CommandText = @"INSERT INTO Categorie (Id, Nome, RepartoFiscale, AliquotaIVA, Colore, ColoreTestoCategoria, Icona, Ordine, Attiva, CentroProduzioneId, EscludiComandaBanco, Subito)
                                        VALUES ((SELECT IFNULL(MAX(Id),0)+1 FROM Categorie WHERE Id < 8000), $n, $rep, $iva, $col, $colT, $ico,
                                                (SELECT IFNULL(MAX(Ordine),0)+1 FROM Categorie WHERE Id < 8000), $att, $centro, $escBanco, $subito)";
                }
                else
                {
                    cmd.CommandText = @"UPDATE Categorie SET Nome=$n, RepartoFiscale=$rep, AliquotaIVA=$iva, Colore=$col,
                                        ColoreTestoCategoria=$colT, Icona=$ico, Attiva=$att, CentroProduzioneId=$centro, EscludiComandaBanco=$escBanco, Subito=$subito WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", c.Id);
                }
                cmd.Parameters.AddWithValue("$n", c.Nome.Trim());
                cmd.Parameters.AddWithValue("$rep", c.RepartoFiscale);
                cmd.Parameters.AddWithValue("$iva", c.AliquotaIVA);
                cmd.Parameters.AddWithValue("$col", c.Colore ?? "#2196F3");
                cmd.Parameters.AddWithValue("$colT", c.ColoreTesto ?? "#000000");
                cmd.Parameters.AddWithValue("$ico", c.Icona ?? "");
                cmd.Parameters.AddWithValue("$att", c.Attiva ? 1 : 0);
                cmd.Parameters.AddWithValue("$centro", c.CentroProduzioneId);
                cmd.Parameters.AddWithValue("$escBanco", c.EscludiComandaBanco ? 1 : 0);
                cmd.Parameters.AddWithValue("$subito", c.Subito ? 1 : 0);
                cmd.ExecuteNonQuery();
            });
            LogManager.Info($"Categoria salvata: {c.Nome} (reparto {c.RepartoFiscale}, IVA {c.AliquotaIVA}%)");
        }

        /// <summary>Elimina la categoria. Rifiuta se contiene articoli (anche nascosti).</summary>
        public void EliminaCategoria(int id)
        {
            Esegui(conn =>
            {
                using (var chk = conn.CreateCommand())
                {
                    chk.CommandText = "SELECT COUNT(*) FROM Prodotti WHERE CategoriaId=$id AND IFNULL(Attivo,1) >= 0";
                    chk.Parameters.AddWithValue("$id", id);
                    long n = (long)chk.ExecuteScalar();
                    if (n > 0) throw new InvalidOperationException($"La categoria contiene {n} articoli: eliminarli o spostarli prima.");
                }
                // articoli già eliminati sul PC (Attivo = -1) e varianti collegate: via insieme alla categoria
                using var tx = conn.BeginTransaction();
                foreach (var sql in new[] { "DELETE FROM Prodotti WHERE CategoriaId=$id AND IFNULL(Attivo,1) < 0",
                                            "DELETE FROM CategorieVarianti WHERE CategoriaId=$id",
                                            "DELETE FROM Categorie WHERE Id=$id" })
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

        /// <summary>Sposta la categoria su (-1) o giù (+1) nell'ordine (frecce ▲▼ del Back office).</summary>
        public void SpostaCategoria(int id, int direzione)
        {
            Esegui(conn =>
            {
                var ordine = new List<int>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Categorie WHERE Id < 8000 ORDER BY IFNULL(Ordine,0), Nome";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) ordine.Add(r.GetInt32(0));
                }
                int i = ordine.IndexOf(id), j = i + direzione;
                if (i < 0 || j < 0 || j >= ordine.Count) return;
                (ordine[i], ordine[j]) = (ordine[j], ordine[i]);
                using var tx = conn.BeginTransaction();   // un solo salvataggio su disco, niente ordini duplicati se si interrompe
                for (int k = 0; k < ordine.Count; k++)
                {
                    using var up = conn.CreateCommand();
                    up.Transaction = tx;
                    up.CommandText = "UPDATE Categorie SET Ordine=$o WHERE Id=$id";
                    up.Parameters.AddWithValue("$o", k + 1);
                    up.Parameters.AddWithValue("$id", ordine[k]);
                    up.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }
    }
}
