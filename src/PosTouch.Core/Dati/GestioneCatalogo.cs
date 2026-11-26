using System;
using System.Collections.Generic;
using System.Linq;
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
    public partial class GestioneCatalogo
    {
        private readonly Catalogo _catalogo;
        private readonly object _lock = new object();

        public GestioneCatalogo(Catalogo catalogo)
        {
            _catalogo = catalogo;
        }

        // ══════════════ INTERNI ══════════════

        /// <summary>Codice interno progressivo come sul PC ("001", "002"...).</summary>
        private static string ProssimoCodice(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            // solo codici numerici "normali" (fino a 9 cifre): un codice lunghissimo non deve mandare in overflow
            cmd.CommandText = "SELECT IFNULL(MAX(CAST(Codice AS INTEGER)),0) FROM Prodotti WHERE Codice GLOB '[0-9]*' AND Codice NOT GLOB '*[^0-9]*' AND LENGTH(Codice) <= 9";
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
            AggiungiColonna(conn, "Categorie", "CentroProduzioneId", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Categorie", "EscludiComandaBanco", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "CentroProduzioneId", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "VenditaAPeso", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "UnitaPeso", "TEXT DEFAULT 'Kg'");
            AggiungiColonna(conn, "Prodotti", "IsCodiceBilancia", "INTEGER DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "IsCombinazione", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "NumeroGusti", "INTEGER NOT NULL DEFAULT 2");
            AggiungiColonna(conn, "Prodotti", "PoliticaPrezzo", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "VariantiProporzionali", "INTEGER NOT NULL DEFAULT 0");
            AggiungiColonna(conn, "Prodotti", "IsCoperto", "INTEGER NOT NULL DEFAULT 0");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Varianti (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Nome TEXT NOT NULL,
                        Gruppo TEXT DEFAULT '',
                        PrezzoAggiuntaCent INTEGER NOT NULL DEFAULT 0,
                        PrezzoRimozioneCent INTEGER NOT NULL DEFAULT 0,
                        Ordine INTEGER DEFAULT 0,
                        Attiva INTEGER DEFAULT 1
                    );
                    CREATE TABLE IF NOT EXISTS CategorieVarianti (
                        CategoriaId INTEGER NOT NULL,
                        VarianteId INTEGER NOT NULL,
                        PRIMARY KEY (CategoriaId, VarianteId)
                    );";
                cmd.ExecuteNonQuery();
            }
            AggiungiColonna(conn, "Varianti", "Tipo", "INTEGER NOT NULL DEFAULT 0");
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
