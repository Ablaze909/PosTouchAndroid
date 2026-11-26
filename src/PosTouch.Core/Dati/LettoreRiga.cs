using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// Lettura "tollerante" delle colonne: se una colonna manca (pos.db di versioni vecchie)
    /// o è NULL, restituisce il valore predefinito invece di andare in errore.
    /// </summary>
    internal sealed class LettoreRiga
    {
        private readonly SqliteDataReader _r;
        private readonly Dictionary<string, int> _colonne = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public LettoreRiga(SqliteDataReader r)
        {
            _r = r;
            for (int i = 0; i < r.FieldCount; i++)
                _colonne[r.GetName(i)] = i;
        }

        public bool Prossima() => _r.Read();

        public int Int(string col, int def = 0)
        {
            if (!_colonne.TryGetValue(col, out int i) || _r.IsDBNull(i)) return def;
            return Convert.ToInt32(_r.GetValue(i));
        }

        public string Testo(string col, string def = "")
        {
            if (!_colonne.TryGetValue(col, out int i) || _r.IsDBNull(i)) return def;
            return Convert.ToString(_r.GetValue(i)) ?? def;
        }

        public bool Bool(string col, bool def = false)
        {
            if (!_colonne.TryGetValue(col, out int i) || _r.IsDBNull(i)) return def;
            return Convert.ToInt32(_r.GetValue(i)) != 0;
        }
    }
}
