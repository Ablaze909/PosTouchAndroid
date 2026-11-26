using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    public class TavoloAperto
    {
        public long Id { get; set; }
        public int SalaId { get; set; }
        public int Numero { get; set; }
        public DateTime Apertura { get; set; }
        public bool Preconto { get; set; }
        /// <summary>Ultima portata mandata con VAI (AVANZA).</summary>
        public int PortataCorrente { get; set; }
        // riepilogo per la mappa tavoli
        public int TotaleCent { get; set; }
        public int Articoli { get; set; }
        public bool DaComandare { get; set; }
        /// <summary>Portata più alta presente (0 = senza portate).</summary>
        public int PortataMassima { get; set; }
    }

    public enum TipoJob { Comanda = 0, Storno = 1, Vai = 2 }
    public enum StatoJob { InAttesa = 0, Errore = 1, Fatto = 2 }

    /// <summary>Stampa in coda (byte ESC/POS già pronti), salvata nel database: non si perde se la stampante è spenta.</summary>
    public class JobStampa
    {
        public long Id { get; set; }
        public DateTime Creato { get; set; }
        public TipoJob Tipo { get; set; }
        public string Descrizione { get; set; } = "";
        public string Stampante { get; set; } = "";
        public string IP { get; set; } = "";
        public int Porta { get; set; } = 9100;
        public byte[] Dati { get; set; }
        public StatoJob Stato { get; set; }
        public int Tentativi { get; set; }
        public string UltimoErrore { get; set; }
    }

    /// <summary>Registro storni (per le statistiche), come StornoComande del PC.</summary>
    public class StornoRegistrato
    {
        public string Sala { get; set; } = "";
        public string Tavolo { get; set; } = "";
        public string Centro { get; set; } = "";
        public string Descrizione { get; set; } = "";
        public decimal Quantita { get; set; }
        public int PrezzoCentesimi { get; set; }
        public bool Stampato { get; set; }
    }

    /// <summary>
    /// tavoli.db: tavoli aperti con le loro righe, coda di stampa comande, progressivo comande, registro storni.
    /// Ogni salvataggio di un tavolo è UNA transazione insieme alle stampe da accodare:
    /// le righe diventano "comandate" solo se la comanda è davvero in coda (niente ristampe, niente perdite).
    /// </summary>
    public class ArchivioTavoli
    {
        private readonly string _connString;
        private readonly object _lock = new object();

        public ArchivioTavoli(string cartellaDati)
        {
            _connString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(cartellaDati, "tavoli.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
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
                    CREATE TABLE IF NOT EXISTS TavoliAperti (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SalaId INTEGER NOT NULL,
                        Numero INTEGER NOT NULL,
                        DataApertura TEXT NOT NULL,
                        Preconto INTEGER NOT NULL DEFAULT 0,
                        PortataCorrente INTEGER NOT NULL DEFAULT 0,
                        UNIQUE(SalaId, Numero)
                    );
                    CREATE TABLE IF NOT EXISTS RigheTavolo (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TavoloId INTEGER NOT NULL,
                        Ordine INTEGER NOT NULL,
                        Tipo INTEGER NOT NULL,
                        ProdottoId INTEGER,
                        Descrizione TEXT NOT NULL,
                        Barcode TEXT DEFAULT '',
                        Quantita REAL NOT NULL,
                        PrezzoCentesimi INTEGER NOT NULL,
                        PrezzoOriginaleCentesimi INTEGER DEFAULT 0,
                        RepartoFiscale INTEGER DEFAULT 0,
                        CategoriaId INTEGER DEFAULT 0,
                        CategoriaNome TEXT DEFAULT '',
                        AliquotaIVA INTEGER DEFAULT 0,
                        UnitaPeso TEXT,
                        ScontoPercentualeCent INTEGER,
                        CentroProduzioneId INTEGER NOT NULL DEFAULT 0,
                        Portata INTEGER NOT NULL DEFAULT 0,
                        Comandata INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY(TavoloId) REFERENCES TavoliAperti(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS IX_RigheTavolo_TavoloId ON RigheTavolo(TavoloId);
                    CREATE TABLE IF NOT EXISTS CodaStampa (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataCreazione TEXT NOT NULL,
                        Tipo INTEGER NOT NULL,
                        Descrizione TEXT NOT NULL,
                        Stampante TEXT NOT NULL,
                        IP TEXT NOT NULL,
                        Porta INTEGER NOT NULL,
                        Dati BLOB NOT NULL,
                        Stato INTEGER NOT NULL DEFAULT 0,
                        Tentativi INTEGER NOT NULL DEFAULT 0,
                        UltimoErrore TEXT,
                        UltimoTentativo TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_CodaStampa_Stato ON CodaStampa(Stato);
                    CREATE TABLE IF NOT EXISTS ProgressivoComanda (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        Numero INTEGER NOT NULL DEFAULT 0,
                        Data TEXT NOT NULL DEFAULT ''
                    );
                    INSERT OR IGNORE INTO ProgressivoComanda (Id, Numero, Data) VALUES (1, 0, '');
                    CREATE TABLE IF NOT EXISTS Storni (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataOra TEXT NOT NULL,
                        Sala TEXT, Tavolo TEXT, Centro TEXT,
                        Descrizione TEXT NOT NULL,
                        Quantita REAL NOT NULL,
                        PrezzoCentesimi INTEGER NOT NULL,
                        Stampato INTEGER NOT NULL DEFAULT 1
                    );";
                cmd.ExecuteNonQuery();
            }
        }

        // ══════════════ TAVOLI ══════════════

        public List<TavoloAperto> Aperti()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    SELECT t.Id, t.SalaId, t.Numero, t.DataApertura, t.Preconto, t.PortataCorrente,
                           IFNULL((SELECT SUM(CAST(ROUND(r.Quantita * r.PrezzoCentesimi) AS INTEGER)) FROM RigheTavolo r
                                   WHERE r.TavoloId = t.Id AND r.Tipo IN (0, 1)), 0),
                           (SELECT COUNT(*) FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.Tipo = 0),
                           EXISTS (SELECT 1 FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.Tipo = 0 AND r.Comandata = 0 AND r.CentroProduzioneId > 0),
                           IFNULL((SELECT MAX(r.Portata) FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.Tipo = 4), 0)
                    FROM TavoliAperti t";
                var lista = new List<TavoloAperto>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    lista.Add(new TavoloAperto
                    {
                        Id = r.GetInt64(0), SalaId = r.GetInt32(1), Numero = r.GetInt32(2),
                        Apertura = DateTime.TryParse(r.GetString(3), out var d) ? d : DateTime.Now,
                        Preconto = r.GetInt32(4) == 1, PortataCorrente = r.GetInt32(5),
                        TotaleCent = Convert.ToInt32(r.GetInt64(6)), Articoli = r.GetInt32(7), DaComandare = r.GetInt32(8) == 1,
                        PortataMassima = r.GetInt32(9)
                    });
                return lista;
            }
        }

        /// <summary>Tavolo aperto (lo crea se libero).</summary>
        public TavoloAperto ApriTavolo(int salaId, int numero)
        {
            lock (_lock)
            {
                using var c = Apri();
                using (var ins = c.CreateCommand())
                {
                    ins.CommandText = "INSERT OR IGNORE INTO TavoliAperti (SalaId, Numero, DataApertura) VALUES ($s, $n, $d)";
                    ins.Parameters.AddWithValue("$s", salaId);
                    ins.Parameters.AddWithValue("$n", numero);
                    ins.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    ins.ExecuteNonQuery();
                }
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Id, DataApertura, Preconto, PortataCorrente FROM TavoliAperti WHERE SalaId=$s AND Numero=$n";
                cmd.Parameters.AddWithValue("$s", salaId);
                cmd.Parameters.AddWithValue("$n", numero);
                using var r = cmd.ExecuteReader();
                r.Read();
                return new TavoloAperto
                {
                    Id = r.GetInt64(0), SalaId = salaId, Numero = numero,
                    Apertura = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now,
                    Preconto = r.GetInt32(2) == 1, PortataCorrente = r.GetInt32(3)
                };
            }
        }

        public List<RigaScontrino> Righe(long tavoloId)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"SELECT Tipo, ProdottoId, Descrizione, Barcode, Quantita, PrezzoCentesimi, PrezzoOriginaleCentesimi,
                                           RepartoFiscale, CategoriaId, CategoriaNome, AliquotaIVA, UnitaPeso, ScontoPercentualeCent,
                                           CentroProduzioneId, Portata, Comandata
                                    FROM RigheTavolo WHERE TavoloId=$t ORDER BY Ordine";
                cmd.Parameters.AddWithValue("$t", tavoloId);
                var lista = new List<RigaScontrino>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    lista.Add(new RigaScontrino
                    {
                        Tipo = (TipoRiga)r.GetInt32(0),
                        ProdottoId = r.IsDBNull(1) ? null : r.GetInt32(1),
                        Descrizione = r.GetString(2),
                        Barcode = r.IsDBNull(3) ? "" : r.GetString(3),
                        Quantita = (decimal)r.GetDouble(4),
                        PrezzoCentesimi = r.GetInt32(5),
                        PrezzoOriginaleCentesimi = r.GetInt32(6),
                        RepartoFiscale = r.GetInt32(7),
                        CategoriaId = r.GetInt32(8),
                        CategoriaNome = r.IsDBNull(9) ? "" : r.GetString(9),
                        AliquotaIVA = r.GetInt32(10),
                        UnitaPeso = r.IsDBNull(11) ? null : r.GetString(11),
                        ScontoPercentualeCent = r.IsDBNull(12) ? null : r.GetInt32(12),
                        CentroProduzioneId = r.GetInt32(13),
                        Portata = r.GetInt32(14),
                        Comandata = r.GetInt32(15) == 1
                    });
                return lista;
            }
        }

        /// <summary>
        /// Salva lo stato del tavolo (memoria = fonte autorevole) insieme a stampe e storni, in un'unica transazione.
        /// Tavolo senza righe = tavolo liberato.
        /// </summary>
        public void Salva(TavoloAperto t, IList<RigaScontrino> righe, IEnumerable<JobStampa> stampe = null, IEnumerable<StornoRegistrato> storni = null)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                try
                {
                    Esegui(c, tx, "DELETE FROM RigheTavolo WHERE TavoloId=$t", ("$t", t.Id));
                    if (righe == null || righe.Count == 0)
                    {
                        Esegui(c, tx, "DELETE FROM TavoliAperti WHERE Id=$t", ("$t", t.Id));
                    }
                    else
                    {
                        Esegui(c, tx, "UPDATE TavoliAperti SET Preconto=$p, PortataCorrente=$pc WHERE Id=$t",
                               ("$p", t.Preconto ? 1 : 0), ("$pc", t.PortataCorrente), ("$t", t.Id));
                        int ordine = 0;
                        foreach (var r in righe)
                        {
                            using var cmd = c.CreateCommand();
                            cmd.Transaction = tx;
                            cmd.CommandText = @"INSERT INTO RigheTavolo (TavoloId, Ordine, Tipo, ProdottoId, Descrizione, Barcode, Quantita, PrezzoCentesimi,
                                                    PrezzoOriginaleCentesimi, RepartoFiscale, CategoriaId, CategoriaNome, AliquotaIVA, UnitaPeso,
                                                    ScontoPercentualeCent, CentroProduzioneId, Portata, Comandata)
                                                VALUES ($t, $o, $tipo, $p, $d, $b, $q, $pr, $orig, $rep, $cat, $catN, $iva, $up, $sc, $centro, $port, $com)";
                            cmd.Parameters.AddWithValue("$t", t.Id);
                            cmd.Parameters.AddWithValue("$o", ordine++);
                            cmd.Parameters.AddWithValue("$tipo", (int)r.Tipo);
                            cmd.Parameters.AddWithValue("$p", (object)r.ProdottoId ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("$d", r.Descrizione ?? "");
                            cmd.Parameters.AddWithValue("$b", r.Barcode ?? "");
                            cmd.Parameters.AddWithValue("$q", (double)r.Quantita);
                            cmd.Parameters.AddWithValue("$pr", r.PrezzoCentesimi);
                            cmd.Parameters.AddWithValue("$orig", r.PrezzoOriginaleCentesimi);
                            cmd.Parameters.AddWithValue("$rep", r.RepartoFiscale);
                            cmd.Parameters.AddWithValue("$cat", r.CategoriaId);
                            cmd.Parameters.AddWithValue("$catN", r.CategoriaNome ?? "");
                            cmd.Parameters.AddWithValue("$iva", r.AliquotaIVA);
                            cmd.Parameters.AddWithValue("$up", (object)r.UnitaPeso ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("$sc", (object)r.ScontoPercentualeCent ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("$centro", r.CentroProduzioneId);
                            cmd.Parameters.AddWithValue("$port", r.Portata);
                            cmd.Parameters.AddWithValue("$com", r.Comandata ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    if (stampe != null) foreach (var j in stampe) Accoda(c, tx, j);
                    if (storni != null) foreach (var s in storni) RegistraStorno(c, tx, s);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    LogManager.Error($"Salvataggio tavolo {t.SalaId}/{t.Numero} fallito: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>Progressivo comande, ricomincia da 1 ogni giorno (come il PC).</summary>
        public int ProssimoProgressivoComanda()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"UPDATE ProgressivoComanda SET Numero = CASE WHEN Data = $oggi THEN Numero + 1 ELSE 1 END, Data = $oggi WHERE Id = 1;
                                    SELECT Numero FROM ProgressivoComanda WHERE Id = 1;";
                cmd.Parameters.AddWithValue("$oggi", DateTime.Now.ToString("yyyy-MM-dd"));
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // ══════════════ CODA DI STAMPA ══════════════

        /// <summary>Stampe in attesa o in errore, dalla più vecchia.</summary>
        public List<JobStampa> CodaDaStampare(bool ancheInErrore)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $@"SELECT Id, DataCreazione, Tipo, Descrizione, Stampante, IP, Porta, Dati, Stato, Tentativi, UltimoErrore
                                     FROM CodaStampa WHERE Stato = 0 {(ancheInErrore ? "OR Stato = 1" : "")} ORDER BY Id";
                var lista = new List<JobStampa>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    lista.Add(new JobStampa
                    {
                        Id = r.GetInt64(0),
                        Creato = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now,
                        Tipo = (TipoJob)r.GetInt32(2), Descrizione = r.GetString(3), Stampante = r.GetString(4),
                        IP = r.GetString(5), Porta = r.GetInt32(6), Dati = (byte[])r.GetValue(7),
                        Stato = (StatoJob)r.GetInt32(8), Tentativi = r.GetInt32(9),
                        UltimoErrore = r.IsDBNull(10) ? null : r.GetString(10)
                    });
                return lista;
            }
        }

        public void EsitoJob(long id, bool ok, string errore, bool inErrore)
        {
            lock (_lock)
            {
                using var c = Apri();
                if (ok) Esegui(c, null, "DELETE FROM CodaStampa WHERE Id=$id", ("$id", id));
                else Esegui(c, null, "UPDATE CodaStampa SET Tentativi = Tentativi + 1, UltimoErrore=$e, UltimoTentativo=$d, Stato=$s WHERE Id=$id",
                            ("$e", errore ?? ""), ("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), ("$s", inErrore ? 1 : 0), ("$id", id));
            }
        }

        /// <summary>Rimette in attesa le stampe in errore (tasto RIPROVA).</summary>
        public void RiprovaTutto()
        {
            lock (_lock) { using var c = Apri(); Esegui(c, null, "UPDATE CodaStampa SET Stato = 0 WHERE Stato = 1"); }
        }

        public void EliminaJob(long id)
        {
            lock (_lock) { using var c = Apri(); Esegui(c, null, "DELETE FROM CodaStampa WHERE Id=$id", ("$id", id)); }
        }

        public int ContaInErrore()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM CodaStampa WHERE Stato = 1";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // ══════════════ interni ══════════════

        private static void Accoda(SqliteConnection c, SqliteTransaction tx, JobStampa j)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO CodaStampa (DataCreazione, Tipo, Descrizione, Stampante, IP, Porta, Dati)
                                VALUES ($d, $t, $desc, $st, $ip, $porta, $dati)";
            cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$t", (int)j.Tipo);
            cmd.Parameters.AddWithValue("$desc", j.Descrizione ?? "");
            cmd.Parameters.AddWithValue("$st", j.Stampante ?? "");
            cmd.Parameters.AddWithValue("$ip", j.IP ?? "");
            cmd.Parameters.AddWithValue("$porta", j.Porta);
            cmd.Parameters.AddWithValue("$dati", j.Dati ?? Array.Empty<byte>());
            cmd.ExecuteNonQuery();
        }

        private static void RegistraStorno(SqliteConnection c, SqliteTransaction tx, StornoRegistrato s)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Storni (DataOra, Sala, Tavolo, Centro, Descrizione, Quantita, PrezzoCentesimi, Stampato)
                                VALUES ($d, $s, $t, $c, $desc, $q, $p, $st)";
            cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$s", s.Sala ?? "");
            cmd.Parameters.AddWithValue("$t", s.Tavolo ?? "");
            cmd.Parameters.AddWithValue("$c", s.Centro ?? "");
            cmd.Parameters.AddWithValue("$desc", s.Descrizione ?? "");
            cmd.Parameters.AddWithValue("$q", (double)s.Quantita);
            cmd.Parameters.AddWithValue("$p", s.PrezzoCentesimi);
            cmd.Parameters.AddWithValue("$st", s.Stampato ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        private static void Esegui(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parametri)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (n, v) in parametri) cmd.Parameters.AddWithValue(n, v);
            cmd.ExecuteNonQuery();
        }
    }
}
