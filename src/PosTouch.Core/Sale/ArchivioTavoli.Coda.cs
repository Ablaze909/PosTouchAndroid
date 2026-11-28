using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    /// <summary>tavoli.db — coda di stampa delle comande e deviazioni delle stampanti.</summary>
    public partial class ArchivioTavoli
    {
        /// <summary>Accoda stampe che non appartengono a un tavolo (comanda da banco), tutte insieme.</summary>
        public void AccodaStampe(IEnumerable<JobStampa> stampe)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                try
                {
                    foreach (var j in stampe) Accoda(c, tx, j);
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }

        // ══════════════ DEVIAZIONI STAMPANTI ══════════════

        public List<DeviazioneStampa> Deviazioni()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT NomeOrigine, IPOrigine, PortaOrigine, NomeDestinazione, IPDestinazione, PortaDestinazione, Attivata FROM DeviazioniStampa";
                var l = new List<DeviazioneStampa>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    l.Add(new DeviazioneStampa
                    {
                        NomeOrigine = r.GetString(0), IPOrigine = r.GetString(1), PortaOrigine = r.GetInt32(2),
                        NomeDestinazione = r.GetString(3), IPDestinazione = r.GetString(4), PortaDestinazione = r.GetInt32(5),
                        Attivata = DateTime.TryParse(r.GetString(6), out var d) ? d : DateTime.Now
                    });
                return l;
            }
        }

        /// <summary>🔀 FORZA DEVIAZIONE: vale per le stampe nuove e sposta subito quelle in coda (resta attiva anche dopo il riavvio).</summary>
        public void AttivaDeviazione(DeviazioneStampa d)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                Esegui(c, tx, @"INSERT OR REPLACE INTO DeviazioniStampa VALUES ($ip, $p, $n, $ipd, $pd, $nd, $data)",
                       ("$ip", d.IPOrigine), ("$p", d.PortaOrigine), ("$n", d.NomeOrigine),
                       ("$ipd", d.IPDestinazione), ("$pd", d.PortaDestinazione), ("$nd", d.NomeDestinazione), ("$data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                // tentativi da capo: sulla stampante nuova ha diritto ai suoi 4 tentativi (prima: errore al primo intoppo)
                Esegui(c, tx, "UPDATE CodaStampa SET IP=$ipd, Porta=$pd, Stampante=$nd, Stato=0, Tentativi=0 WHERE IP=$ip AND Porta=$p AND Stato IN (0, 1)",
                       ("$ipd", d.IPDestinazione), ("$pd", d.PortaDestinazione), ("$nd", $"{d.NomeDestinazione} (deviata da {d.NomeOrigine})"),
                       ("$ip", d.IPOrigine), ("$p", d.PortaOrigine));
                tx.Commit();
                LogManager.Warning($"DEVIAZIONE attivata: {d.NomeOrigine} ({d.IPOrigine}) → {d.NomeDestinazione} ({d.IPDestinazione})");
            }
        }

        public void RimuoviDeviazioni()
        {
            lock (_lock) { using var c = Apri(); Esegui(c, null, "DELETE FROM DeviazioniStampa"); }
            LogManager.Info("DEVIAZIONI rimosse");
        }

        /// <summary>Devia una singola stampa in coda (anche in errore) su un'altra stampante.</summary>
        public void DeviaJob(long id, string nome, string ip, int porta)
        {
            lock (_lock)
            {
                using var c = Apri();
                Esegui(c, null, "UPDATE CodaStampa SET IP=$ip, Porta=$p, Stampante=$n, Stato=0, Tentativi=0 WHERE Id=$id",
                       ("$ip", ip), ("$p", porta), ("$n", nome), ("$id", id));
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
                cmd.CommandText = $@"SELECT Id, DataCreazione, Tipo, Descrizione, Stampante, IP, Porta, Dati, Stato, Tentativi, UltimoErrore, ControlloStato, SalaId, NumeroTavolo
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
                        UltimoErrore = r.IsDBNull(10) ? null : r.GetString(10),
                        ControlloStato = !r.IsDBNull(11) && r.GetInt32(11) != 0,
                        SalaId = r.IsDBNull(12) ? 0 : r.GetInt32(12),
                        NumeroTavolo = r.IsDBNull(13) ? 0 : r.GetInt32(13)
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
            // tentativi da capo: dopo RIPROVA ogni stampa ha di nuovo i suoi 4 tentativi prima dell'avviso
            lock (_lock) { using var c = Apri(); Esegui(c, null, "UPDATE CodaStampa SET Stato = 0, Tentativi = 0 WHERE Stato = 1"); }
        }

        /// <summary>
        /// Rimette in attesa una stampa (RIPROVA del singolo lavoro). Se prima di lei, sulla stessa stampante, c'è una
        /// stampa in errore, riparte anche quella: l'ordine resta e la RIPROVA non resta senza effetto.
        /// </summary>
        public void RiprovaJob(long id)
        {
            lock (_lock)
            {
                using var c = Apri();
                Esegui(c, null, @"UPDATE CodaStampa SET Stato = 0, Tentativi = 0
                                  WHERE Id = $id
                                     OR (Stato = 1 AND Id < $id AND EXISTS (SELECT 1 FROM CodaStampa j WHERE j.Id = $id AND j.IP = CodaStampa.IP AND j.Porta = CodaStampa.Porta))",
                       ("$id", id));
            }
        }

        public void EliminaJob(long id)
        {
            lock (_lock) { using var c = Apri(); Esegui(c, null, "DELETE FROM CodaStampa WHERE Id=$id", ("$id", id)); }
        }

        /// <summary>
        /// Stampa ferma: in errore, oppure in attesa dietro una stampa in errore della stessa stampante (non partirà
        /// finché quella non esce). Prima contavano solo quelle in errore: con 3 comande per la cucina spenta la coda
        /// diceva 1 e la mappa segnava un solo tavolo.
        /// </summary>
        private const string CondizioneFerma = @"(Stato = 1 OR (Stato = 0 AND EXISTS (SELECT 1 FROM CodaStampa e
                                                  WHERE e.Stato = 1 AND e.IP = CodaStampa.IP AND e.Porta = CodaStampa.Porta AND e.Id < CodaStampa.Id)))";

        /// <summary>Tavoli con almeno una stampa ferma (in errore o in attesa dietro una in errore): chiave "sala:numero".</summary>
        public HashSet<string> TavoliConErroreStampa()
        {
            lock (_lock)
            {
                var r = new HashSet<string>();
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT DISTINCT SalaId, NumeroTavolo FROM CodaStampa WHERE NumeroTavolo <> 0 AND {CondizioneFerma}";
                using var l = cmd.ExecuteReader();
                while (l.Read()) r.Add($"{l.GetInt32(0)}:{l.GetInt32(1)}");
                return r;
            }
        }

        public int ContaInErrore()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM CodaStampa WHERE {CondizioneFerma}";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static void Accoda(SqliteConnection c, SqliteTransaction tx, JobStampa j)
        {
            // deviazione forzata attiva per questa stampante?
            using (var dev = c.CreateCommand())
            {
                dev.Transaction = tx;
                dev.CommandText = "SELECT IPDestinazione, PortaDestinazione, NomeDestinazione FROM DeviazioniStampa WHERE IPOrigine=$ip AND PortaOrigine=$p";
                dev.Parameters.AddWithValue("$ip", j.IP ?? "");
                dev.Parameters.AddWithValue("$p", j.Porta);
                using var r = dev.ExecuteReader();
                if (r.Read())
                {
                    j.Stampante = $"{r.GetString(2)} (deviata da {j.Stampante})";
                    j.IP = r.GetString(0);
                    j.Porta = r.GetInt32(1);
                }
            }
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO CodaStampa (DataCreazione, Tipo, Descrizione, Stampante, IP, Porta, Dati, SalaId, NumeroTavolo, ControlloStato)
                                VALUES ($d, $t, $desc, $st, $ip, $porta, $dati, $sala, $num, $stato)";
            cmd.Parameters.AddWithValue("$stato", j.ControlloStato ? 1 : 0);
            cmd.Parameters.AddWithValue("$sala", j.SalaId);
            cmd.Parameters.AddWithValue("$num", j.NumeroTavolo);
            cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$t", (int)j.Tipo);
            cmd.Parameters.AddWithValue("$desc", j.Descrizione ?? "");
            cmd.Parameters.AddWithValue("$st", j.Stampante ?? "");
            cmd.Parameters.AddWithValue("$ip", j.IP ?? "");
            cmd.Parameters.AddWithValue("$porta", j.Porta);
            cmd.Parameters.AddWithValue("$dati", j.Dati ?? Array.Empty<byte>());
            cmd.ExecuteNonQuery();
        }
    }
}
