using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// tavoli.db: tavoli aperti con le loro righe, coda di stampa comande, progressivo comande, registro storni.
    /// Ogni salvataggio di un tavolo è UNA transazione insieme alle stampe da accodare:
    /// le righe diventano "comandate" solo se la comanda è davvero in coda (niente ristampe, niente perdite).
    /// </summary>
    public partial class ArchivioTavoli
    {
        private readonly string _connString;
        private readonly object _lock = new object();

        public ArchivioTavoli(string cartellaDati)
        {
            _connString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(cartellaDati, "tavoli.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true   // tavoli.db non viene mai sostituito: connessioni riusate (meno scritture su disco)
            }.ToString();
            CreaSchema();
        }

        private SqliteConnection Apri()
        {
            var c = new SqliteConnection(_connString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
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
                    -- Eliminacode della comanda da banco (come il PC): si azzera alla chiusura fiscale e a ogni nuovo giorno
                    CREATE TABLE IF NOT EXISTS EliminacodeProgressivo (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        Numero INTEGER NOT NULL DEFAULT 0,
                        Data TEXT NOT NULL DEFAULT ''
                    );
                    INSERT OR IGNORE INTO EliminacodeProgressivo (Id, Numero, Data) VALUES (1, 0, '');
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
                // colonne aggiunte nelle versioni successive (archivi già esistenti)
                AggiungiColonna(c, "TavoliAperti", "NomeCliente", "TEXT DEFAULT ''");
                AggiungiColonna(c, "RigheTavolo", "VariantiTesto", "TEXT");
                AggiungiColonna(c, "RigheTavolo", "InStorno", "INTEGER NOT NULL DEFAULT 0");
                AggiungiColonna(c, "RigheTavolo", "SovrapprezzoCent", "INTEGER NOT NULL DEFAULT 0");
                AggiungiColonna(c, "CodaStampa", "SalaId", "INTEGER NOT NULL DEFAULT 0");
                AggiungiColonna(c, "CodaStampa", "NumeroTavolo", "INTEGER NOT NULL DEFAULT 0");
                AggiungiColonna(c, "CodaStampa", "ControlloStato", "INTEGER NOT NULL DEFAULT 0");
                Esegui(c, null, @"CREATE TABLE IF NOT EXISTS DeviazioniStampa (
                                    IPOrigine TEXT NOT NULL, PortaOrigine INTEGER NOT NULL, NomeOrigine TEXT NOT NULL,
                                    IPDestinazione TEXT NOT NULL, PortaDestinazione INTEGER NOT NULL, NomeDestinazione TEXT NOT NULL,
                                    Attivata TEXT NOT NULL, PRIMARY KEY (IPOrigine, PortaOrigine))");
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
                           IFNULL((SELECT SUM(CAST(ROUND(r.Quantita * r.PrezzoCentesimi) AS INTEGER) + r.SovrapprezzoCent) FROM RigheTavolo r
                                   WHERE r.TavoloId = t.Id AND r.Tipo IN (0, 1) AND r.InStorno = 0), 0),
                           (SELECT COUNT(*) FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.Tipo = 0 AND r.InStorno = 0),
                           EXISTS (SELECT 1 FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.Tipo = 0 AND r.Comandata = 0 AND r.CentroProduzioneId > 0 AND r.InStorno = 0),
                           IFNULL((SELECT MAX(r.Portata) FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.Tipo = 4), 0),
                           IFNULL(t.NomeCliente, ''),
                           EXISTS (SELECT 1 FROM RigheTavolo r WHERE r.TavoloId = t.Id AND r.InStorno = 1)
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
                        PortataMassima = r.GetInt32(9), NomeCliente = r.GetString(10),
                        StorniDaInviare = r.GetInt32(11) == 1
                    });
                return lista;
            }
        }

        /// <summary>Tavolo aperto (lo crea se libero).</summary>
        public TavoloAperto ApriTavolo(int salaId, int numero, string nomeCliente = null)
        {
            lock (_lock)
            {
                using var c = Apri();
                using (var ins = c.CreateCommand())
                {
                    ins.CommandText = "INSERT OR IGNORE INTO TavoliAperti (SalaId, Numero, DataApertura, NomeCliente) VALUES ($s, $n, $d, $nome)";
                    ins.Parameters.AddWithValue("$nome", nomeCliente ?? "");
                    ins.Parameters.AddWithValue("$s", salaId);
                    ins.Parameters.AddWithValue("$n", numero);
                    ins.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    ins.ExecuteNonQuery();
                }
                if (!string.IsNullOrWhiteSpace(nomeCliente))   // conto cliente riaperto vuoto: vale il nome nuovo
                    Esegui(c, null, "UPDATE TavoliAperti SET NomeCliente=$nome WHERE SalaId=$s AND Numero=$n AND NOT EXISTS (SELECT 1 FROM RigheTavolo r WHERE r.TavoloId = TavoliAperti.Id)",
                           ("$nome", nomeCliente), ("$s", salaId), ("$n", numero));
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Id, DataApertura, Preconto, PortataCorrente, IFNULL(NomeCliente,'') FROM TavoliAperti WHERE SalaId=$s AND Numero=$n";
                cmd.Parameters.AddWithValue("$s", salaId);
                cmd.Parameters.AddWithValue("$n", numero);
                using var r = cmd.ExecuteReader();
                r.Read();
                return new TavoloAperto
                {
                    Id = r.GetInt64(0), SalaId = salaId, Numero = numero,
                    Apertura = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now,
                    Preconto = r.GetInt32(2) == 1, PortataCorrente = r.GetInt32(3), NomeCliente = r.GetString(4)
                };
            }
        }

        public List<RigaScontrino> Righe(long tavoloId) => LeggiRighe(tavoloId, inStorno: false);

        /// <summary>Storni non ancora stampati di un tavolo (app chiusa prima di uscire dal tavolo).</summary>
        public List<RigaScontrino> StorniPendenti(long tavoloId) => LeggiRighe(tavoloId, inStorno: true);

        private List<RigaScontrino> LeggiRighe(long tavoloId, bool inStorno)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"SELECT Tipo, ProdottoId, Descrizione, Barcode, Quantita, PrezzoCentesimi, PrezzoOriginaleCentesimi,
                                           RepartoFiscale, CategoriaId, CategoriaNome, AliquotaIVA, UnitaPeso, ScontoPercentualeCent,
                                           CentroProduzioneId, Portata, Comandata, VariantiTesto, SovrapprezzoCent
                                    FROM RigheTavolo WHERE TavoloId=$t AND InStorno=$s ORDER BY Ordine";
                cmd.Parameters.AddWithValue("$t", tavoloId);
                cmd.Parameters.AddWithValue("$s", inStorno ? 1 : 0);
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
                        Comandata = r.GetInt32(15) == 1,
                        VariantiTesto = r.IsDBNull(16) ? null : r.GetString(16),
                        SovrapprezzoCent = r.IsDBNull(17) ? 0 : r.GetInt32(17)
                    });
                return lista;
            }
        }

        /// <summary>
        /// Salva lo stato del tavolo (memoria = fonte autorevole) insieme a stampe e storni, in un'unica transazione.
        /// Tavolo senza righe = tavolo liberato.
        /// </summary>
        /// <param name="liberaSeVuoto">true solo uscendo dal tavolo o dopo il pagamento: un tavolo vuoto viene liberato.
        /// Il salvataggio continuo (bozza) non libera mai il tavolo, anche se per un attimo è vuoto.</param>
        /// <param name="storniPendenti">Righe comandate tolte ma non ancora stornate in stampa: salvate a parte (InStorno=1),
        /// così uno storno non si perde se l'app viene chiusa prima di uscire dal tavolo.</param>
        public void Salva(TavoloAperto t, IList<RigaScontrino> righe, IEnumerable<JobStampa> stampe = null, IEnumerable<StornoRegistrato> storni = null,
                          bool liberaSeVuoto = false, IEnumerable<RigaScontrino> storniPendenti = null)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                try
                {
                    Esegui(c, tx, "DELETE FROM RigheTavolo WHERE TavoloId=$t", ("$t", t.Id));
                    // vuoto = nessun articolo (un tavolo con sole intestazioni portata o righe di sconto si libera: niente tavoli "fantasma")
                    bool vuoto = righe == null || !righe.Any(r => r.Tipo == TipoRiga.Prodotto);
                    if (vuoto && liberaSeVuoto)
                    {
                        Esegui(c, tx, "DELETE FROM TavoliAperti WHERE Id=$t", ("$t", t.Id));
                    }
                    else
                    {
                        // il tavolo esiste sempre finché è aperto (lo ricrea se qualcosa lo avesse tolto)
                        Esegui(c, tx, "INSERT OR IGNORE INTO TavoliAperti (Id, SalaId, Numero, DataApertura, NomeCliente) VALUES ($t, $s, $n, $d, $nome)",
                               ("$t", t.Id), ("$s", t.SalaId), ("$n", t.Numero), ("$d", t.Apertura.ToString("yyyy-MM-dd HH:mm:ss")), ("$nome", t.NomeCliente ?? ""));
                        Esegui(c, tx, "UPDATE TavoliAperti SET Preconto=$p, PortataCorrente=$pc WHERE Id=$t",
                               ("$p", t.Preconto ? 1 : 0), ("$pc", t.PortataCorrente), ("$t", t.Id));
                        int ordine = 0;
                        var tutte = (righe ?? new List<RigaScontrino>()).Select(r => (r, false))
                                    .Concat((storniPendenti ?? Enumerable.Empty<RigaScontrino>()).Select(r => (r, true)));
                        // un solo comando preparato, riusato per ogni riga (prima: query rielaborata a ogni riga, lento sui tablet)
                        using var cmd = c.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO RigheTavolo (TavoloId, Ordine, Tipo, ProdottoId, Descrizione, Barcode, Quantita, PrezzoCentesimi,
                                                PrezzoOriginaleCentesimi, RepartoFiscale, CategoriaId, CategoriaNome, AliquotaIVA, UnitaPeso,
                                                ScontoPercentualeCent, CentroProduzioneId, Portata, Comandata, VariantiTesto, InStorno, SovrapprezzoCent)
                                            VALUES ($t, $o, $tipo, $p, $d, $b, $q, $pr, $orig, $rep, $cat, $catN, $iva, $up, $sc, $centro, $port, $com, $var, $ins, $sovr)";
                        var par = new Dictionary<string, SqliteParameter>();
                        foreach (var nome in new[] { "$t", "$o", "$tipo", "$p", "$d", "$b", "$q", "$pr", "$orig", "$rep", "$cat", "$catN", "$iva", "$up", "$sc", "$centro", "$port", "$com", "$var", "$ins", "$sovr" })
                            par[nome] = cmd.Parameters.Add(new SqliteParameter(nome, DBNull.Value));
                        cmd.Prepare();
                        foreach (var (r, inStorno) in tutte)
                        {
                            par["$sovr"].Value = r.SovrapprezzoCent;
                            par["$t"].Value = t.Id;
                            par["$o"].Value = ordine++;
                            par["$tipo"].Value = (int)r.Tipo;
                            par["$p"].Value = (object)r.ProdottoId ?? DBNull.Value;
                            par["$d"].Value = r.Descrizione ?? "";
                            par["$b"].Value = r.Barcode ?? "";
                            par["$q"].Value = (double)r.Quantita;
                            par["$pr"].Value = r.PrezzoCentesimi;
                            par["$orig"].Value = r.PrezzoOriginaleCentesimi;
                            par["$rep"].Value = r.RepartoFiscale;
                            par["$cat"].Value = r.CategoriaId;
                            par["$catN"].Value = r.CategoriaNome ?? "";
                            par["$iva"].Value = r.AliquotaIVA;
                            par["$up"].Value = (object)r.UnitaPeso ?? DBNull.Value;
                            par["$sc"].Value = (object)r.ScontoPercentualeCent ?? DBNull.Value;
                            par["$centro"].Value = r.CentroProduzioneId;
                            par["$port"].Value = r.Portata;
                            par["$com"].Value = r.Comandata ? 1 : 0;
                            par["$var"].Value = (object)r.VariantiTesto ?? DBNull.Value;
                            par["$ins"].Value = inStorno ? 1 : 0;
                            cmd.ExecuteNonQuery();
                        }
                    }
                    if (stampe != null) foreach (var j in stampe) { j.SalaId = t.SalaId; j.NumeroTavolo = t.Numero; Accoda(c, tx, j); }
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

        // ══════════════ interni ══════════════

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

        private static void AggiungiColonna(SqliteConnection c, string tabella, string colonna, string tipo)
        {
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabella}') WHERE name = $n";
                chk.Parameters.AddWithValue("$n", colonna);
                if (Convert.ToInt64(chk.ExecuteScalar()) > 0) return;
            }
            Esegui(c, null, $"ALTER TABLE {tabella} ADD COLUMN {colonna} {tipo}");
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
