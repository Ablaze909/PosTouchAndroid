using PosTouch.App.Dialoghi;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using PosTouch.Core.Stampa;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Flussi dei turni con la gestione operatori accesa: PIN all'apertura, turno di un altro rimasto aperto,
    /// chiusura con riepilogo e conteggio contanti. Con la gestione spenta non fanno nulla.
    /// </summary>
    public static class Turni
    {
        private static ServizioOperatori Op => ContestoCassa.Operatori;
        private static bool _inCorso;

        /// <summary>
        /// Prima di vendere: true se si può lavorare (gestione spenta, turno già aperto, o PIN corretto).
        /// Se c'è il turno di un altro ancora aperto non si lavora finché non viene chiuso.
        /// </summary>
        public static async Task<bool> AssicuraTurnoAsync()
        {
            if (Op == null || !Op.Abilitata || Op.PuoLavorare) return true;
            if (_inCorso) return false;
            _inCorso = true;
            try
            {
                while (true)
                {
                    string pin = await TastierinoPrezzo.ChiediCodice("👤 APERTURA TURNO\nInserisci il tuo PIN", nascondi: true);
                    if (pin == null) return false;
                    var (esito, op, altrui) = Op.Accedi(pin);
                    switch (esito)
                    {
                        case EsitoAccesso.Ok:
                            return true;
                        case EsitoAccesso.PinErrato:
                            await MessaggioBox.Errore("PIN ERRATO", "Nessun operatore attivo con questo PIN.");
                            continue;
                        case EsitoAccesso.TurnoAltrui:
                            if (!await MessaggioBox.Domanda("TURNO ANCORA APERTO",
                                    $"Il turno di {altrui.OperatoreNome} (dalle {altrui.Apertura:HH:mm} del {altrui.Apertura:dd/MM}) non è stato chiuso.\n\n" +
                                    $"Per lavorare come {op.Nome} va chiuso prima.\nLo può chiudere {altrui.OperatoreNome} o un amministratore.\n\nChiuderlo adesso?"))
                                return false;
                            // chi chiude: l'operatore del PIN appena inserito se è lui o è amministratore, altrimenti si chiede il PIN
                            var chi = op.Id == altrui.OperatoreId || op.Amministratore ? op : await ChiChiudeAsync(altrui);
                            if (chi == null) return false;
                            if (!await ChiudiAsync(altrui, chi)) return false;
                            var dopo = Op.Accedi(pin);   // stesso PIN: apre il nuovo turno
                            return dopo.esito == EsitoAccesso.Ok;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Apertura turno: {ex}");
                await MessaggioBox.Errore("TURNO", $"Impossibile aprire il turno:\n{ex.Message}");
                return false;
            }
            finally { _inCorso = false; }
        }

        private static async Task<Operatore> ChiChiudeAsync(Turno t)
        {
            string pin = await TastierinoPrezzo.ChiediCodice($"🔒 CHIUSURA TURNO DI {t.OperatoreNome}\nPIN di {t.OperatoreNome} o di un amministratore", nascondi: true);
            if (pin == null) return null;
            var chi = Op.Archivio.TrovaPerPin(pin);
            if (chi != null && (chi.Id == t.OperatoreId || chi.Amministratore)) return chi;
            await MessaggioBox.Errore("NON AUTORIZZATO", $"Il turno di {t.OperatoreNome} lo può chiudere solo lui o un amministratore.");
            return null;
        }

        /// <summary>
        /// Chiusura turno: riepilogo, conteggio contanti (facoltativo), chiusura, stampa sulla stampante RIEPILOGHI se c'è.
        /// false = annullata dall'operatore (il turno resta aperto).
        /// </summary>
        public static async Task<bool> ChiudiAsync(Turno t, Operatore chi)
        {
            var anteprima = Op.Riepilogo(t);
            if (!await MessaggioBox.Conferma($"🔒 CHIUSURA TURNO — {t.OperatoreNome}", TestoRiepilogo(anteprima) + "\n\nChiudere il turno?"))
                return false;
            var contati = await TastierinoPrezzo.ChiediEuro("💶 CONTANTI INCASSATI NEL TURNO\n(senza fondo cassa — ANNULLA = non contare)");
            int? contatiCent = contati.HasValue ? (int)Math.Round(contati.Value * 100, MidpointRounding.AwayFromZero) : (int?)null;
            var r = Op.Chiudi(t, chi, contatiCent);
            await StampaAsync(r);
            await MessaggioBox.Info("✅ TURNO CHIUSO", TestoRiepilogo(r));
            return true;
        }

        public static string TestoRiepilogo(RiepilogoTurno r)
        {
            static string E(int c) => Importi.Euro(c);
            var t = r.Turno;
            string s = $"Operatore: {t.OperatoreNome}\nDalle {t.Apertura:dd/MM HH:mm}" + (t.Chiusura is DateTime ch ? $" alle {ch:HH:mm}" : "") + "\n\n" +
                       $"Scontrini: {r.Scontrini}   {E(r.TotaleScontriniCent)}\n" +
                       $"  contanti {E(r.ContantiCent)}   carta {E(r.CartaCent)}";
            if (r.Conti > 0) s += $"\nConti non fiscali: {r.Conti}   {E(r.TotaleContiCent)}";
            if (t.ContantiContatiCent is int c)
            {
                int d = r.DifferenzaCent ?? 0;
                s += $"\n\nContati: {E(c)}   " + (d == 0 ? "cassa in pari" : d > 0 ? $"ECCEDENZA {E(d)}" : $"AMMANCO {E(-d)}");
            }
            return s;
        }

        /// <summary>Stampa del riepilogo in coda sulla stampante RIEPILOGHI (se assegnata). Non blocca la chiusura.</summary>
        private static Task StampaAsync(RiepilogoTurno r)
        {
            try
            {
                var par = ContestoCassa.Impostazioni.Stampe?.Base?.Riepiloghi;
                if (par == null || !par.Assegnata) return Task.CompletedTask;
                var cfg = ContestoCassa.Impostazioni;
                var job = new JobStampa
                {
                    Tipo = TipoJob.Riepilogo, Descrizione = $"CHIUSURA TURNO {r.Turno.OperatoreNome}", Stampante = "RIEPILOGHI",
                    IP = par.IP, Porta = par.Porta, ControlloStato = par.ControlloStato,
                    Dati = Finitura.Applica(LayoutTurno.Crea(r, cfg.IntestazioneConto1), par, cfg.Sale.CicalinoComande)
                };
                ContestoCassa.Tavoli.AccodaStampe(new[] { job });
                ContestoCassa.Spooler.Sveglia();
            }
            catch (Exception ex) { LogManager.Error($"Stampa riepilogo turno: {ex.Message}"); }
            return Task.CompletedTask;
        }
    }
}
