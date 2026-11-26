using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    /// <summary>tavoli.db — numeratori: progressivo comande ed eliminacode del banco.</summary>
    public partial class ArchivioTavoli
    {
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

        /// <summary>Prossimo numero eliminacode della comanda da banco (1, 2, 3…; ricomincia ogni giorno o dopo la chiusura fiscale).</summary>
        public int ProssimoEliminacode()
        {
            lock (_lock)
            {
                using var c = Apri();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"UPDATE EliminacodeProgressivo SET Numero = CASE WHEN Data = $oggi THEN Numero + 1 ELSE 1 END, Data = $oggi WHERE Id = 1;
                                    SELECT Numero FROM EliminacodeProgressivo WHERE Id = 1;";
                cmd.Parameters.AddWithValue("$oggi", DateTime.Now.ToString("yyyy-MM-dd"));
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>Chiusura fiscale: l'eliminacode riparte da 1 (ResetEliminacode del PC).</summary>
        public void AzzeraEliminacode()
        {
            lock (_lock) { using var c = Apri(); Esegui(c, null, "UPDATE EliminacodeProgressivo SET Numero = 0 WHERE Id = 1"); }
        }
    }
}
