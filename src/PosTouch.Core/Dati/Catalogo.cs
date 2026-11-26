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
        /// <summary>Come il PC: 8000-8999 Gratta e Vinci, 9000-9999 tabacchi (gestiti a parte).</summary>
        public const int PrimaCategoriaRiservata = 8000;

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

        public Prodotto ProdottoDa(int id) => _indice.PerId.TryGetValue(id, out var p) ? p : null;

        public Catalogo(string cartellaDati)
        {
            _percorso = Path.Combine(cartellaDati, NomeFile);
        }

        public bool Presente => File.Exists(_percorso);
        /// <summary>Aumenta a ogni caricamento: le schermate ridisegnano i tasti solo se è cambiato.</summary>
        public int Versione { get; private set; }
        internal string Percorso => _percorso;
        /// <summary>Categorie visibili nel POS (attive), in ordine.</summary>
        public IReadOnlyList<Categoria> Categorie => _indice.CategorieAttive;
        /// <summary>Tutte le categorie, anche nascoste (Back office).</summary>
        public IReadOnlyList<Categoria> TutteCategorie => _categorie;
        /// <summary>Tutti gli articoli, anche nascosti (Back office).</summary>
        public IReadOnlyList<Prodotto> TuttiProdotti => _prodotti;

        private bool Vendibile(Prodotto p) => p.Attivo && _indice.IdCategorieAttive.Contains(p.CategoriaId);

        /// <summary>
        /// Indici calcolati una volta a ogni caricamento (prestazioni): prima ogni cambio categoria, ogni barcode
        /// e ogni ricalcolo combinazione scorrevano tutto il catalogo, e per ogni articolo tutte le categorie.
        /// </summary>
        private sealed class Indice
        {
            public HashSet<int> IdCategorieAttive = new();
            public List<Categoria> CategorieAttive = new();
            public Dictionary<int, Prodotto> PerId = new();
            public Dictionary<int, List<Prodotto>> PerCategoria = new();
            public List<Prodotto> Preferiti = new();
        }
        private Indice _indice = new Indice();

        private static Indice CreaIndice(List<Categoria> categorie, List<Prodotto> prodotti)
        {
            var i = new Indice();
            i.CategorieAttive = categorie.Where(c => c.Attiva).ToList();
            i.IdCategorieAttive = i.CategorieAttive.Select(c => c.Id).ToHashSet();
            foreach (var p in prodotti)
            {
                i.PerId.TryAdd(p.Id, p);
                if (!p.Attivo || !i.IdCategorieAttive.Contains(p.CategoriaId)) continue;
                if (!i.PerCategoria.TryGetValue(p.CategoriaId, out var l)) i.PerCategoria[p.CategoriaId] = l = new List<Prodotto>();
                l.Add(p);
                if (p.IsPreferito) i.Preferiti.Add(p);
            }
            return i;
        }

        public IEnumerable<Prodotto> ProdottiDi(int categoriaId) =>
            _indice.PerCategoria.TryGetValue(categoriaId, out var l) ? l : (IEnumerable<Prodotto>)Array.Empty<Prodotto>();
        public IEnumerable<Prodotto> Preferiti() => _indice.Preferiti;

        /// <summary>Articolo "codice bilancia" con questo codice (le 5 cifre dell'etichetta), anche se non è marcato come tale.</summary>
        public Prodotto CercaCodiceBilancia(string codice5)
        {
            if (string.IsNullOrWhiteSpace(codice5)) return null;
            return _prodotti.FirstOrDefault(p => Vendibile(p) && p.IsCodiceBilancia && (p.Barcode == codice5 || p.Codice == codice5))
                ?? _prodotti.FirstOrDefault(p => Vendibile(p) && (p.Barcode == codice5 || p.Codice == codice5));
        }

        public Prodotto CercaBarcode(string codice) => CercaBarcodeConQuantita(codice).prodotto;

        /// <summary>
        /// GetProdottoByBarcodeConQta del PC: tabella ProdottiBarcode (più barcode per articolo, con pezzi per confezione),
        /// poi Prodotti.Barcode, poi il Codice interno solo per articoli senza barcode.
        /// </summary>
        public (Prodotto prodotto, int pezzi) CercaBarcodeConQuantita(string codice)
        {
            if (string.IsNullOrWhiteSpace(codice)) return (null, 1);
            codice = codice.Trim();
            if (_barcode.TryGetValue(codice, out var b))
            {
                var p = _prodotti.FirstOrDefault(x => x.Id == b.prodottoId && Vendibile(x));
                if (p != null) return (p, Math.Max(1, b.pezzi));
            }
            var trovato = _prodotti.FirstOrDefault(p => Vendibile(p) && p.Barcode == codice)
                       ?? _prodotti.FirstOrDefault(p => Vendibile(p) && string.IsNullOrEmpty(p.Barcode) && p.Codice == codice);
            return (trovato, 1);
        }

        private Dictionary<string, (int prodottoId, int pezzi)> _barcode = new Dictionary<string, (int, int)>();

        private static Dictionary<string, (int, int)> CaricaBarcodeMultipli(SqliteConnection conn)
        {
            var m = new Dictionary<string, (int, int)>();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT ProdottoId, TRIM(Barcode), QuantitaConfezione FROM ProdottiBarcode";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string bc = r.IsDBNull(1) ? "" : r.GetString(1);
                    if (bc.Length > 0 && !m.ContainsKey(bc)) m[bc] = (r.GetInt32(0), r.IsDBNull(2) ? 1 : r.GetInt32(2));
                }
            }
            catch (SqliteException) { /* pos.db senza la tabella (versioni vecchie): solo Prodotti.Barcode */ }
            return m;
        }

        /// <summary>Carica in memoria categorie e prodotti (anche nascosti). Restituisce il numero di prodotti.</summary>
        public int Carica()
        {
            var categorie = new List<Categoria>();
            var prodotti = new List<Prodotto>();
            if (!Presente) { _categorie = categorie; _prodotti = prodotti; _indice = CreaIndice(categorie, prodotti); Versione++; return 0; }

            using var conn = Apri(_percorso);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Categorie";
                using var r = cmd.ExecuteReader();
                var l = new LettoreRiga(r);
                while (l.Prossima())
                {
                    int id = l.Int("Id");
                    if (id >= PrimaCategoriaRiservata) continue;
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
                        CentroProduzioneId = l.Int("CentroProduzioneId"),
                        EscludiComandaBanco = l.Bool("EscludiComandaBanco"),
                        Subito = l.Bool("Subito")
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
                    if (l.Int("Attivo", 1) < 0) continue;              // eliminato sul PC (Attivo = -1)
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
                        IsCoperto = l.Bool("IsCoperto"),
                        NumeroGusti = Math.Clamp(l.Int("NumeroGusti", 2), 2, 8),
                        PoliticaPrezzo = Math.Clamp(l.Int("PoliticaPrezzo"), 0, 2),
                        IsCodiceBilancia = l.Bool("IsCodiceBilancia"),
                        VariantiProporzionali = l.Bool("VariantiProporzionali"),
                        CentroProduzioneId = l.Int("CentroProduzioneId"),
                        CentroStampa = CentroEffettivo(l.Int("CentroProduzioneId"), c.CentroProduzioneId),
                        Subito = c.Subito,
                        Ordine = l.Int("Ordine")
                    });
                }
            }
            prodotti = prodotti.OrderBy(p => p.Ordine).ThenBy(p => p.Descrizione).ToList();   // come il PC
            var varianti = CaricaVarianti(conn);
            var barcode = CaricaBarcodeMultipli(conn);
            foreach (var c in categorie)
                c.NumeroProdotti = prodotti.Count(p => p.CategoriaId == c.Id && p.Attivo);

            _indice = CreaIndice(categorie, prodotti);   // prima dei riferimenti: chi legge vede sempre dati coerenti
            _categorie = categorie;
            _prodotti = prodotti;
            _varianti = varianti;
            _barcode = barcode;
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
                    cmd.CommandText = "PRAGMA integrity_check";
                    var integrita = cmd.ExecuteScalar() as string;
                    if (!string.Equals(integrita, "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Database danneggiato ({integrita}).");
                }
            }
            catch (Exception ex)
            {
                SqliteConnection.ClearAllPools();
                File.Delete(tmp);
                throw new InvalidDataException("Il file scelto non è un database pos.db di POS Touch.", ex);
            }

            SqliteConnection.ClearAllPools();
            // il catalogo attuale va da parte; le copie .bak/.bak2/.bak3 scalano SOLO se l'import riesce
            // (un import fallito non deve spingere fuori la copia buona di prima)
            string precedente = _percorso + ".precedente";
            bool cera = File.Exists(_percorso);
            if (cera) File.Copy(_percorso, precedente, overwrite: true);
            bool cerano = _prodotti.Count > 0;
            File.Move(tmp, _percorso, overwrite: true);
            try
            {
                int n = Carica();
                if (n == 0 && cerano) throw new InvalidDataException("Il file scelto non contiene articoli leggibili.");
                if (cera)
                {
                    if (File.Exists(_percorso + ".bak2")) File.Copy(_percorso + ".bak2", _percorso + ".bak3", overwrite: true);
                    if (File.Exists(_percorso + ".bak")) File.Copy(_percorso + ".bak", _percorso + ".bak2", overwrite: true);
                    File.Move(precedente, _percorso + ".bak", overwrite: true);
                }
                return n;
            }
            catch
            {
                // catalogo nuovo illeggibile o vuoto: si rimette quello di prima, come se l'import non fosse avvenuto
                SqliteConnection.ClearAllPools();
                if (cera) File.Move(precedente, _percorso, overwrite: true);
                else File.Delete(_percorso);   // primo import: niente file illeggibile al suo posto
                try { Carica(); } catch (Exception ex2) { LogManager.Error($"Ripristino catalogo dopo import fallito: {ex2.Message}"); }
                throw;
            }
        }

        /// <summary>Rimette il catalogo com'era prima dell'ultimo import (copia .bak).</summary>
        public int RipristinaPrecedente()
        {
            if (!File.Exists(_percorso + ".bak")) throw new FileNotFoundException("Nessuna copia del catalogo precedente.");
            SqliteConnection.ClearAllPools();
            string corrente = _percorso + ".prima_del_ripristino";
            if (File.Exists(_percorso)) File.Copy(_percorso, corrente, overwrite: true);
            File.Copy(_percorso + ".bak", _percorso, overwrite: true);
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
