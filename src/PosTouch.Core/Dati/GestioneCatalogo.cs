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
    /// "Elimina" nasconde (Attiva/Attivo = 0) come fa il PC, così lo storico vendite resta coerente.
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
                        if (c.Id == 0 && r.GetInt32(1) == 0)
                        {
                            // Esiste nascosta con lo stesso nome: la riattivo invece di crearne un'altra
                            c.Id = r.GetInt32(0);
                        }
                        else throw new ArgumentException($"Esiste già una categoria \"{c.Nome.Trim()}\".");
                    }
                }

                using var cmd = conn.CreateCommand();
                if (c.Id == 0)
                {
                    cmd.CommandText = @"INSERT INTO Categorie (Nome, RepartoFiscale, AliquotaIVA, Colore, ColoreTestoCategoria, Ordine, Attiva)
                                        VALUES ($n, $rep, $iva, $col, $colT, $ord, 1)";
                }
                else
                {
                    cmd.CommandText = @"UPDATE Categorie SET Nome=$n, RepartoFiscale=$rep, AliquotaIVA=$iva, Colore=$col,
                                        ColoreTestoCategoria=$colT, Ordine=$ord, Attiva=1 WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", c.Id);
                }
                cmd.Parameters.AddWithValue("$n", c.Nome.Trim());
                cmd.Parameters.AddWithValue("$rep", c.RepartoFiscale);
                cmd.Parameters.AddWithValue("$iva", c.AliquotaIVA);
                cmd.Parameters.AddWithValue("$col", c.Colore ?? "#2196F3");
                cmd.Parameters.AddWithValue("$colT", c.ColoreTesto ?? "#FFFFFF");
                cmd.Parameters.AddWithValue("$ord", c.Ordine);
                cmd.ExecuteNonQuery();
            });
            LogManager.Info($"Categoria salvata: {c.Nome} (reparto {c.RepartoFiscale}, IVA {c.AliquotaIVA}%)");
        }

        /// <summary>Nasconde la categoria. Rifiuta se contiene articoli attivi.</summary>
        public void EliminaCategoria(int id)
        {
            Esegui(conn =>
            {
                using (var chk = conn.CreateCommand())
                {
                    chk.CommandText = "SELECT COUNT(*) FROM Prodotti WHERE CategoriaId=$id AND IFNULL(Attivo,1)=1";
                    chk.Parameters.AddWithValue("$id", id);
                    long n = (long)chk.ExecuteScalar();
                    if (n > 0) throw new InvalidOperationException($"La categoria contiene {n} articoli: eliminarli o spostarli prima.");
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Categorie SET Attiva=0 WHERE Id=$id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
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
                    chk.CommandText = "SELECT Descrizione FROM Prodotti WHERE Barcode=$b AND Id<>$id AND IFNULL(Attivo,1)=1";
                    chk.Parameters.AddWithValue("$b", barcode);
                    chk.Parameters.AddWithValue("$id", p.Id);
                    if (chk.ExecuteScalar() is string altro)
                        throw new ArgumentException($"Barcode già usato da \"{altro}\".");
                }

                using var cmd = conn.CreateCommand();
                if (p.Id == 0)
                {
                    p.Codice = ProssimoCodice(conn);
                    cmd.CommandText = @"INSERT INTO Prodotti (Codice, Barcode, Descrizione, PrezzoCentesimi, CategoriaId, Colore, ColoreTestoTasto, IsPreferito, Attivo)
                                        VALUES ($cod, $b, $d, $pr, $cat, $col, $colT, $pref, 1)";
                    cmd.Parameters.AddWithValue("$cod", p.Codice);
                }
                else
                {
                    cmd.CommandText = @"UPDATE Prodotti SET Barcode=$b, Descrizione=$d, PrezzoCentesimi=$pr, CategoriaId=$cat,
                                        Colore=$col, ColoreTestoTasto=$colT, IsPreferito=$pref, Attivo=1 WHERE Id=$id";
                    cmd.Parameters.AddWithValue("$id", p.Id);
                }
                cmd.Parameters.AddWithValue("$b", barcode);
                cmd.Parameters.AddWithValue("$d", p.Descrizione.Trim());
                cmd.Parameters.AddWithValue("$pr", p.PrezzoCentesimi);
                cmd.Parameters.AddWithValue("$cat", p.CategoriaId);
                cmd.Parameters.AddWithValue("$col", p.Colore ?? "");
                cmd.Parameters.AddWithValue("$colT", p.ColoreTesto ?? "#FFFFFF");
                cmd.Parameters.AddWithValue("$pref", p.IsPreferito ? 1 : 0);
                cmd.ExecuteNonQuery();
            });
            LogManager.Info($"Articolo salvato: {p.Descrizione} €{p.PrezzoCentesimi / 100m:F2} cat {p.CategoriaId}");
        }

        public void EliminaProdotto(int id)
        {
            Esegui(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Prodotti SET Attivo=0 WHERE Id=$id";
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
