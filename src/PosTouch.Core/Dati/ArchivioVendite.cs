using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>Vendita da registrare nell'archivio locale.</summary>
    public class VenditaRegistrata
    {
        public DateTime DataOra { get; set; } = DateTime.Now;
        public int NumeroScontrino { get; set; }
        public int NumeroAzzeramento { get; set; }
        public int TotaleCentesimi { get; set; }
        public string MetodoPagamento { get; set; } = MetodiPagamento.Contanti;
        public int ContantiCent { get; set; }
        public int CartaCent { get; set; }
        public bool StampatoSuRT { get; set; } = true;
        /// <summary>Data/ora emissione letta dall'RT (yyyy-MM-dd HH:mm), se disponibile.</summary>
        public string DataEmissioneRT { get; set; }
        public List<RigaScontrino> Righe { get; set; } = new List<RigaScontrino>();
    }

    public class TotaliGiorno
    {
        public int NumeroScontrini { get; set; }
        public int TotaleCent { get; set; }
        public int ContantiCent { get; set; }
        public int CartaCent { get; set; }
    }

    /// <summary>
    /// Archivio vendite locale (vendite.db). Tabelle e colonne con gli stessi nomi
    /// della versione Windows, per poterle unire in futuro con i dati del PC.
    /// </summary>
    public class ArchivioVendite
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
                    CREATE INDEX IF NOT EXISTS IX_Vendite_DataOra ON Vendite(DataOra);";
                cmd.ExecuteNonQuery();

                // Archivi creati dalle versioni precedenti: aggiunge le colonne nuove (i dati esistenti restano)
                AggiungiColonna(c, "RigheVendita", "CategoriaNome", "TEXT DEFAULT ''");
                AggiungiColonna(c, "RigheVendita", "AliquotaIVA", "INTEGER DEFAULT 0");
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
                                                 StampatoSuRT, DataEmissioneRT, ContantiCent, CartaCent)
                            VALUES ($data, $num, $azz, $tot, $metodo, $rt, $dataRT, $cont, $carta);
                            SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("$data", v.DataOra.ToString("yyyy-MM-dd HH:mm:ss"));
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
                                                      CategoriaNome, AliquotaIVA)
                            VALUES ($v, $p, $cod, $desc, $qta, $prezzo, $orig, $rep, $cat, $catNome, $iva)";
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

        public TotaliGiorno TotaliDi(DateTime giorno)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*), IFNULL(SUM(TotaleCentesimi),0), IFNULL(SUM(ContantiCent),0), IFNULL(SUM(CartaCent),0)
                    FROM Vendite WHERE Annullato = 0 AND DataOra >= $da AND DataOra < $a";
                cmd.Parameters.AddWithValue("$da", giorno.Date.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$a", giorno.Date.AddDays(1).ToString("yyyy-MM-dd"));
                using var r = cmd.ExecuteReader();
                r.Read();
                return new TotaliGiorno
                {
                    NumeroScontrini = r.GetInt32(0),
                    TotaleCent = Convert.ToInt32(r.GetInt64(1)),
                    ContantiCent = Convert.ToInt32(r.GetInt64(2)),
                    CartaCent = Convert.ToInt32(r.GetInt64(3))
                };
            }
        }
    }
}
