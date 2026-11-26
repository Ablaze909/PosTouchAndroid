using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    public partial class ArchivioVendite
    {
        // ══════════════ CONTI NON FISCALI ══════════════

        /// <summary>Nuovo numero di conto, atomico (lo stesso numero va in stampa e in archivio).</summary>
        public int ProssimoNumeroConto()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"UPDATE Progressivi SET ProgressivoConto = ProgressivoConto + 1, UltimoAggiornamento = datetime('now') WHERE Id = 1;
                                    SELECT ProgressivoConto FROM Progressivi WHERE Id = 1;";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>Salva il conto (righe articolo e sconto) in un'unica transazione. Restituisce l'Id.</summary>
        public long SalvaConto(IList<RigaScontrino> righe, int numero, DateTime quando, bool stampato)
        {
            var valide = new List<RigaScontrino>();
            foreach (var r in righe) if (r.ContaNelTotale || r.Tipo == TipoRiga.Gusto) valide.Add(r);
            int totale = 0;
            foreach (var r in valide) if (r.ContaNelTotale) totale += r.ImportoCentesimi;

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
                        cmd.CommandText = @"INSERT INTO Conti (DataOra, TotaleCentesimi, NumeroProgressivo, Stampato, OperatoreId, OperatoreNome)
                                            VALUES ($d, $t, $n, $s, $op, $opNome); SELECT last_insert_rowid();";
                        var (opId, opNome) = OperatoreDi(0, null);
                        cmd.Parameters.AddWithValue("$op", opId);
                        cmd.Parameters.AddWithValue("$opNome", opNome);
                        cmd.Parameters.AddWithValue("$d", quando.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("$t", totale);
                        cmd.Parameters.AddWithValue("$n", numero);
                        cmd.Parameters.AddWithValue("$s", stampato ? 1 : 0);
                        id = Convert.ToInt64(cmd.ExecuteScalar());
                    }
                    foreach (var r in valide)
                    {
                        using var cmd = c.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO ContiRighe (ContoId, ProdottoId, CodiceArticolo, Descrizione, Quantita, PrezzoCentesimi,
                                                PrezzoOriginaleCentesimi, ImportoCentesimi, RepartoFiscale, CategoriaId, CategoriaNome, AliquotaIVA,
                                                Tipo, IsAPeso, UnitaPesoRiga, VariantiTesto)
                                            VALUES ($c, $p, $cod, $desc, $q, $pr, $orig, $imp, $rep, $cat, $catNome, $iva, $tipo, $peso, $unita, $vt)";
                        cmd.Parameters.AddWithValue("$c", id);
                        cmd.Parameters.AddWithValue("$p", (object)r.ProdottoId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$cod", r.Barcode ?? "");
                        cmd.Parameters.AddWithValue("$desc", r.Descrizione ?? "");
                        cmd.Parameters.AddWithValue("$q", (double)r.Quantita);
                        cmd.Parameters.AddWithValue("$pr", r.PrezzoCentesimi);
                        cmd.Parameters.AddWithValue("$orig", r.PrezzoOriginaleCentesimi);
                        cmd.Parameters.AddWithValue("$imp", r.ImportoCentesimi);
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
                    LogManager.Info($"Conto N. {numero} salvato: €{totale / 100m:F2}, {valide.Count} righe");
                    return id;
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    LogManager.Error($"Salvataggio conto N. {numero} fallito: {ex.Message}");
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

        /// <summary>Incassi di un operatore tra apertura e chiusura del turno (scontrini e conti non fiscali).</summary>
        public RiepilogoTurno Riepilogo(Turno t)
        {
            var fine = t.Chiusura ?? DateTime.Now;
            string da = t.Apertura.ToString("yyyy-MM-dd HH:mm:ss"), a = fine.ToString("yyyy-MM-dd HH:mm:ss");
            var ris = new RiepilogoTurno { Turno = t };
            lock (_lock)
            {
                using var c = Apri();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COUNT(*), IFNULL(SUM(TotaleCentesimi),0), IFNULL(SUM(ContantiCent),0), IFNULL(SUM(CartaCent),0)
                                        FROM Vendite WHERE Annullato = 0 AND OperatoreId = $op AND DataOra >= $da AND DataOra <= $a";
                    cmd.Parameters.AddWithValue("$op", t.OperatoreId);
                    cmd.Parameters.AddWithValue("$da", da);
                    cmd.Parameters.AddWithValue("$a", a);
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        ris.Scontrini = r.GetInt32(0);
                        ris.TotaleScontriniCent = Convert.ToInt32(r.GetInt64(1));
                        ris.ContantiCent = Convert.ToInt32(r.GetInt64(2));
                        ris.CartaCent = Convert.ToInt32(r.GetInt64(3));
                    }
                }
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*), IFNULL(SUM(TotaleCentesimi),0) FROM Conti WHERE OperatoreId = $op AND DataOra >= $da AND DataOra <= $a";
                    cmd.Parameters.AddWithValue("$op", t.OperatoreId);
                    cmd.Parameters.AddWithValue("$da", da);
                    cmd.Parameters.AddWithValue("$a", a);
                    using var r = cmd.ExecuteReader();
                    if (r.Read()) { ris.Conti = r.GetInt32(0); ris.TotaleContiCent = Convert.ToInt32(r.GetInt64(1)); }
                }
            }
            return ris;
        }
    }
}
