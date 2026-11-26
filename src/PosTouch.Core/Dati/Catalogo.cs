using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Dati
{
    /// <summary>
    /// Catalogo prodotti in sola lettura.
    /// Usa lo stesso schema di pos.db della versione Windows: basta importare quel file.
    /// Le categorie tabacchi (Id 9000-9999) sono escluse in questa fase.
    /// </summary>
    public class Catalogo
    {
        public const string NomeFile = "catalogo.db";
        private const int PrimaCategoriaTabacchi = 9000;

        private readonly string _percorso;
        private List<Categoria> _categorie = new List<Categoria>();
        private List<Prodotto> _prodotti = new List<Prodotto>();

        public Catalogo(string cartellaDati)
        {
            _percorso = Path.Combine(cartellaDati, NomeFile);
        }

        public bool Presente => File.Exists(_percorso);
        internal string Percorso => _percorso;
        public IReadOnlyList<Categoria> Categorie => _categorie;

        public IEnumerable<Prodotto> ProdottiDi(int categoriaId) => _prodotti.Where(p => p.CategoriaId == categoriaId);
        public IEnumerable<Prodotto> Preferiti() => _prodotti.Where(p => p.IsPreferito);

        public Prodotto CercaBarcode(string codice)
        {
            if (string.IsNullOrWhiteSpace(codice)) return null;
            codice = codice.Trim();
            return _prodotti.FirstOrDefault(p => p.Barcode == codice || p.Codice == codice);
        }

        /// <summary>Carica in memoria categorie e prodotti attivi. Restituisce il numero di prodotti.</summary>
        public int Carica()
        {
            _categorie = new List<Categoria>();
            _prodotti = new List<Prodotto>();
            if (!Presente) return 0;

            using var conn = Apri(_percorso);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Categorie";
                using var r = cmd.ExecuteReader();
                var l = new LettoreRiga(r);
                while (l.Prossima())
                {
                    if (!l.Bool("Attiva", true)) continue;
                    int id = l.Int("Id");
                    if (id >= PrimaCategoriaTabacchi) continue;
                    _categorie.Add(new Categoria
                    {
                        Id = id,
                        Nome = l.Testo("Nome"),
                        RepartoFiscale = l.Int("RepartoFiscale", 1),
                        AliquotaIVA = l.Int("AliquotaIVA"),
                        Colore = l.Testo("Colore", "#2196F3"),
                        ColoreTesto = l.Testo("ColoreTestoCategoria", "#000000"),
                        Ordine = l.Int("Ordine")
                    });
                }
            }
            _categorie = _categorie.OrderBy(c => c.Ordine).ThenBy(c => c.Nome).ToList();
            var repartoPerCategoria = _categorie.ToDictionary(c => c.Id, c => c.RepartoFiscale);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Prodotti";
                using var r = cmd.ExecuteReader();
                var l = new LettoreRiga(r);
                while (l.Prossima())
                {
                    if (!l.Bool("Attivo", true)) continue;
                    int cat = l.Int("CategoriaId");
                    if (!repartoPerCategoria.TryGetValue(cat, out int reparto)) continue; // categoria nascosta o tabacchi
                    _prodotti.Add(new Prodotto
                    {
                        Id = l.Int("Id"),
                        Codice = l.Testo("Codice"),
                        Barcode = l.Testo("Barcode"),
                        Descrizione = l.Testo("Descrizione"),
                        PrezzoCentesimi = l.Int("PrezzoCentesimi"),
                        CategoriaId = cat,
                        RepartoFiscale = reparto,
                        Colore = l.Testo("Colore", ""),
                        ColoreTesto = l.Testo("ColoreTestoTasto", "#FFFFFF"),
                        IsPreferito = l.Bool("IsPreferito")
                    });
                }
            }
            _prodotti = _prodotti.OrderBy(p => p.Descrizione).ToList();
            LogManager.Info($"Catalogo caricato: {_categorie.Count} categorie, {_prodotti.Count} prodotti");
            return _prodotti.Count;
        }

        /// <summary>
        /// Importa un pos.db copiato dal PC. Verifica che contenga le tabelle giuste
        /// prima di sostituire il catalogo attuale.
        /// </summary>
        public int Importa(Stream sorgente)
        {
            string tmp = _percorso + ".import";
            using (var f = File.Create(tmp)) sorgente.CopyTo(f);

            try
            {
                using (var conn = Apri(tmp))
                using (var cmd = conn.CreateCommand())
                {
                    // pos.db sul PC è in modalità WAL: lo riporto a file singolo,
                    // così non servono i file -wal/-shm accanto.
                    cmd.CommandText = "PRAGMA journal_mode=DELETE;";
                    cmd.ExecuteScalar();
                    cmd.CommandText = "SELECT COUNT(*) FROM Prodotti";
                    cmd.ExecuteScalar();
                    cmd.CommandText = "SELECT COUNT(*) FROM Categorie";
                    cmd.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                SqliteConnection.ClearAllPools();
                File.Delete(tmp);
                throw new InvalidDataException("Il file scelto non è un database pos.db di POS Touch.", ex);
            }

            SqliteConnection.ClearAllPools();
            File.Move(tmp, _percorso, overwrite: true);
            return Carica();
        }

        internal static SqliteConnection Apri(string percorso)
        {
            // Lettura e scrittura (non sola lettura): un DB in WAL aperto in sola lettura
            // fallisce se mancano i file -wal/-shm. Il catalogo comunque non viene modificato.
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = percorso,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
            var conn = new SqliteConnection(cs);
            conn.Open();
            return conn;
        }
    }
}
