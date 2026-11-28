using System;
using System.Collections.Generic;
using System.Linq;

namespace PosTouch.Core.Configurazione
{
    /// <summary>Stampante di rete (ESC/POS) usata per le comande.</summary>
    public class StampanteRete
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public string IP { get; set; } = "";
        public int Porta { get; set; } = 9100;
    }

    /// <summary>Centro di produzione (CUCINA, BAR, PIZZERIA...) con la sua stampante predefinita.</summary>
    public class CentroProduzione
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public int StampanteId { get; set; }
    }

    /// <summary>Sala con i suoi tavoli.</summary>
    public class Sala
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public int NumeroTavoli { get; set; } = 10;
        /// <summary>Numero del primo tavolo (es. 101 per numerare la seconda sala da 101).</summary>
        public int NumeroIniziale { get; set; } = 1;
        /// <summary>Articolo "coperto" chiesto all'apertura del tavolo (0 = nessuno).</summary>
        public int CopertoProdottoId { get; set; }
        /// <summary>Listino dei prezzi della sala (1-4), come ListinoSala del PC.</summary>
        public int Listino { get; set; } = 1;
    }

    /// <summary>Eccezione come POSBY: in questa sala il centro stampa su un'altra stampante.</summary>
    public class StampanteSala
    {
        public int SalaId { get; set; }
        public int CentroId { get; set; }
        public int StampanteId { get; set; }
    }

    /// <summary>Configurazione di sale, tavoli, centri di produzione e comande (parte di <see cref="Impostazioni"/>).</summary>
    public class ConfigSale
    {
        public bool AbilitaSale { get; set; } = false;
        /// <summary>Conti clienti sospesi (scheda 👤 CONTI): conti con il nome del cliente, sala 0.</summary>
        public bool AbilitaContiClienti { get; set; } = false;
        public int NumeroSlotContiClienti { get; set; } = 10;
        /// <summary>Listino dei conti clienti (1-4), come ListinoContiClienti del PC.</summary>
        public int ListinoContiClienti { get; set; } = 1;
        /// <summary>Mappa tavoli: la striscia dei tempi diventa arancione se l'ultimo ordine/VAI è più vecchio di questi minuti (0 = mai).</summary>
        public int MinutiAvvisoTavolo { get; set; } = 25;
        public const int SalaConti = 0;
        public List<Sala> Sale { get; set; } = new List<Sala> { new Sala { Id = 1, Nome = "Sala 1" } };
        // ── SOLO PER IL PASSAGGIO ALLE "OPZIONI DI STAMPA" (ConfigStampe): lette dai file vecchi, poi svuotate ──
        public List<StampanteRete> Stampanti { get; set; } = new List<StampanteRete>();
        public List<CentroProduzione> Centri { get; set; } = new List<CentroProduzione>
        {
            new CentroProduzione { Id = 1, Nome = "CUCINA" },
            new CentroProduzione { Id = 2, Nome = "BAR" }
        };
        public List<StampanteSala> StampantiSala { get; set; } = new List<StampanteSala>();
        // ── fine voci vecchie ──

        // ── comande (stesse opzioni del PC) ──
        public bool AbilitaComande { get; set; } = true;
        public bool StampaRiepilogoAltriCentri { get; set; } = true;
        public bool StampaStorniConNoStampa { get; set; } = true;
        public bool AccorpaStampaComanda { get; set; } = true;
        /// <summary>0=Off, 1=BEL, 2=Epson ESC(A, 3=ESC B, 4=DLE DC4</summary>
        public int CicalinoComande { get; set; } = 0;
        public int StileComandaProdotto { get; set; } = 8;
        public int StileComandaVariante { get; set; } = 5;
        public int StileComandaSeparatore { get; set; } = 4;

        // ── portate ──
        public bool AbilitaPortate { get; set; } = false;
        public bool AbilitaSpostaTavolo { get; set; } = true;
        public bool AbilitaUnisciConti { get; set; } = true;
        public List<string> SeparatoriPortate { get; set; } = new List<string> { "SEGUE", "SEGUE 2", "SEGUE 3" };

        public Sala SalaDa(int id) => Sale.FirstOrDefault(s => s.Id == id);
        public CentroProduzione CentroDa(int id) => Centri.FirstOrDefault(c => c.Id == id);
        public StampanteRete StampanteDa(int id) => Stampanti.FirstOrDefault(s => s.Id == id);

        /// <summary>Copia profonda (le impostazioni vengono modificate su una copia e salvate solo con SALVA).</summary>
        /// <summary>Liste mai null (JSON scritto a mano o di versioni vecchie).</summary>
        public void Normalizza()
        {
            Sale ??= new List<Sala>();
            Stampanti ??= new List<StampanteRete>();
            Centri ??= new List<CentroProduzione>();
            StampantiSala ??= new List<StampanteSala>();
            SeparatoriPortate ??= new List<string>();
            foreach (var s in Sale) s.Listino = Math.Clamp(s.Listino, 1, 4);   // listini 1-4 come il PC
            ListinoContiClienti = Math.Clamp(ListinoContiClienti, 1, 4);
            MinutiAvvisoTavolo = Math.Clamp(MinutiAvvisoTavolo, 0, 240);
        }

        /// <summary>Impronta del contenuto: la pagina confronta apertura e chiusura per chiedere se salvare.</summary>
        public string Firma() => System.Text.Json.JsonSerializer.Serialize(this, JsonContesto.Default.ConfigSale);

        public ConfigSale Copia()
        {
            Normalizza();
            var c = (ConfigSale)MemberwiseClone();
            c.Sale = Sale.Select(s => new Sala { Id = s.Id, Nome = s.Nome, NumeroTavoli = s.NumeroTavoli, NumeroIniziale = s.NumeroIniziale, CopertoProdottoId = s.CopertoProdottoId, Listino = s.Listino }).ToList();
            c.Stampanti = Stampanti.Select(s => new StampanteRete { Id = s.Id, Nome = s.Nome, IP = s.IP, Porta = s.Porta }).ToList();
            c.Centri = Centri.Select(x => new CentroProduzione { Id = x.Id, Nome = x.Nome, StampanteId = x.StampanteId }).ToList();
            c.StampantiSala = StampantiSala.Select(x => new StampanteSala { SalaId = x.SalaId, CentroId = x.CentroId, StampanteId = x.StampanteId }).ToList();
            c.SeparatoriPortate = SeparatoriPortate.ToList();
            return c;
        }
    }
}
