using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// Archivio vendite locale (vendite.db). Tabelle e colonne con gli stessi nomi
    /// della versione Windows, per poterle unire in futuro con i dati del PC.
    /// </summary>
    public partial class ArchivioVendite
    {
        private readonly string _connString;
        private readonly object _lock = new object();

        public ArchivioVendite(string cartellaDati)
        {
            _connString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(cartellaDati, "vendite.db"),
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
            CreaSchema();
        }

        /// <summary>
        /// Operatore del turno in corso (gestione operatori): impostato dalla cassa. Null = operatori spenti.
        /// Le vendite e i conti lo registrano senza che chi li emette debba conoscerlo.
        /// </summary>
        public Func<(int id, string nome)?> OperatoreCorrente { get; set; }

        private (int id, string nome) OperatoreDi(int id, string nome)
        {
            if (id > 0) return (id, nome ?? "");
            var c = OperatoreCorrente?.Invoke();
            return c.HasValue && c.Value.id > 0 ? (c.Value.id, c.Value.nome ?? "") : (1, "");   // 1 = come il PC senza operatori
        }

        private SqliteConnection Apri()
        {
            var c = new SqliteConnection(_connString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
            return c;
        }

        private void CreaSchema()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Vendite (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataOra TEXT NOT NULL,
                        NumeroScontrino INTEGER,
                        NumeroAzzeramento INTEGER,
                        TotaleCentesimi INTEGER NOT NULL,
                        MetodoPagamento TEXT NOT NULL,
                        OperatoreId INTEGER DEFAULT 1,
                        StampatoSuRT INTEGER DEFAULT 1,
                        DataEmissioneRT TEXT,
                        ContantiCent INTEGER NOT NULL DEFAULT 0,
                        CartaCent INTEGER NOT NULL DEFAULT 0,
                        Annullato INTEGER DEFAULT 0,
                        OrigineConto TEXT DEFAULT 'ANDROID'
                    );
                    CREATE TABLE IF NOT EXISTS RigheVendita (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        VenditaId INTEGER NOT NULL,
                        ProdottoId INTEGER,
                        CodiceArticolo TEXT NOT NULL,
                        Descrizione TEXT NOT NULL,
                        Quantita REAL NOT NULL,
                        PrezzoCentesimi INTEGER NOT NULL,
                        PrezzoOriginaleCentesimi INTEGER DEFAULT 0,
                        RepartoFiscale INTEGER NOT NULL,
                        CategoriaId INTEGER DEFAULT 0,
                        CategoriaNome TEXT DEFAULT '',
                        AliquotaIVA INTEGER DEFAULT 0,
                        FOREIGN KEY(VenditaId) REFERENCES Vendite(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS IX_Vendite_DataOra ON Vendite(DataOra);
                    CREATE INDEX IF NOT EXISTS IX_RigheVendita_VenditaId ON RigheVendita(VenditaId);

                    -- Conti non fiscali (tasto CONTO): separati dagli scontrini, come sul PC
                    CREATE TABLE IF NOT EXISTS Conti (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataOra TEXT NOT NULL,
                        TotaleCentesimi INTEGER NOT NULL,
                        NumeroProgressivo INTEGER NOT NULL,
                        OperatoreId INTEGER DEFAULT 1,
                        Stampato INTEGER DEFAULT 1
                    );
                    CREATE TABLE IF NOT EXISTS ContiRighe (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ContoId INTEGER NOT NULL,
                        ProdottoId INTEGER,
                        CodiceArticolo TEXT DEFAULT '',
                        Descrizione TEXT NOT NULL,
                        Quantita REAL NOT NULL,
                        PrezzoCentesimi INTEGER NOT NULL,
                        PrezzoOriginaleCentesimi INTEGER DEFAULT 0,
                        ImportoCentesimi INTEGER NOT NULL,
                        RepartoFiscale INTEGER DEFAULT 1,
                        CategoriaId INTEGER DEFAULT 0,
                        CategoriaNome TEXT DEFAULT '',
                        AliquotaIVA INTEGER DEFAULT 0,
                        FOREIGN KEY(ContoId) REFERENCES Conti(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS IX_Conti_DataOra ON Conti(DataOra);
                    CREATE INDEX IF NOT EXISTS IX_ContiRighe_ContoId ON ContiRighe(ContoId);
                    CREATE TABLE IF NOT EXISTS Progressivi (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        ProgressivoConto INTEGER NOT NULL DEFAULT 0,
                        UltimoAggiornamento TEXT
                    );
                    INSERT OR IGNORE INTO Progressivi (Id, ProgressivoConto) VALUES (1, 0);";
                cmd.ExecuteNonQuery();

                // Archivi creati dalle versioni precedenti: aggiunge le colonne nuove (i dati esistenti restano)
                AggiungiColonna(c, "RigheVendita", "CategoriaNome", "TEXT DEFAULT ''");
                AggiungiColonna(c, "RigheVendita", "AliquotaIVA", "INTEGER DEFAULT 0");
                AggiungiColonna(c, "RigheVendita", "Tipo", "INTEGER DEFAULT 0");
                AggiungiColonna(c, "RigheVendita", "IsAPeso", "INTEGER DEFAULT 0");
                AggiungiColonna(c, "RigheVendita", "UnitaPesoRiga", "TEXT");
                AggiungiColonna(c, "ContiRighe", "Tipo", "INTEGER DEFAULT 0");
                AggiungiColonna(c, "ContiRighe", "IsAPeso", "INTEGER DEFAULT 0");
                AggiungiColonna(c, "ContiRighe", "UnitaPesoRiga", "TEXT");
                AggiungiColonna(c, "RigheVendita", "VariantiTesto", "TEXT");   // "+MOZZARELLA|-CIPOLLA": storico completo (regola 5)
                AggiungiColonna(c, "ContiRighe", "VariantiTesto", "TEXT");
                AggiungiColonna(c, "Vendite", "OperatoreNome", "TEXT DEFAULT ''");
                AggiungiColonna(c, "Conti", "OperatoreNome", "TEXT DEFAULT ''");
            }
        }

        private static void AggiungiColonna(SqliteConnection c, string tabella, string colonna, string tipo)
        {
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabella}') WHERE name = $n";
                chk.Parameters.AddWithValue("$n", colonna);
                if (Convert.ToInt64(chk.ExecuteScalar()) > 0) return;
            }
            using var alt = c.CreateCommand();
            alt.CommandText = $"ALTER TABLE {tabella} ADD COLUMN {colonna} {tipo}";
            alt.ExecuteNonQuery();
        }

        /// <summary>Salva vendita e righe in un'unica transazione. Restituisce l'Id.</summary>
        public long Salva(VenditaRegistrata v)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                try
                {
                    long id;
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO Vendite (DataOra, NumeroScontrino, NumeroAzzeramento, TotaleCentesimi, MetodoPagamento,
                                                 StampatoSuRT, DataEmissioneRT, ContantiCent, CartaCent, OperatoreId, OperatoreNome)
                            VALUES ($data, $num, $azz, $tot, $metodo, $rt, $dataRT, $cont, $carta, $op, $opNome);
                            SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("$data", v.DataOra.ToString("yyyy-MM-dd HH:mm:ss"));
                        var (opId, opNome) = OperatoreDi(v.OperatoreId, v.OperatoreNome);
                        cmd.Parameters.AddWithValue("$op", opId);
                        cmd.Parameters.AddWithValue("$opNome", opNome);
                        cmd.Parameters.AddWithValue("$num", v.NumeroScontrino);
                        cmd.Parameters.AddWithValue("$azz", v.NumeroAzzeramento);
                        cmd.Parameters.AddWithValue("$tot", v.TotaleCentesimi);
                        cmd.Parameters.AddWithValue("$metodo", v.MetodoPagamento);
                        cmd.Parameters.AddWithValue("$rt", v.StampatoSuRT ? 1 : 0);
                        cmd.Parameters.AddWithValue("$dataRT", (object)v.DataEmissioneRT ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$cont", v.ContantiCent);
                        cmd.Parameters.AddWithValue("$carta", v.CartaCent);
                        id = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    foreach (var r in v.Righe)
                    {
                        using var cmd = c.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO RigheVendita (VenditaId, ProdottoId, CodiceArticolo, Descrizione, Quantita,
                                                      PrezzoCentesimi, PrezzoOriginaleCentesimi, RepartoFiscale, CategoriaId,
                                                      CategoriaNome, AliquotaIVA, Tipo, IsAPeso, UnitaPesoRiga, VariantiTesto)
                            VALUES ($v, $p, $cod, $desc, $qta, $prezzo, $orig, $rep, $cat, $catNome, $iva, $tipo, $peso, $unita, $vt)";
                        cmd.Parameters.AddWithValue("$v", id);
                        cmd.Parameters.AddWithValue("$p", (object)r.ProdottoId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$cod", r.Barcode ?? "");
                        cmd.Parameters.AddWithValue("$desc", r.Descrizione ?? "");
                        cmd.Parameters.AddWithValue("$qta", (double)r.Quantita);
                        cmd.Parameters.AddWithValue("$prezzo", r.PrezzoCentesimi);
                        cmd.Parameters.AddWithValue("$orig", r.PrezzoOriginaleCentesimi);
                        cmd.Parameters.AddWithValue("$rep", r.RepartoFiscale);
                        cmd.Parameters.AddWithValue("$cat", r.CategoriaId);
                        cmd.Parameters.AddWithValue("$catNome", r.CategoriaNome ?? "");
                        cmd.Parameters.AddWithValue("$iva", r.AliquotaIVA);
                        cmd.Parameters.AddWithValue("$tipo", (int)r.Tipo);
                        cmd.Parameters.AddWithValue("$peso", r.IsAPeso ? 1 : 0);
                        cmd.Parameters.AddWithValue("$unita", (object)r.UnitaPeso ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$vt", (object)r.VariantiTesto ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                    return id;
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    LogManager.Error($"Salvataggio vendita fallito: {ex.Message}");
                    throw;
                }
            }
        }

    }
}
