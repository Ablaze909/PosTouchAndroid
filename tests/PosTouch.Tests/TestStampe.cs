using System.Linq;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Stampa;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    public static class TestStampe
    {
        public static void Esegui()
        {
            var v = new ConfigSale();
            v.Stampanti.Add(new StampanteRete { Id = 1, Nome = "A", IP = "10.0.0.5", Porta = 9100 });
            v.Stampanti.Add(new StampanteRete { Id = 2, Nome = "B", IP = "10.0.0.6", Porta = 9101 });
            v.Centri[0].StampanteId = 1; v.Centri[1].StampanteId = 2;
            v.StampantiSala.Add(new StampanteSala { SalaId = 1, CentroId = 1, StampanteId = 2 });
            var c = ConfigStampe.DaVecchia(v, "10.0.0.9", 9100, true);
            Ok(c.IndirizziIP.SequenceEqual(new[] { "10.0.0.5", "10.0.0.6:9101", "10.0.0.9" }), "IP migrati dalle vecchie stampanti");
            Ok(c.Uscite.Count == 2 && c.NomeUscita(1) == "CUCINA", "centri → uscite");
            Ok(c.ImpostazioniPerSala && c.StampanteUscita(1, 1).IP == "10.0.0.6", "eccezione sala → pagina della sala");
            Uguale("10.0.0.5", c.StampanteUscita(ConfigStampe.SalaBanco, 1)?.IP, "il banco usa la base");
            Uguale("10.0.0.9", c.Base.Preconto.IP, "vecchia termica → preconti");
            Ok(c.StampanteUscita(1, 7) == null, "uscita inesistente: nessuna stampante");
            var copia = c.Copia(); copia.PerSala[1].Uscite[1].IP = "1.1.1.1";
            Uguale("10.0.0.6", c.PerSala[1].Uscite[1].IP, "copia profonda");
            Ok(c.Firma().Length > 10, "impronta JSON");

            var corpo = new EscPos().Testo("CIAO").Byte();
            var f = Finitura.Applica(corpo, new ParametriStampa { RigheSaltoIniziale = 2, RigheSaltoFinale = 3, Taglio = 1, Cicalino = 2, Inversione = true }, 1);
            Ok(f[0] == 0x1B && f[1] == 0x40 && f[2] == 0x1B && f[3] == 0x7B && f[5] == 0x0A && f[6] == 0x0A, "inizio: init, capovolta, 2 righe");
            Ok(f[^4] == 0x1D && f[^3] == 0x56 && f[^2] == 0x42, "taglio parziale in fondo");
            Uguale(6, f.Count(b => b == 0x07), "2 beep");
            var (ip, porta) = ConfigStampe.Separa("10.1.1.1:9200");
            Ok(ip == "10.1.1.1" && porta == 9200, "IP:porta separati");
        }
    }
}
