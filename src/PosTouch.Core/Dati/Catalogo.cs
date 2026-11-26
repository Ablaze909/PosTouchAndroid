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
        private List<Variante> _varianti = new List<Variante>();

        /// <summary>Tutte le varianti (anche non attive), per il Back office.</summary>
        public IReadOnlyList<Variante> TutteVarianti => _varianti;

        /// <summary>Varianti attive di una categoria, per gruppo e ordine (GetVariantiPerCategoria del PC).</summary>
        public List<Variante> VariantiDi(int categoriaId) => _varianti
            .Where(v => v.Attiva && v.Categorie.Contains(categoriaId))
            .OrderBy(v => v.Gruppo).ThenBy(v => v.Ordine).ThenBy(v => v.Nome).ToList();

        public Prodotto ProdottoDa(int id) => _prodotti.FirstOrDefault(p => p.Id == id);

        public Catalogo(string cartellaDati)
        {
            _percorso = Path.Combine(cartellaDati, NomeFile);
        }

        public bool Presente => File.Exists(_percorso);
        /// <summary>Aumenta a ogni caricamento: le schermate ridisegnano i tasti solo se è cambiato.</summary>
        public int Versione { get; private set; }
        internal string Percorso => _percorso;
        /// <summary>Categorie visibili nel POS (attive), in ordine.</summary>
        public IReadOnlyList<Categoria> Categorie => _categorie.Where(c => c.Attiva).ToList();
        /// <summary>Tutte le categorie, anche nascoste (Back office).</summary>
        public IReadOnlyList<Categoria> TutteCategorie => _categorie;
        /// <summary>Tutti gli articoli, anche nascosti (Back office).</summary>
        public IReadOnlyList<Prodotto> TuttiProdotti => _prodotti;

        private bool Vendibile(Prodotto p) => p.Attivo && _categorie.Any(c => c.Id == p.CategoriaId && c.Attiva);

        public IEnumerable<Prodotto> ProdottiDi(int categoriaId) => _prodotti.Where(p => p.CategoriaId == categoriaId && Vendibile(p));
        public IEnumerable<Prodotto> Preferiti() => _prodotti.Where(p => p.IsPreferito && Vendibile(p));

        /// <summary>Articolo "codice bilancia" con questo codice (le 5 cifre dell'etichetta), anche se non è marcato come tale.</summary>
        public Prodotto CercaCodiceBilancia(string codice5)
        {
            if (string.IsNullOrWhiteSpace(codice5)) return null;
            return _prodotti.FirstOrDefault(p => Vendibile(p) && p.IsCodiceBilancia && (p.Barcode == codice5 || p.Codice == codice5))
                ?? _prodotti.FirstOrDefault(p => Vendibile(p) && (p.Barcode == codice5 || p.Codice == codice5));
        }

        public Prodotto CercaBarcode(string codice)
        {
            if (string.IsNullOrWhiteSpace(codice)) return null;
            codice = codice.Trim();
            return _prodotti.FirstOrDefault(p => Vendibile(p) && (p.Barcode == codice || p.Codice == codice));
        }

        /// <summary>Carica in memoria categorie e prodotti (anche nascosti). Restituisce il numero di prodotti.</summary>
        public int Carica()
        {
            var categorie = new List<Categoria>();
            var prodotti = new List<Prodotto>();
            if (!Presente) { _categorie = categorie; _prodotti = prodotti; Versione++; return 0; }

            using var conn = Apri(_percorso);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Categorie";
                using var r = cmd.ExecuteReader();
                var l = new LettoreRiga(r);
                while (l.Prossima())
                {
                    int id = l.Int("Id");
                    if (id >= PrimaCategoriaTabacchi) continue;
                    categorie.Add(new Categoria
                    {
                        Id = id,
                        Nome = l.Testo("Nome"),
                        RepartoFiscale = l.Int("RepartoFiscale", 1),
                        AliquotaIVA = l.Int("AliquotaIVA", 10),
                        Colore = l.Testo("Colore", "#2196F3"),
                        ColoreTesto = l.Testo("ColoreTestoCategoria", "#000000"),
                        Icona = l.Testo("Icona", ""),
                        Ordine = l.Int("Ordine"),
                        Attiva = l.Bool("Attiva", true),
                        CentroProduzioneId = l.Int("CentroProduzioneId")
                    });
                }
            }
            categorie = categorie.OrderBy(c => c.Ordine).ThenBy(c => c.Nome).ToList();
            var perId = categorie.ToDictionary(c => c.Id);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Prodotti";
                using var r = cmd.ExecuteReader();
                var l = new LettoreRiga(r);
                while (l.Prossima())
                {
                    int cat = l.Int("CategoriaId");
                    if (!perId.TryGetValue(cat, out var c)) continue; // tabacchi o categoria inesistente
                    prodotti.Add(new Prodotto
                    {
                        Id = l.Int("Id"),
                        Codice = l.Testo("Codice"),
                        Barcode = l.Testo("Barcode"),
                        Descrizione = l.Testo("Descrizione"),
                        PrezzoCentesimi = l.Int("PrezzoCentesimi"),
                        CostoCentesimi = l.Int("CostoCentesimi"),
                        CategoriaId = cat,
                        CategoriaNome = c.Nome,
                        RepartoFiscale = c.RepartoFiscale,
                        AliquotaIVA = c.AliquotaIVA,
                        Colore = l.Testo("Colore", ""),
                        ColoreTesto = l.Testo("ColoreTestoTasto", "#FFFFFF"),
                        DimensioneFontTasto = l.Int("DimensioneFontTasto", 12),
                        IsPreferito = l.Bool("IsPreferito"),
                        Attivo = l.Bool("Attivo", true),
                        VenditaAPeso = l.Bool("VenditaAPeso"),
                        UnitaPeso = l.Testo("UnitaPeso", "Kg"),
                        IsCombinazione = l.Bool("IsCombinazione"),
                        NumeroGusti = Math.Clamp(l.Int("NumeroGusti", 2), 2, 8),
                        PoliticaPrezzo = Math.Clamp(l.Int("PoliticaPrezzo"), 0, 2),
                        IsCodiceBilancia = l.Bool("IsCodiceBilancia"),
                        VariantiProporzionali = l.Bool("VariantiProporzionali"),
                        CentroProduzioneId = l.Int("CentroProduzioneId"),
                        CentroStampa = CentroEffettivo(l.Int("CentroProduzioneId"), c.CentroProduzioneId)
                    });
                }
            }
            prodotti = prodotti.OrderBy(p => p.Descrizione).ToList();
            var varianti = CaricaVarianti(conn);
            foreach (var c in categorie)
                c.NumeroProdotti = prodotti.Count(p => p.CategoriaId == c.Id && p.Attivo);

            _categorie = categorie;
            _prodotti = prodotti;
            _varianti = varianti;
            Versione++;
            LogManager.Info($"Catalogo caricato: {categorie.Count} categorie, {prodotti.Count} prodotti");
            return prodotti.Count;
        }

        /// <summary>Varianti e categorie collegate; un pos.db senza tabelle varianti dà una lista vuota.</summary>
        private static List<Variante> CaricaVarianti(SqliteConnection conn)
        {
            var lista = new List<Variante>();
            try
            {
                var perId = new Dictionary<int, Variante>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM Varianti";
                    using var r = cmd.ExecuteReader();
                    var l = new LettoreRiga(r);
                    while (l.Prossima())
                    {
                        var v = new Variante
                        {
                            Id = l.Int("Id"), Nome = l.Testo("Nome"), Gruppo = l.Testo("Gruppo", ""),
                            Tipo = (TipoVariante)Math.Clamp(l.Int("Tipo"), 0, 1),
                            PrezzoAggiuntaCent = l.Int("PrezzoAggiuntaCent"), PrezzoRimozioneCent = l.Int("PrezzoRimozioneCent"),
                            Ordine = l.Int("Ordine"), Attiva = l.Bool("Attiva", true)
                        };
                        perId[v.Id] = v;
                        lista.Add(v);
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT CategoriaId, VarianteId FROM CategorieVarianti";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (perId.TryGetValue(r.GetInt32(1), out var v)) v.Categorie.Add(r.GetInt32(0));
                }
            }
            catch (SqliteException)
            {
                // tabelle assenti (pos.db vecchio o catalogo nuovo): nessuna variante
            }
            return lista.OrderBy(v => v.Gruppo).ThenBy(v => v.Ordine).ThenBy(v => v.Nome).ToList();
        }

        /// <summary>Articolo: &gt;0 il suo centro, -1 nessuna stampa, 0 quello della categoria.</summary>
        public static int CentroEffettivo(int centroArticolo, int centroCategoria)
            => centroArticolo > 0 ? centroArticolo : centroArticolo < 0 ? 0 : Math.Max(0, centroCategoria);

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
