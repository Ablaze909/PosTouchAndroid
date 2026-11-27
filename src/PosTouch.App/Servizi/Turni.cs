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
                    if (!Op.Archivio.CeAmministratore())
                    {
                        // operatori.db perso o rovinato: nessuno può aprire turni, si sblocca da Impostazioni con la password amministratore
                        await MessaggioBox.Errore("OPERATORI NON TROVATI", "La gestione operatori è accesa ma non c'è nessun amministratore.\n\nApri IMPOSTAZIONI → SICUREZZA (password amministratore) e spegni o ricrea la gestione operatori.");
                        return false;
                    }
                    string pin = await TastierinoPrezzo.ChiediCodice("👤 APERTURA TURNO\nInserisci il tuo PIN", nascondi: true);
                    if (pin == null) return false;
                    var (esito, op, altrui) = await Task.Run(() => Op.Accedi(pin));   // hash del PIN fuori dal thread grafico
                    switch (esito)
                    {
                        case EsitoAccesso.Ok:
                            await ChiediFondoAsync();
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
                            var dopo = await Task.Run(() => Op.Accedi(pin));   // stesso PIN: apre il nuovo turno
                            if (dopo.esito != EsitoAccesso.Ok) return false;
                            await ChiediFondoAsync();
                            return true;
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

        /// <summary>
        /// Fondo cassa all'apertura (opzione): proposto quello lasciato dal turno precedente. Chiesto anche su un turno
        /// ripreso se non era stato inserito (app chiusa prima). ANNULLA = si tiene il valore proposto.
        /// </summary>
        private static async Task ChiediFondoAsync()
        {
            if (!ContestoCassa.Impostazioni.ChiediFondoCassa || Op.TurnoCorrente == null || Op.TurnoCorrente.FondoInizialeCent != null) return;
            try
            {
                // opzione spenta: nessuna proposta, si digita quello che c'è (OK o ANNULLA senza cifre = 0)
                int proposto = ContestoCassa.Impostazioni.ProponiFondoPrecedente ? Op.FondoProposto() : 0;
                var v = ContestoCassa.Impostazioni.ProponiFondoPrecedente
                    ? await TastierinoPrezzo.ChiediEuro($"💶 FONDO CASSA — {Op.Corrente?.Nome}\n(rimasti dal turno prima: {Importi.Euro(proposto)})", proposto / 100m)
                    : await TastierinoPrezzo.ChiediEuro($"💶 FONDO CASSA — {Op.Corrente?.Nome}\nContanti nel cassetto adesso");
                int fondo = v.HasValue && v.Value >= 0 ? (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero) : proposto;
                Op.ImpostaFondo(fondo);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Fondo cassa: {ex}");
                await MessaggioBox.Errore("FONDO CASSA", $"Fondo non salvato:\n{ex.Message}\n\nIl riepilogo del turno lo conterà come 0.");
            }
        }

        /// <summary>💸 Prelievo / 💰 versamento di contanti durante il turno (opzione).</summary>
        public static async Task MovimentoAsync(TipoMovimento tipo)
        {
            string nome = tipo == TipoMovimento.Prelievo ? "PRELIEVO" : "VERSAMENTO";
            var v = await TastierinoPrezzo.ChiediEuro($"{(tipo == TipoMovimento.Prelievo ? "💸" : "💰")} {nome} — contanti {(tipo == TipoMovimento.Prelievo ? "tolti dal" : "messi nel")} cassetto");
            if (v == null || v <= 0) return;
            int cent = (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero);
            string causale = await TestoBox.Chiedi($"{nome} {Importi.Euro(cent)} — causale (es. BANCA, SPICCIOLI)", "", 30);
            if (causale == null) return;   // annullato: nessun movimento
            if (!await MessaggioBox.Conferma(nome, $"{nome} di {Importi.Euro(cent)}\n{causale}\n\nConfermi?")) return;
            Op.Movimento(tipo, cent, causale);
            await MessaggioBox.Successo(nome, $"{nome} di {Importi.Euro(cent)} registrato nel turno di {Op.Corrente?.Nome}.");
        }

        private static async Task<Operatore> ChiChiudeAsync(Turno t)
        {
            string pin = await TastierinoPrezzo.ChiediCodice($"🔒 CHIUSURA TURNO DI {t.OperatoreNome}\nPIN di {t.OperatoreNome} o di un amministratore", nascondi: true);
            if (pin == null) return null;
            if (await Task.Run(() => Accesso.EAssistenza(pin, $"chiusura turno di {t.OperatoreNome}"))) return Accesso.Assistenza;
            var chi = await Task.Run(() => Op.Archivio.TrovaPerPin(pin));
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
            var cfg = ContestoCassa.Impostazioni;
            // con fondo cassa o movimenti si conta tutto il cassetto; altrimenti solo gli incassi (come prima)
            bool tuttoIlCassetto = t.FondoInizialeCent != null || anteprima.Movimenti.Count > 0;
            bool obbligatorio = cfg.ConteggioObbligatorio;
            string annulla = obbligatorio ? "ANNULLA = il turno resta aperto" : "ANNULLA = non contare";
            var contati = await TastierinoPrezzo.ChiediEuro(tuttoIlCassetto
                ? $"💶 CONTANTI NEL CASSETTO (fondo compreso)\nAttesi {Importi.Euro(anteprima.ContantiAttesiCent)} — {annulla}"
                : $"💶 CONTANTI INCASSATI NEL TURNO\n(senza fondo cassa — {annulla})");
            int? contatiCent = contati.HasValue && contati.Value >= 0 ? (int)Math.Round(contati.Value * 100, MidpointRounding.AwayFromZero) : (int?)null;
            // OK premuto senza digitare dà 0: con contanti attesi si chiede conferma (altrimenti "ammanco" dell'intero incasso)
            if (contatiCent == 0 && anteprima.ContantiAttesiCent > 0
                && !await MessaggioBox.Conferma("CONTANTI A ZERO", $"Hai inserito € 0,00 ma ne sono attesi {Importi.Euro(anteprima.ContantiAttesiCent)}.\n\nConfermi che il cassetto è vuoto?"))
                return false;
            if (obbligatorio && contatiCent == null)
            {
                // conteggio obbligatorio: senza importo (anche 0) il turno non si chiude
                await MessaggioBox.Avviso("CONTEGGIO OBBLIGATORIO", "Per chiudere il turno inserisci i contanti contati (0 se il cassetto è vuoto).\n\nIl turno resta aperto.");
                return false;
            }
            int? lasciatoCent = null;
            if (cfg.ChiediFondoLasciato)
            {
                int proposto = t.FondoInizialeCent ?? 0;
                if (contatiCent is int cc && proposto > cc) proposto = cc;   // non si lascia più di quanto c'è
                var l = await TastierinoPrezzo.ChiediEuro($"💶 FONDO LASCIATO PER IL PROSSIMO TURNO\n(ANNULLA = {Importi.Euro(proposto)})", proposto / 100m);
                lasciatoCent = l.HasValue && l.Value >= 0 ? (int)Math.Round(l.Value * 100, MidpointRounding.AwayFromZero) : proposto;
                if (contatiCent is int c2 && lasciatoCent > c2)
                {
                    await MessaggioBox.Avviso("FONDO LASCIATO", $"Il fondo lasciato ({Importi.Euro(lasciatoCent.Value)}) è più dei contanti contati ({Importi.Euro(c2)}): uso {Importi.Euro(c2)}.");
                    lasciatoCent = c2;
                }
            }
            var r = Op.Chiudi(t, chi, contatiCent, lasciatoCent);
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
            if (t.FondoInizialeCent != null || r.Movimenti.Count > 0)
            {
                s += "\n";
                if (t.FondoInizialeCent is int fi) s += $"\nFondo iniziale: {E(fi)}";
                if (r.VersamentiCent > 0) s += $"\nVersamenti: + {E(r.VersamentiCent)}";
                if (r.PrelieviCent > 0) s += $"\nPrelievi: − {E(r.PrelieviCent)}";
                s += $"\nContanti attesi nel cassetto: {E(r.ContantiAttesiCent)}";
            }
            if (t.ContantiContatiCent is int c)
            {
                int d = r.DifferenzaCent ?? 0;
                s += $"\n\nContati: {E(c)}   " + (d == 0 ? "cassa in pari" : d > 0 ? $"ECCEDENZA {E(d)}" : $"AMMANCO {E(-d)}");
            }
            if (t.FondoLasciatoCent is int fl)
                s += $"\nFondo lasciato: {E(fl)}" + (r.DaVersareCent is int dv ? $"   da togliere: {E(dv)}" : "");
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
