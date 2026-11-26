using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// Creazione e modifica di categorie (con reparto RT) e articoli, direttamente sul tablet.
    /// Scrive nel catalogo con lo schema di pos.db; se il catalogo non esiste lo crea vuoto.
    /// "Nascondi" = Attiva/Attivo a 0 (come il PC); "Elimina" cancella davvero.
    /// Dopo ogni modifica ricarica il <see cref="Catalogo"/> in memoria.
    /// </summary>
    public class GestioneCatalogo
    {
        private readonly Catalogo _catalogo;
        private readonly object _lock = new object();

        public GestioneCatalogo(Catalogo catalogo)
        {
            _catalogo = catalogo;
        }

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
                    cmd.CommandText = @"INSERT INTO Categorie (Nome, RepartoFiscale, AliquotaIVA, Colore, ColoreTestoCategoria, Icona, Ordine, Attiva)
                                        VALUES ($n, $rep, $iva, $col, $colT, $ico, (SELECT IFNULL(MAX(Ordine),0)+1 FROM Categorie), $att)";
                }
                else
                {
                    cmd.CommandText = @"UPDATE Categorie SET Nome=$n, RepartoFiscale=$rep, AliquotaIVA=$iva, Colore=$col,
                                        ColoreTestoCategoria=$colT, Icona=$ico, Attiva=$att WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", c.Id);
                }
                cmd.Parameters.AddWithValue("$n", c.Nome.Trim());
                cmd.Parameters.AddWithValue("$rep", c.RepartoFiscale);
                cmd.Parameters.AddWithValue("$iva", c.AliquotaIVA);
                cmd.Parameters.AddWithValue("$col", c.Colore ?? "#2196F3");
                cmd.Parameters.AddWithValue("$colT", c.ColoreTesto ?? "#000000");
                cmd.Parameters.AddWithValue("$ico", c.Icona ?? "");
                cmd.Parameters.AddWithValue("$att", c.Attiva ? 1 : 0);
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
                    chk.CommandText = "SELECT COUNT(*) FROM Prodotti WHERE CategoriaId=$id";
                    chk.Parameters.AddWithValue("$id", id);
                    long n = (long)chk.ExecuteScalar();
                    if (n > 0) throw new InvalidOperationException($"La categoria contiene {n} articoli: eliminarli o spostarli prima.");
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Categorie WHERE Id=$id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
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
                    cmd.CommandText = "SELECT Id FROM Categorie WHERE Id < 9000 ORDER BY IFNULL(Ordine,0), Nome";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) ordine.Add(r.GetInt32(0));
                }
                int i = ordine.IndexOf(id), j = i + direzione;
                if (i < 0 || j < 0 || j >= ordine.Count) return;
                (ordine[i], ordine[j]) = (ordine[j], ordine[i]);
                for (int k = 0; k < ordine.Count; k++)
                {
                    using var up = conn.CreateCommand();
                    up.CommandText = "UPDATE Categorie SET Ordine=$o WHERE Id=$id";
                    up.Parameters.AddWithValue("$o", k + 1);
                    up.Parameters.AddWithValue("$id", ordine[k]);
                    up.ExecuteNonQuery();
                }
            });
        }

        // ══════════════ ARTICOLI ══════════════

        public void SalvaProdotto(Prodotto p)
        {
            if (string.IsNullOrWhiteSpace(p.Descrizione)) throw new ArgumentException("La descrizione è obbligatoria.");
            if (p.PrezzoCentesimi < 0) throw new ArgumentException("Prezzo non valido.");
            if (p.CategoriaId <= 0) throw new ArgumentException("Scegliere la categoria.");
            string barcode = (p.Barcode ?? "").Trim();

            Esegui(conn =>
            {
                if (barcode.Length > 0)
                {
                    using var chk = conn.CreateCommand();
                    chk.CommandText = "SELECT Descrizione FROM Prodotti WHERE Barcode=$b AND Id<>$id";
                    chk.Parameters.AddWithValue("$b", barcode);
                    chk.Parameters.AddWithValue("$id", p.Id);
                    if (chk.ExecuteScalar() is string altro)
                        throw new ArgumentException($"Barcode già usato da \"{altro}\".");
                }

                using var cmd = conn.CreateCommand();
                if (p.Id == 0)
                {
                    p.Codice = ProssimoCodice(conn);
                    cmd.CommandText = @"INSERT INTO Prodotti (Codice, Barcode, Descrizione, PrezzoCentesimi, CostoCentesimi, CategoriaId, Colore, ColoreTestoTasto, DimensioneFontTasto, IsPreferito, Attivo)
                                        VALUES ($cod, $b, $d, $pr, $costo, $cat, $col, $colT, $font, $pref, $att)";
                    cmd.Parameters.AddWithValue("$cod", p.Codice);
                }
                else
                {
                    cmd.CommandText = @"UPDATE Prodotti SET Barcode=$b, Descrizione=$d, PrezzoCentesimi=$pr, CostoCentesimi=$costo, CategoriaId=$cat,
                                        Colore=$col, ColoreTestoTasto=$colT, DimensioneFontTasto=$font, IsPreferito=$pref, Attivo=$att WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", p.Id);
                }
                cmd.Parameters.AddWithValue("$b", barcode);
                cmd.Parameters.AddWithValue("$d", p.Descrizione.Trim());
                cmd.Parameters.AddWithValue("$pr", p.PrezzoCentesimi);
                cmd.Parameters.AddWithValue("$cat", p.CategoriaId);
                cmd.Parameters.AddWithValue("$col", p.Colore ?? "");
                cmd.Parameters.AddWithValue("$colT", p.ColoreTesto ?? "#FFFFFF");
                cmd.Parameters.AddWithValue("$pref", p.IsPreferito ? 1 : 0);
                cmd.Parameters.AddWithValue("$costo", p.CostoCentesimi);
                cmd.Parameters.AddWithValue("$font", p.DimensioneFontTasto);
                cmd.Parameters.AddWithValue("$att", p.Attivo ? 1 : 0);
                cmd.ExecuteNonQuery();
            });
            LogManager.Info($"Articolo salvato: {p.Descrizione} €{p.PrezzoCentesimi / 100m:F2} cat {p.CategoriaId}");
        }

        /// <summary>👁 Nascondi / 👁 Mostra di più articoli insieme (barra azioni del Back office).</summary>
        public void ImpostaAttivo(IEnumerable<int> ids, bool attivo)
        {
            var lista = new List<int>(ids);
            Esegui(conn =>
            {
                foreach (int id in lista)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE Prodotti SET Attivo=$a WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$a", attivo ? 1 : 0);
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        /// <summary>📂 Sposta più articoli in un'altra categoria.</summary>
        public void SpostaProdotti(IEnumerable<int> ids, int categoriaId)
        {
            var lista = new List<int>(ids);
            Esegui(conn =>
            {
                foreach (int id in lista)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE Prodotti SET CategoriaId=$c WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$c", categoriaId);
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public void EliminaProdotti(IEnumerable<int> ids)
        {
            var lista = new List<int>(ids);
            Esegui(conn =>
            {
                foreach (int id in lista)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM Prodotti WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public void EliminaProdotto(int id)
        {
            Esegui(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Prodotti WHERE Id=$id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            });
        }

        // ══════════════ INTERNI ══════════════

        /// <summary>Codice interno progressivo come sul PC ("001", "002"...).</summary>
        private static string ProssimoCodice(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT IFNULL(MAX(CAST(Codice AS INTEGER)),0) FROM Prodotti WHERE Codice GLOB '[0-9]*'";
            long max = Convert.ToInt64(cmd.ExecuteScalar());
            long n = max + 1;
            // Evita collisioni con codici non numerici o duplicati
            while (true)
            {
                string cod = n.ToString("D3");
                using var chk = conn.CreateCommand();
                chk.CommandText = "SELECT COUNT(*) FROM Prodotti WHERE Codice=$c";
                chk.Parameters.AddWithValue("$c", cod);
                if ((long)chk.ExecuteScalar() == 0) return cod;
                n++;
            }
        }

        private void Esegui(Action<SqliteConnection> azione)
        {
            lock (_lock)
            {
                using (var conn = Catalogo.Apri(_catalogo.Percorso))
                {
                    AssicuraSchema(conn);
                    azione(conn);
                }
                _catalogo.Carica();
            }
        }

        /// <summary>
        /// Crea le tabelle se il catalogo è nuovo e aggiunge le colonne mancanti
        /// se il pos.db importato viene da una versione vecchia.
        /// </summary>
        private static void AssicuraSchema(SqliteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Categorie (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Nome TEXT NOT NULL UNIQUE,
                        RepartoFiscale INTEGER NOT NULL,
                        AliquotaIVA INTEGER NOT NULL,
                        Colore TEXT
                    );
                    CREATE TABLE IF NOT EXISTS Prodotti (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Codice TEXT NOT NULL UNIQUE,
                        Descrizione TEXT NOT NULL,
                        PrezzoCentesimi INTEGER NOT NULL,
                        CategoriaId INTEGER NOT NULL,
                        PathImmagine TEXT,
                        Colore TEXT,
                        IsPreferito INTEGER DEFAULT 0,
                        FOREIGN KEY(CategoriaId) REFERENCES Categorie(Id)
                    );";
                cmd.ExecuteNonQuery();
            }
            AggiungiColonna(conn, "Categorie", "Ordine", "INTEGER DEFAULT 0");
            AggiungiColonna(conn, "Categorie", "Attiva", "INTEGER DEFAULT 1");
            AggiungiColonna(conn, "Categorie", "ColoreTestoCategoria", "TEXT DEFAULT '#000000'");
            AggiungiColonna(conn, "Prodotti", "Barcode", "TEXT");
            AggiungiColonna(conn, "Prodotti", "ColoreTestoTasto", "TEXT DEFAULT '#FFFFFF'");
            AggiungiColonna(conn, "Prodotti", "Attivo", "INTEGER DEFAULT 1");
            AggiungiColonna(conn, "Prodotti", "CostoCentesimi", "INTEGER DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "DimensioneFontTasto", "INTEGER DEFAULT 12");
            AggiungiColonna(conn, "Categorie", "Icona", "TEXT DEFAULT ''");
        }

        private static void AggiungiColonna(SqliteConnection conn, string tabella, string colonna, string tipo)
        {
            var esistenti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({tabella})";
                using var r = cmd.ExecuteReader();
                while (r.Read()) esistenti.Add(r.GetString(1));
            }
            if (esistenti.Contains(colonna)) return;
            using var alt = conn.CreateCommand();
            alt.CommandText = $"ALTER TABLE {tabella} ADD COLUMN {colonna} {tipo}";
            alt.ExecuteNonQuery();
        }
    }
}
