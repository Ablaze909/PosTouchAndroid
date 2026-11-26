using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PosTouch.Core.Dati;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    /// <summary>Operatori e turni (operatori.db + vendite.db veri): PIN, permessi, un turno alla volta, riepilogo.</summary>
    public static class TestOperatori
    {
        private static bool Rifiuta(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }

        public static void Esegui()
        {
            string dir = Path.Combine(Path.GetTempPath(), "postouch_op_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var arch = new ArchivioOperatori(dir);
                var vendite = new ArchivioVendite(dir);
                var s = new ServizioOperatori(arch, vendite, () => true);
                vendite.OperatoreCorrente = () => s.Corrente is Operatore o ? (o.Id, o.Nome) : ((int, string)?)null;

                Ok(Rifiuta(() => arch.Salva(new Operatore { Nome = "MARIO" }, "1234")), "il primo operatore deve essere amministratore");
                var admin = new Operatore { Nome = "titolare", Amministratore = true };
                arch.Salva(admin, "1990");
                Ok(admin.Id > 0 && admin.Nome == "TITOLARE", "amministratore creato (nome in maiuscolo)");
                var mario = new Operatore { Nome = "MARIO", Permessi = new HashSet<Permesso> { Permesso.Sconti } };
                arch.Salva(mario, "1234");
                var luigi = new Operatore { Nome = "LUIGI" };
                Ok(Rifiuta(() => arch.Salva(luigi, "1234")), "PIN già usato rifiutato");
                Ok(Rifiuta(() => arch.Salva(luigi, "12")), "PIN troppo corto rifiutato");
                Ok(Rifiuta(() => arch.Salva(luigi, "12a4")), "PIN non numerico rifiutato");
                Ok(luigi.Id == 0, "operatore rifiutato: resta nuovo");
                arch.Salva(luigi, "5678");
                Ok(Rifiuta(() => arch.Salva(new Operatore { Id = admin.Id, Nome = "TITOLARE", Amministratore = false }, null)), "l'ultimo amministratore non perde il ruolo");
                Ok(Rifiuta(() => arch.Elimina(admin.Id)), "l'ultimo amministratore non si elimina");
                Uguale(3, arch.Operatori().Count, "tre operatori");

                Uguale(EsitoAccesso.PinErrato, s.Accedi("0000").esito, "PIN errato");
                Ok(!s.PuoLavorare, "senza accesso non si vende");
                var a1 = s.Accedi("1234");
                Ok(a1.esito == EsitoAccesso.Ok && s.Corrente?.Nome == "MARIO" && s.PuoLavorare, "Mario apre il turno");
                long turnoMario = s.TurnoCorrente.Id;
                Ok(s.Puo(Permesso.Sconti) && !s.Puo(Permesso.ModificaPrezzi), "permessi di Mario");
                Ok(s.Autorizza("1234", Permesso.ModificaPrezzi) == null, "Mario non autorizza il cambio prezzo");
                Ok(s.Autorizza("1990", Permesso.ModificaPrezzi)?.Nome == "TITOLARE", "il titolare autorizza col suo PIN");
                Ok(s.AutorizzaAmministratore("1234") == null && s.AutorizzaAmministratore("1990") != null, "solo l'amministratore entra negli operatori");

                vendite.Salva(new VenditaRegistrata { TotaleCentesimi = 500, ContantiCent = 500, Righe = new List<RigaScontrino>() });
                vendite.Salva(new VenditaRegistrata { TotaleCentesimi = 300, CartaCent = 300, MetodoPagamento = MetodiPagamento.Carta, Righe = new List<RigaScontrino>() });

                s.Sospendi();
                Ok(!s.PuoLavorare && s.TurnoAttivo()?.Stato == StatoTurno.Sospeso, "turno sospeso");
                var a2 = s.Accedi("5678");
                Ok(a2.esito == EsitoAccesso.TurnoAltrui && a2.altrui.OperatoreNome == "MARIO" && !s.PuoLavorare, "Luigi bloccato: turno di Mario ancora aperto");
                var a3 = s.Accedi("1234");
                Ok(a3.esito == EsitoAccesso.Ok && s.TurnoCorrente.Id == turnoMario, "Mario riprende lo stesso turno");

                var turno = s.TurnoCorrente;
                bool negato = false;
                try { s.Chiudi(turno, luigi, null); } catch (UnauthorizedAccessException) { negato = true; }
                Ok(negato, "Luigi non chiude il turno di Mario");
                Ok(Rifiuta(() => arch.Elimina(mario.Id)), "operatore con turno aperto non si elimina");
                var r = s.Chiudi(turno, mario, 450);
                Ok(r.Scontrini == 2 && r.TotaleScontriniCent == 800 && r.ContantiCent == 500 && r.CartaCent == 300, "riepilogo del turno di Mario");
                Uguale(-50, r.DifferenzaCent, "contati 4,50 su 5,00: ammanco 0,50");
                Ok(s.TurnoAttivo() == null && !s.PuoLavorare, "turno chiuso");

                var a4 = s.Accedi("5678");
                Ok(a4.esito == EsitoAccesso.Ok && s.Corrente.Nome == "LUIGI", "dopo la chiusura Luigi apre il suo turno");
                var rl = s.Riepilogo(s.TurnoCorrente);
                Uguale(0, rl.Scontrini, "le vendite di Mario non sono nel turno di Luigi");

                // disattivato: il PIN non entra più; riattivarlo chiede un PIN nuovo
                var m2 = arch.Operatori().First(o => o.Nome == "MARIO");
                m2.Attivo = false; arch.Salva(m2, null);
                Ok(arch.TrovaPerPin("1234") == null, "operatore disattivato non entra");
                m2.Attivo = true;
                Ok(Rifiuta(() => arch.Salva(m2, null)), "riattivato senza PIN nuovo: rifiutato");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
