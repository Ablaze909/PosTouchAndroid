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
    /// <summary>ArchivioOperatori — turni, fondo cassa e movimenti (stesso database degli operatori).</summary>
    public partial class ArchivioOperatori
    {
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
        /// <summary>L'ultimo turno chiuso, null se nessuno.</summary>
        public Turno UltimoTurnoChiuso()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Id, OperatoreId, OperatoreNome, Apertura, Chiusura, Stato, ContantiContatiCent, FondoInizialeCent, FondoLasciatoCent FROM Turni WHERE Stato = 2 ORDER BY Id DESC LIMIT 1";
                using var r = cmd.ExecuteReader();
                return r.Read() ? LeggiTurno(r) : null;
            }
        }

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
