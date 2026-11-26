using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;

namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Operazioni sui dati del tablet usate dalle Impostazioni: import del pos.db del PC, dati di prova,
    /// controllo delle uscite mancanti, log da condividere. Nessuna grafica: la pagina chiede conferme e mostra esiti.
    /// </summary>
    public static class ManutenzioneDati
    {
        /// <summary>Importa un pos.db (catalogo): restituisce il numero di articoli.</summary>
        public static Task<int> ImportaCatalogoAsync(Stream sorgente) => Task.Run(() => ContestoCassa.Catalogo.Importa(sorgente));

        /// <summary>Motivo per cui ora non si possono caricare i dati di prova (null = si può).</summary>
        public static string BloccoDatiProva()
        {
            if (ContestoCassa.Tavoli.Aperti().Any(t => t.Articoli > 0)) return "Ci sono tavoli o conti aperti: incassali prima di caricare i dati di prova.";
            if (!ContestoCassa.Carrello.Vuoto) return "Al banco c'è uno scontrino aperto: chiudilo prima di caricare i dati di prova.";
            return null;
        }

        /// <summary>
        /// 🧪 Dati di prova: catalogo d'esempio dentro l'app (catalogo_prova.db) e sale, coperti, portate e uscite adatti.
        /// Stampanti e impostazioni fiscali restano quelle impostate. Restituisce il numero di articoli.
        /// </summary>
        public static async Task<int> CaricaDatiProvaAsync()
        {
            int n;
            using (var s = await FileSystem.OpenAppPackageFileAsync("catalogo_prova.db"))
            using (var copia = new MemoryStream())
            {
                await s.CopyToAsync(copia);
                copia.Position = 0;
                n = await ImportaCatalogoAsync(copia);
            }
            var imp = ContestoCassa.Impostazioni.Copia();
            ConfiguraProva(imp, ContestoCassa.Catalogo);
            ContestoCassa.SalvaImpostazioni(imp);
            LogManager.Info($"Dati di prova caricati: {n} articoli");
            return n;
        }

        /// <summary>Sale, coperti, portate e uscite adatti al catalogo di prova (le stampanti restano quelle impostate).</summary>
        internal static void ConfiguraProva(Impostazioni imp, Catalogo cat)
        {
            int coperto = cat.TuttiProdotti.FirstOrDefault(p => p.Descrizione == "COPERTO")?.Id ?? 0;
            int copertoTerrazza = cat.TuttiProdotti.FirstOrDefault(p => p.Descrizione == "COPERTO TERRAZZA")?.Id ?? coperto;
            var s = imp.Sale;
            s.AbilitaSale = true;
            s.AbilitaContiClienti = true;
            s.NumeroSlotContiClienti = 10;
            s.AbilitaComande = true;
            s.AbilitaPortate = true;
            if (s.SeparatoriPortate.Count == 0) s.SeparatoriPortate = new List<string> { "SEGUE", "SEGUE 2", "SEGUE 3" };
            s.Sale = new List<Sala>
            {
                new Sala { Id = 1, Nome = "SALA 1", NumeroTavoli = 10, NumeroIniziale = 1, CopertoProdottoId = coperto },
                new Sala { Id = 2, Nome = "TERRAZZA", NumeroTavoli = 10, NumeroIniziale = 101, CopertoProdottoId = copertoTerrazza }
            };
            imp.PreparaStampe();
            var st = imp.Stampe;
            foreach (var (num, nome) in new[] { (1, "CUCINA"), (2, "BAR"), (3, "PIZZERIA") })
            {
                var u = st.UscitaDa(num);
                if (u == null) st.Uscite.Add(new Uscita { Numero = num, Nome = nome }); else u.Nome = nome;
            }
            st.Normalizza();
        }

        /// <summary>Uscite (centri) usate da categorie e articoli ma non impostate sul tablet: quegli articoli non andrebbero in comanda.</summary>
        public static List<int> UsciteMancanti()
        {
            var imp = ContestoCassa.Impostazioni;
            if (!imp.Sale.AbilitaSale && imp.Stampe?.ComandeDaBanco != true) return new List<int>();
            var noti = (imp.Stampe?.Uscite ?? new()).Select(u => u.Numero).ToHashSet();
            return ContestoCassa.Catalogo.TutteCategorie.Select(c => c.CentroProduzioneId)
                   .Concat(ContestoCassa.Catalogo.TuttiProdotti.Select(p => p.CentroProduzioneId))
                   .Where(id => id > 0 && !noti.Contains(id)).Distinct().OrderBy(id => id).ToList();
        }

        /// <summary>Copia del log pronta da condividere (null = nessun log).</summary>
        public static string CopiaLog()
        {
            LogManager.Svuota();   // le ultime righe in coda finiscono nel file prima di condividerlo
            string percorso = LogManager.Percorso;
            if (percorso == null || !File.Exists(percorso)) return null;
            string copia = Path.Combine(FileSystem.CacheDirectory, $"postouch_log_{DateTime.Now:yyyyMMdd_HHmm}.txt");
            File.Copy(percorso, copia, overwrite: true);
            return copia;
        }
    }
}
