using System;
using System.Collections.Generic;
using System.Linq;

namespace PosTouch.Core.Configurazione
{
    /// <summary>
    /// Parametri di UNA stampa (scheda "Configura" di POSBY): su quale stampante esce e come.
    /// IP vuoto = stampa non assegnata.
    /// </summary>
    public class ParametriStampa
    {
        public string IP { get; set; } = "";
        public int Porta { get; set; } = 9100;
        public int RigheSaltoIniziale { get; set; } = 0;
        public int RigheSaltoFinale { get; set; } = 4;
        /// <summary>0 = nessuno, 1 = parziale, 2 = totale.</summary>
        public int Taglio { get; set; } = 1;
        /// <summary>0 = nessuno, 1 = 1 beep, 2 = 2 beep.</summary>
        public int Cicalino { get; set; } = 0;
        /// <summary>Stampa capovolta (stampante montata al contrario).</summary>
        public bool Inversione { get; set; }
        /// <summary>Prima di stampare chiede lo stato alla stampante (carta finita, coperchio aperto).</summary>
        public bool ControlloStato { get; set; }

        public bool Assegnata => !string.IsNullOrWhiteSpace(IP);
        public string Descrizione => Assegnata ? (Porta == 9100 ? IP : $"{IP}:{Porta}") : "";

        public ParametriStampa Copia() => (ParametriStampa)MemberwiseClone();

        public void Normalizza()
        {
            IP = (IP ?? "").Trim();
            if (Porta < 1 || Porta > 65535) Porta = 9100;
            RigheSaltoIniziale = Math.Clamp(RigheSaltoIniziale, 0, 20);
            RigheSaltoFinale = Math.Clamp(RigheSaltoFinale, 0, 20);
            Taglio = Math.Clamp(Taglio, 0, 2);
            Cicalino = Math.Clamp(Cicalino, 0, 2);
        }
    }

    /// <summary>Uscita di produzione (BAR, CUCINA, PASTICCERIA…): il numero è il "centro di produzione" di categorie e articoli.</summary>
    public class Uscita
    {
        public int Numero { get; set; }
        public string Nome { get; set; } = "";
    }

    /// <summary>Le stampe di una sala (o di tutte, se le impostazioni per sala sono spente).</summary>
    public class StampeSala
    {
        public ParametriStampa Preconto { get; set; } = new ParametriStampa();
        public ParametriStampa Riepiloghi { get; set; } = new ParametriStampa();
        /// <summary>Numero uscita → parametri.</summary>
        public Dictionary<int, ParametriStampa> Uscite { get; set; } = new Dictionary<int, ParametriStampa>();

        public ParametriStampa Uscita(int numero)
        {
            if (!Uscite.TryGetValue(numero, out var p)) Uscite[numero] = p = new ParametriStampa();
            return p;
        }

        public StampeSala Copia() => new StampeSala
        {
            Preconto = (Preconto ?? new ParametriStampa()).Copia(),
            Riepiloghi = (Riepiloghi ?? new ParametriStampa()).Copia(),
            Uscite = (Uscite ?? new Dictionary<int, ParametriStampa>()).ToDictionary(x => x.Key, x => x.Value.Copia())
        };

        public void Normalizza()
        {
            Preconto ??= new ParametriStampa();
            Riepiloghi ??= new ParametriStampa();
            Uscite ??= new Dictionary<int, ParametriStampa>();
            Preconto.Normalizza();
            Riepiloghi.Normalizza();
            foreach (var p in Uscite.Values) p?.Normalizza();
            foreach (var k in Uscite.Where(x => x.Value == null).Select(x => x.Key).ToList()) Uscite[k] = new ParametriStampa();
        }
    }

    /// <summary>
    /// Opzioni di stampa come POSBY: elenco IP delle stampanti di rete, uscite di produzione (max 8),
    /// stampe di base e, se attivo, una configurazione completa per ogni sala (Banco compreso).
    /// </summary>
    public class ConfigStampe
    {
        public const int MaxUscite = 8;
        /// <summary>Chiave del Banco nelle impostazioni per sala (le sale vere hanno Id ≥ 1, i conti clienti 0).</summary>
        public const int SalaBanco = -1;

        /// <summary>"Gestione porte TCP/IP": indirizzi delle stampanti di rete (es. "10.10.10.52" o "10.10.10.52:9100").</summary>
        public List<string> IndirizziIP { get; set; } = new List<string>();
        /// <summary>Uscite aggiunte (si vedono solo queste).</summary>
        public List<Uscita> Uscite { get; set; } = new List<Uscita>();
        public bool ImpostazioniPerSala { get; set; }
        /// <summary>Comanda da banco: a chiusura di scontrino o conto al banco stampa la comanda con il numero eliminacode.</summary>
        public bool ComandeDaBanco { get; set; }
        /// <summary>Eliminacode su ticket della termica preconti invece che in appendice allo scontrino.</summary>
        public bool TicketEliminacodeSuTermica { get; set; }
        public StampeSala Base { get; set; } = new StampeSala();
        /// <summary>Id sala → stampe di quella sala (usate solo con <see cref="ImpostazioniPerSala"/>).</summary>
        public Dictionary<int, StampeSala> PerSala { get; set; } = new Dictionary<int, StampeSala>();

        /// <summary>Stampe valide per una sala (la sua pagina se le impostazioni per sala sono attive, altrimenti la base).</summary>
        public StampeSala Di(int salaId) =>
            ImpostazioniPerSala && PerSala.TryGetValue(salaId, out var s) && s != null ? s : Base;

        /// <summary>Pagina da modificare per una sala (creata copiando la base la prima volta).</summary>
        public StampeSala PaginaDi(int salaId)
        {
            if (!PerSala.TryGetValue(salaId, out var s) || s == null) PerSala[salaId] = s = Base.Copia();
            return s;
        }

        public Uscita UscitaDa(int numero) => Uscite.FirstOrDefault(u => u.Numero == numero);
        public string NomeUscita(int numero) => UscitaDa(numero)?.Nome is { Length: > 0 } n ? n : $"Uscita {numero}";

        /// <summary>Stampante di un'uscita in una sala; null se l'uscita non esiste o non ha stampante.</summary>
        public ParametriStampa StampanteUscita(int salaId, int numero)
        {
            if (UscitaDa(numero) == null) return null;
            return Di(salaId).Uscite.TryGetValue(numero, out var p) && p != null && p.Assegnata ? p : null;
        }

        /// <summary>
        /// Stampanti dell'elenco IP con un nome che dice a cosa servono (es. "10.10.10.52 · BAR, PRECONTI"),
        /// per la deviazione dalla coda di stampa.
        /// </summary>
        public List<StampanteRete> StampantiConNome()
        {
            var pagine = new List<StampeSala> { Base };
            if (ImpostazioniPerSala) pagine.AddRange(PerSala.Values);
            var l = new List<StampanteRete>();
            int id = 1;
            foreach (var ind in IndirizziIP)
            {
                var (ip, porta) = Separa(ind);
                bool Uguale(ParametriStampa p) => p != null && p.IP == ip && p.Porta == porta;
                var usi = new List<string>();
                foreach (var u in Uscite) if (pagine.Any(pg => pg.Uscite.TryGetValue(u.Numero, out var p) && Uguale(p))) usi.Add(u.Nome);
                if (pagine.Any(pg => Uguale(pg.Preconto))) usi.Add("PRECONTI");
                l.Add(new StampanteRete { Id = id++, IP = ip, Porta = porta, Nome = usi.Count == 0 ? ind : $"{ind} · {string.Join(", ", usi)}" });
            }
            return l;
        }

        /// <summary>"10.10.10.52:9100" → (IP, porta).</summary>
        public static (string ip, int porta) Separa(string indirizzo)
        {
            var s = (indirizzo ?? "").Trim();
            int i = s.LastIndexOf(':');
            if (i > 0 && int.TryParse(s[(i + 1)..], out int porta) && porta > 0 && porta <= 65535) return (s[..i], porta);
            return (s, 9100);
        }

        public void Normalizza()
        {
            IndirizziIP ??= new List<string>();
            IndirizziIP = IndirizziIP.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
            Uscite ??= new List<Uscita>();
            Uscite = Uscite.Where(u => u != null && u.Numero >= 1 && u.Numero <= MaxUscite)
                           .GroupBy(u => u.Numero).Select(g => g.First()).OrderBy(u => u.Numero).ToList();
            Base ??= new StampeSala();
            Base.Normalizza();
            PerSala ??= new Dictionary<int, StampeSala>();
            foreach (var k in PerSala.Keys.ToList()) { PerSala[k] ??= Base.Copia(); PerSala[k].Normalizza(); }
        }

        /// <summary>Impronta del contenuto (per capire se ci sono modifiche non salvate).</summary>
        public string Firma()
        {
            Normalizza();
            return System.Text.Json.JsonSerializer.Serialize(this, JsonContesto.Default.ConfigStampe);
        }

        public ConfigStampe Copia()
        {
            Normalizza();
            var c = (ConfigStampe)MemberwiseClone();
            c.IndirizziIP = IndirizziIP.ToList();
            c.Uscite = Uscite.Select(u => new Uscita { Numero = u.Numero, Nome = u.Nome }).ToList();
            c.Base = Base.Copia();
            c.PerSala = PerSala.ToDictionary(x => x.Key, x => x.Value.Copia());
            return c;
        }

        /// <summary>
        /// Prima volta con le nuove opzioni di stampa: costruisce la configurazione dai vecchi centri, stampanti,
        /// eccezioni per sala e termica dei conti, così niente va perso.
        /// </summary>
        public static ConfigStampe DaVecchia(ConfigSale vecchia, string ipTermica, int portaTermica, bool cicalinoComande)
        {
            var c = new ConfigStampe();
            vecchia ??= new ConfigSale();
            vecchia.Normalizza();
            string Indirizzo(string ip, int porta) => porta == 9100 ? ip.Trim() : $"{ip.Trim()}:{porta}";
            ParametriStampa Da(StampanteRete s, bool beep) => s == null || string.IsNullOrWhiteSpace(s.IP) ? new ParametriStampa()
                : new ParametriStampa { IP = s.IP.Trim(), Porta = s.Porta, Cicalino = beep ? 1 : 0, Taglio = 2 };   // le versioni vecchie tagliavano a fondo

            foreach (var s in vecchia.Stampanti.Where(s => !string.IsNullOrWhiteSpace(s.IP))) c.IndirizziIP.Add(Indirizzo(s.IP, s.Porta));
            if (!string.IsNullOrWhiteSpace(ipTermica)) c.IndirizziIP.Add(Indirizzo(ipTermica, portaTermica));

            foreach (var centro in vecchia.Centri.Where(x => x.Id >= 1 && x.Id <= MaxUscite))
            {
                c.Uscite.Add(new Uscita { Numero = centro.Id, Nome = centro.Nome });
                c.Base.Uscite[centro.Id] = Da(vecchia.StampanteDa(centro.StampanteId), cicalinoComande);
            }
            if (!string.IsNullOrWhiteSpace(ipTermica))
                c.Base.Preconto = new ParametriStampa { IP = ipTermica.Trim(), Porta = portaTermica, Taglio = 2 };
            foreach (var centro in vecchia.Centri.Where(x => x.Id < 1 || x.Id > MaxUscite))
                Diagnostica.LogManager.Warning($"Passaggio stampe: centro {centro.Id} {centro.Nome} non riportato (uscite ammesse 1-{MaxUscite})");

            // vecchie eccezioni "sala × centro → stampante": diventano pagine per sala
            if (vecchia.StampantiSala.Any(x => x.StampanteId > 0))
            {
                c.ImpostazioniPerSala = true;
                foreach (var sala in vecchia.Sale) c.PerSala[sala.Id] = c.Base.Copia();
                foreach (var e in vecchia.StampantiSala.Where(x => x.StampanteId > 0 && c.PerSala.ContainsKey(x.SalaId) && x.CentroId >= 1 && x.CentroId <= MaxUscite))
                    c.PerSala[e.SalaId].Uscite[e.CentroId] = Da(vecchia.StampanteDa(e.StampanteId), cicalinoComande);
            }
            c.Normalizza();
            return c;
        }
    }
}
