using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// operatori.db: operatori (nome, PIN, permessi) e turni.
    /// Il PIN non è salvato in chiaro: hash PBKDF2-SHA256 con sale per operatore.
    /// Il PIN identifica l'operatore, quindi due operatori attivi non possono avere lo stesso PIN.
    /// </summary>
    public class ArchivioOperatori
    {
        private const int Iterazioni = 10000;
        private readonly string _connString;
        private readonly object _lock = new object();

        public ArchivioOperatori(string cartellaDati)
        {
            _connString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(cartellaDati, "operatori.db"),
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
            CreaSchema();
        }

        private SqliteConnection Apri()
        {
            var c = new SqliteConnection(_connString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=30000;";
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
                    CREATE TABLE IF NOT EXISTS Operatori (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Nome TEXT NOT NULL,
                        PinHash TEXT NOT NULL,
                        PinSale TEXT NOT NULL,
                        Amministratore INTEGER NOT NULL DEFAULT 0,
                        Attivo INTEGER NOT NULL DEFAULT 1,
                        Permessi TEXT NOT NULL DEFAULT ''
                    );
                    CREATE TABLE IF NOT EXISTS Turni (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        OperatoreId INTEGER NOT NULL,
                        OperatoreNome TEXT NOT NULL,
                        Apertura TEXT NOT NULL,
                        Chiusura TEXT,
                        Stato INTEGER NOT NULL DEFAULT 0,
                        ContantiContatiCent INTEGER
                    );
                    CREATE INDEX IF NOT EXISTS IX_Turni_Stato ON Turni(Stato);
                    CREATE TABLE IF NOT EXISTS MovimentiCassa (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TurnoId INTEGER NOT NULL,
                        DataOra TEXT NOT NULL,
                        Tipo INTEGER NOT NULL,
                        ImportoCent INTEGER NOT NULL,
                        Causale TEXT NOT NULL DEFAULT '',
                        OperatoreNome TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS IX_MovimentiCassa_Turno ON MovimentiCassa(TurnoId);
                    -- gli Id partono da 2: 1 è il valore delle vendite senza operatore (come il PC), mai di una persona
                    INSERT INTO sqlite_sequence (name, seq) SELECT 'Operatori', 1
                        WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = 'Operatori');";
                cmd.ExecuteNonQuery();
                // colonne aggiunte dopo (database creati con la versione precedente)
                AggiungiColonna(c, "Turni", "FondoInizialeCent", "INTEGER");
                AggiungiColonna(c, "Turni", "FondoLasciatoCent", "INTEGER");
            }
        }

        private static void AggiungiColonna(SqliteConnection c, string tabella, string colonna, string tipo)
        {
            using var chk = c.CreateCommand();
            chk.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabella}') WHERE name = '{colonna}'";
            if (Convert.ToInt32(chk.ExecuteScalar()) > 0) return;
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {tabella} ADD COLUMN {colonna} {tipo}";
            cmd.ExecuteNonQuery();
        }

        // ══════════════ OPERATORI ══════════════

        public List<Operatore> Operatori()
        {
            lock (_lock)
            {
                using var c = Apri();
                return Leggi(c).Select(x => x.op).ToList();
            }
        }

        public bool CeAmministratore() => Operatori().Any(o => o.Amministratore && o.Attivo);

        /// <summary>Operatore attivo con questo PIN, null se nessuno.</summary>
        public Operatore TrovaPerPin(string pin)
        {
            if (string.IsNullOrEmpty(pin)) return null;
            lock (_lock)
            {
                using var c = Apri();
                var trovati = Leggi(c).Where(x => x.op.Attivo && PinUguale(pin, x.hash, x.sale)).Select(x => x.op).ToList();
                if (trovati.Count > 1) LogManager.Error($"PIN condiviso da {trovati.Count} operatori: accesso negato");
                return trovati.Count == 1 ? trovati[0] : null;   // mai indovinare tra due persone
            }
        }

        /// <summary>true se il PIN è di un operatore, anche disattivato (per non farlo coincidere con la password assistenza).</summary>
        public bool PinUsato(string pin)
        {
            if (string.IsNullOrEmpty(pin)) return false;
            lock (_lock)
            {
                using var c = Apri();
                return Leggi(c).Any(x => PinUguale(pin, x.hash, x.sale));
            }
        }

        /// <summary>
        /// Crea o aggiorna un operatore. <paramref name="pinNuovo"/> null = PIN invariato (solo per un operatore esistente).
        /// Errori (ArgumentException): nome vuoto, PIN non valido o già usato, nessun amministratore attivo rimasto.
        /// </summary>
        public void Salva(Operatore o, string pinNuovo)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            string nome = (o.Nome ?? "").Trim().ToUpperInvariant();
            if (nome.Length == 0) throw new ArgumentException("Il nome dell'operatore è obbligatorio.");
            if (o.Id == 0 && pinNuovo == null) throw new ArgumentException("Il PIN è obbligatorio.");
            if (pinNuovo != null) ControllaFormatoPin(pinNuovo);

            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                var tutti = Leggi(c, tx);
                if (tutti.Any(x => x.op.Id != o.Id && x.op.Nome == nome))
                    throw new ArgumentException($"Esiste già un operatore {nome}.");
                if (pinNuovo != null && tutti.Any(x => x.op.Id != o.Id && x.op.Attivo && PinUguale(pinNuovo, x.hash, x.sale)))
                    throw new ArgumentException("Questo PIN è già usato da un altro operatore: il PIN deve identificare una sola persona.");
                // deve restare almeno un amministratore attivo, altrimenti nessuno può più gestire gli operatori
                bool restaAdmin = tutti.Any(x => x.op.Id != o.Id && x.op.Amministratore && x.op.Attivo) || (o.Amministratore && o.Attivo);
                if (!restaAdmin) throw new ArgumentException("Deve restare almeno un amministratore attivo.");
                // riattivato senza PIN nuovo: il suo vecchio PIN potrebbe essere stato dato nel frattempo a un altro
                var prima = tutti.FirstOrDefault(x => x.op.Id == o.Id).op;
                if (prima != null && !prima.Attivo && o.Attivo && pinNuovo == null)
                    throw new ArgumentException("Per riattivare l'operatore assegna un nuovo PIN.");

                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                string permessi = string.Join(",", o.Permessi.Select(p => p.ToString()));
                if (o.Id == 0)
                {
                    var (hash, sale) = CalcolaHash(pinNuovo);
                    cmd.CommandText = @"INSERT INTO Operatori (Nome, PinHash, PinSale, Amministratore, Attivo, Permessi)
                                        VALUES ($n, $h, $s, $a, $att, $p); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$s", sale);
                }
                else if (pinNuovo != null)
                {
                    var (hash, sale) = CalcolaHash(pinNuovo);
                    cmd.CommandText = "UPDATE Operatori SET Nome=$n, PinHash=$h, PinSale=$s, Amministratore=$a, Attivo=$att, Permessi=$p WHERE Id=$id; SELECT $id;";
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$s", sale);
                }
                else
                    cmd.CommandText = "UPDATE Operatori SET Nome=$n, Amministratore=$a, Attivo=$att, Permessi=$p WHERE Id=$id; SELECT $id;";
                cmd.Parameters.AddWithValue("$n", nome);
                cmd.Parameters.AddWithValue("$a", o.Amministratore ? 1 : 0);
                cmd.Parameters.AddWithValue("$att", o.Attivo ? 1 : 0);
                cmd.Parameters.AddWithValue("$p", permessi);
                cmd.Parameters.AddWithValue("$id", o.Id);
                int id = Convert.ToInt32(cmd.ExecuteScalar());
                tx.Commit();
                o.Id = id;   // solo a salvataggio riuscito
                o.Nome = nome;
            }
            LogManager.Info($"Operatore salvato: {nome}{(o.Amministratore ? " (amministratore)" : "")}{(pinNuovo != null ? ", PIN cambiato" : "")}");
        }

        /// <summary>Elimina un operatore (non se è l'ultimo amministratore o ha un turno non chiuso).</summary>
        public void Elimina(int id)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                var tutti = Leggi(c, tx);
                var o = tutti.FirstOrDefault(x => x.op.Id == id).op ?? throw new ArgumentException("Operatore non trovato.");
                if (o.Amministratore && !tutti.Any(x => x.op.Id != id && x.op.Amministratore && x.op.Attivo))
                    throw new ArgumentException("È l'ultimo amministratore: non si può eliminare.");
                using (var chk = c.CreateCommand())
                {
                    chk.Transaction = tx;
                    chk.CommandText = "SELECT COUNT(*) FROM Turni WHERE OperatoreId=$id AND Stato <> 2";
                    chk.Parameters.AddWithValue("$id", id);
                    if (Convert.ToInt32(chk.ExecuteScalar()) > 0) throw new ArgumentException($"{o.Nome} ha un turno aperto: chiudilo prima.");
                }
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Operatori WHERE Id=$id";   // i turni chiusi restano con il nome (storico)
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
                tx.Commit();
                LogManager.Info($"Operatore eliminato: {o.Nome}");
            }
        }

        public static void ControllaFormatoPin(string pin)
        {
            if (string.IsNullOrEmpty(pin) || pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
                throw new ArgumentException("Il PIN deve avere da 4 a 8 cifre.");
        }

        private List<(Operatore op, string hash, string sale)> Leggi(SqliteConnection c, SqliteTransaction tx = null)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, Nome, PinHash, PinSale, Amministratore, Attivo, Permessi FROM Operatori ORDER BY Amministratore DESC, Nome";
            var l = new List<(Operatore, string, string)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var permessi = new HashSet<Permesso>();
                foreach (var p in r.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (Enum.TryParse(p.Trim(), out Permesso pp)) permessi.Add(pp);   // permessi sconosciuti (versioni future) ignorati
                l.Add((new Operatore { Id = r.GetInt32(0), Nome = r.GetString(1), Amministratore = r.GetInt32(4) == 1, Attivo = r.GetInt32(5) == 1, Permessi = permessi },
                       r.GetString(2), r.GetString(3)));
            }
            return l;
        }

        private static (string hash, string sale) CalcolaHash(string pin)
        {
            byte[] sale = RandomNumberGenerator.GetBytes(16);
            byte[] h = Rfc2898DeriveBytes.Pbkdf2(pin, sale, Iterazioni, HashAlgorithmName.SHA256, 32);
            return (Convert.ToBase64String(h), Convert.ToBase64String(sale));
        }

        private static bool PinUguale(string pin, string hash, string sale)
        {
            try
            {
                byte[] h = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(sale), Iterazioni, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(hash));
            }
            catch { return false; }
        }

        // ══════════════ TURNI ══════════════

        /// <summary>Il turno non chiuso (aperto o sospeso), null se nessuno. Ce n'è al massimo uno.</summary>
        public Turno TurnoAttivo()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Id, OperatoreId, OperatoreNome, Apertura, Chiusura, Stato, ContantiContatiCent, FondoInizialeCent, FondoLasciatoCent FROM Turni WHERE Stato <> 2 ORDER BY Id LIMIT 1";
                using var r = cmd.ExecuteReader();
                return r.Read() ? LeggiTurno(r) : null;
            }
        }

        /// <summary>Apre un turno; rifiutato se un altro turno non è chiuso.</summary>
        public Turno ApriTurno(Operatore o)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                using (var chk = c.CreateCommand())
                {
                    chk.Transaction = tx;
                    chk.CommandText = "SELECT OperatoreNome FROM Turni WHERE Stato <> 2 LIMIT 1";
                    if (chk.ExecuteScalar() is string altro) throw new InvalidOperationException($"C'è ancora il turno di {altro} aperto.");
                }
                var t = new Turno { OperatoreId = o.Id, OperatoreNome = o.Nome, Apertura = DateTime.Now, Stato = StatoTurno.Aperto };
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO Turni (OperatoreId, OperatoreNome, Apertura, Stato) VALUES ($o, $n, $a, 0); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$o", t.OperatoreId);
                cmd.Parameters.AddWithValue("$n", t.OperatoreNome);
                cmd.Parameters.AddWithValue("$a", t.Apertura.ToString("yyyy-MM-dd HH:mm:ss"));
                t.Id = Convert.ToInt64(cmd.ExecuteScalar());
                tx.Commit();
                LogManager.Info($"TURNO APERTO: {o.Nome}");
                return t;
            }
        }

        public void ImpostaStato(long turnoId, StatoTurno stato)
        {
            if (stato == StatoTurno.Chiuso) throw new ArgumentException("Per chiudere usare ChiudiTurno.");
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Turni SET Stato=$s WHERE Id=$id AND Stato <> 2";
                cmd.Parameters.AddWithValue("$s", (int)stato);
                cmd.Parameters.AddWithValue("$id", turnoId);
                cmd.ExecuteNonQuery();
            }
        }

        public Turno ChiudiTurno(long turnoId, int? contantiContatiCent, int? fondoLasciatoCent = null)
        {
            if (contantiContatiCent < 0 || fondoLasciatoCent < 0) throw new ArgumentException("Importi negativi non ammessi.");
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"UPDATE Turni SET Stato=2, Chiusura=$c, ContantiContatiCent=$cc, FondoLasciatoCent=$fl WHERE Id=$id AND Stato <> 2;
                                    SELECT Id, OperatoreId, OperatoreNome, Apertura, Chiusura, Stato, ContantiContatiCent, FondoInizialeCent, FondoLasciatoCent FROM Turni WHERE Id=$id";
                cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$cc", (object)contantiContatiCent ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$fl", (object)fondoLasciatoCent ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$id", turnoId);
                using var r = cmd.ExecuteReader();
                var t = r.Read() ? LeggiTurno(r) : null;
                if (t != null) LogManager.Info($"TURNO CHIUSO: {t.OperatoreNome}");
                return t;
            }
        }

        // ══════════════ FONDO CASSA E MOVIMENTI ══════════════

        /// <summary>Fondo cassa del turno appena aperto (solo su un turno non chiuso).</summary>
        public void ImpostaFondoIniziale(long turnoId, int fondoCent)
        {
            if (fondoCent < 0) throw new ArgumentException("Il fondo cassa non può essere negativo.");
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Turni SET FondoInizialeCent=$f WHERE Id=$id AND Stato <> 2";
                cmd.Parameters.AddWithValue("$f", fondoCent);
                cmd.Parameters.AddWithValue("$id", turnoId);
                cmd.ExecuteNonQuery();
            }
            LogManager.Info($"Fondo cassa turno {turnoId}: €{fondoCent / 100m:F2}");
        }

        /// <summary>Fondo da proporre all'apertura: quello lasciato dal turno precedente, altrimenti il suo fondo iniziale, altrimenti 0.</summary>
        public int FondoProposto()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT IFNULL(FondoLasciatoCent, IFNULL(FondoInizialeCent, 0)) FROM Turni WHERE Stato = 2 ORDER BY Id DESC LIMIT 1";
                return cmd.ExecuteScalar() is long v ? (int)v : 0;
            }
        }

        /// <summary>Prelievo o versamento su un turno aperto. Importo maggiore di zero.</summary>
        public MovimentoCassa AggiungiMovimento(long turnoId, TipoMovimento tipo, int importoCent, string causale, string operatore)
        {
            if (importoCent <= 0) throw new ArgumentException("L'importo deve essere maggiore di zero.");
            var m = new MovimentoCassa { TurnoId = turnoId, DataOra = DateTime.Now, Tipo = tipo, ImportoCent = importoCent, Causale = (causale ?? "").Trim(), OperatoreNome = operatore ?? "" };
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                using (var chk = c.CreateCommand())
                {
                    chk.Transaction = tx;
                    chk.CommandText = "SELECT Stato FROM Turni WHERE Id=$id";
                    chk.Parameters.AddWithValue("$id", turnoId);
                    if (!(chk.ExecuteScalar() is long stato) || stato == 2) throw new InvalidOperationException("Il turno non è aperto.");
                }
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO MovimentiCassa (TurnoId, DataOra, Tipo, ImportoCent, Causale, OperatoreNome)
                                    VALUES ($t, $d, $tipo, $i, $c, $o); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$t", turnoId);
                cmd.Parameters.AddWithValue("$d", m.DataOra.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$tipo", (int)tipo);
                cmd.Parameters.AddWithValue("$i", importoCent);
                cmd.Parameters.AddWithValue("$c", m.Causale);
                cmd.Parameters.AddWithValue("$o", m.OperatoreNome);
                m.Id = Convert.ToInt64(cmd.ExecuteScalar());
                tx.Commit();
            }
            LogManager.Info($"{(tipo == TipoMovimento.Prelievo ? "PRELIEVO" : "VERSAMENTO")} €{importoCent / 100m:F2} turno {turnoId} ({m.OperatoreNome}) {m.Causale}");
            return m;
        }

        public List<MovimentoCassa> Movimenti(long turnoId)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Id, TurnoId, DataOra, Tipo, ImportoCent, Causale, OperatoreNome FROM MovimentiCassa WHERE TurnoId=$t ORDER BY Id";
                cmd.Parameters.AddWithValue("$t", turnoId);
                var l = new List<MovimentoCassa>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    l.Add(new MovimentoCassa
                    {
                        Id = r.GetInt64(0), TurnoId = r.GetInt64(1), DataOra = DateTime.TryParse(r.GetString(2), out var d) ? d : DateTime.Now,
                        Tipo = (TipoMovimento)r.GetInt32(3), ImportoCent = r.GetInt32(4), Causale = r.GetString(5), OperatoreNome = r.GetString(6)
                    });
                return l;
            }
        }

        private static Turno LeggiTurno(SqliteDataReader r) => new Turno
        {
            Id = r.GetInt64(0), OperatoreId = r.GetInt32(1), OperatoreNome = r.GetString(2),
            Apertura = DateTime.TryParse(r.GetString(3), out var a) ? a : DateTime.Now,
            Chiusura = r.IsDBNull(4) ? (DateTime?)null : DateTime.TryParse(r.GetString(4), out var ch) ? ch : (DateTime?)null,
            Stato = (StatoTurno)r.GetInt32(5),
            ContantiContatiCent = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
            FondoInizialeCent = r.IsDBNull(7) ? (int?)null : r.GetInt32(7),
            FondoLasciatoCent = r.IsDBNull(8) ? (int?)null : r.GetInt32(8)
        };
    }
}
