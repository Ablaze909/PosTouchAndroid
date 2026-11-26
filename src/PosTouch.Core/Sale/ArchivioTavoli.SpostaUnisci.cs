using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    public partial class ArchivioTavoli
    {
        // ══════════════ SPOSTA / UNISCI (come il PC) ══════════════

        /// <summary>
        /// 🔀 SPOSTA: il tavolo va sul tavolo di destinazione; se è occupato i due tavoli si SCAMBIANO.
        /// Restituisce true se è stato uno scambio.
        /// </summary>
        public bool SpostaTavolo(long tavoloId, int salaDest, int numeroDest)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                try
                {
                    long? altro = null;
                    int salaOrig = 0, numOrig = 0;
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT SalaId, Numero FROM TavoliAperti WHERE Id=$id";
                        cmd.Parameters.AddWithValue("$id", tavoloId);
                        using var r = cmd.ExecuteReader();
                        if (!r.Read()) throw new InvalidOperationException("Tavolo non trovato.");
                        salaOrig = r.GetInt32(0); numOrig = r.GetInt32(1);
                    }
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT Id FROM TavoliAperti WHERE SalaId=$s AND Numero=$n";
                        cmd.Parameters.AddWithValue("$s", salaDest);
                        cmd.Parameters.AddWithValue("$n", numeroDest);
                        if (cmd.ExecuteScalar() is long id) altro = id;
                    }
                    if (altro.HasValue)
                    {
                        // scambio: il tavolo di destinazione passa per un numero provvisorio per rispettare UNIQUE(SalaId, Numero)
                        Esegui(c, tx, "UPDATE TavoliAperti SET SalaId=-999, Numero=-999 WHERE Id=$id", ("$id", altro.Value));
                        Esegui(c, tx, "UPDATE TavoliAperti SET SalaId=$s, Numero=$n WHERE Id=$id", ("$s", salaDest), ("$n", numeroDest), ("$id", tavoloId));
                        Esegui(c, tx, "UPDATE TavoliAperti SET SalaId=$s, Numero=$n WHERE Id=$id", ("$s", salaOrig), ("$n", numOrig), ("$id", altro.Value));
                    }
                    else
                        Esegui(c, tx, "UPDATE TavoliAperti SET SalaId=$s, Numero=$n WHERE Id=$id", ("$s", salaDest), ("$n", numeroDest), ("$id", tavoloId));
                    SpostaCoda(c, tx, salaOrig, numOrig, salaDest, numeroDest, scambio: altro.HasValue);
                    tx.Commit();
                    LogManager.Info($"SPOSTA tavolo {salaOrig}/{numOrig} → {salaDest}/{numeroDest}{(altro.HasValue ? " (scambio)" : "")}");
                    return altro.HasValue;
                }
                catch { tx.Rollback(); throw; }
            }
        }

        /// <summary>Le stampe in coda/errore seguono il tavolo spostato (il bordo rosso in mappa resta sul tavolo giusto).</summary>
        private static void SpostaCoda(SqliteConnection c, SqliteTransaction tx, int salaOrig, int numOrig, int salaDest, int numDest, bool scambio)
        {
            const string upd = "UPDATE CodaStampa SET SalaId=$s2, NumeroTavolo=$n2 WHERE SalaId=$s1 AND NumeroTavolo=$n1";
            if (scambio) Esegui(c, tx, upd, ("$s1", salaDest), ("$n1", numDest), ("$s2", -999), ("$n2", -999));
            Esegui(c, tx, upd, ("$s1", salaOrig), ("$n1", numOrig), ("$s2", salaDest), ("$n2", numDest));
            if (scambio) Esegui(c, tx, upd, ("$s1", -999), ("$n1", -999), ("$s2", salaOrig), ("$n2", numOrig));
        }

        /// <summary>🔗 UNISCI: le righe dei tavoli sorgente vanno in fondo al tavolo di destinazione; i sorgenti si liberano.</summary>
        public void UnisciTavoli(long destinazioneId, IEnumerable<long> sorgenti)
        {
            lock (_lock)
            {
                using var c = Apri();
                using var tx = c.BeginTransaction();
                try
                {
                    foreach (long id in sorgenti)
                    {
                        if (id == destinazioneId) continue;
                        long max;
                        using (var cmd = c.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "SELECT IFNULL(MAX(Ordine), -1) + 1 FROM RigheTavolo WHERE TavoloId=$d";
                            cmd.Parameters.AddWithValue("$d", destinazioneId);
                            max = Convert.ToInt64(cmd.ExecuteScalar());   // vero MAX ogni volta (FIX BUG-I2 del PC)
                        }
                        Esegui(c, tx, "UPDATE RigheTavolo SET TavoloId=$d, Ordine = Ordine + $o WHERE TavoloId=$s", ("$d", destinazioneId), ("$o", max), ("$s", id));
                        // stampe in coda/errore del tavolo unito: passano al tavolo di destinazione (come SPOSTA)
                        Esegui(c, tx, @"UPDATE CodaStampa SET SalaId = (SELECT SalaId FROM TavoliAperti WHERE Id=$d), NumeroTavolo = (SELECT Numero FROM TavoliAperti WHERE Id=$d)
                                        WHERE SalaId = (SELECT SalaId FROM TavoliAperti WHERE Id=$s) AND NumeroTavolo = (SELECT Numero FROM TavoliAperti WHERE Id=$s)",
                               ("$d", destinazioneId), ("$s", id));
                        Esegui(c, tx, "DELETE FROM TavoliAperti WHERE Id=$s", ("$s", id));
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
        }
    }
}
