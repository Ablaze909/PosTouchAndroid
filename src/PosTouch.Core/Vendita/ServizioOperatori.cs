using System;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    public enum EsitoAccesso
    {
        /// <summary>Turno aperto (nuovo o ripreso): si può vendere.</summary>
        Ok,
        /// <summary>PIN che non corrisponde a nessun operatore attivo.</summary>
        PinErrato,
        /// <summary>C'è il turno di un altro operatore ancora aperto o sospeso: va chiuso prima.</summary>
        TurnoAltrui
    }

    /// <summary>
    /// Gestione operatori e turni (come il PC). Con la gestione spenta tutto è permesso e non c'è un operatore.
    /// Un solo turno alla volta sulla cassa: un altro operatore non può lavorare finché il turno precedente
    /// non viene chiuso (dal suo operatore o da un amministratore).
    /// </summary>
    public class ServizioOperatori
    {
        private readonly ArchivioOperatori _archivio;
        private readonly ArchivioVendite _vendite;
        private readonly Func<bool> _abilitata;
        private readonly object _lock = new object();

        public ServizioOperatori(ArchivioOperatori archivio, ArchivioVendite vendite, Func<bool> abilitata)
        {
            _archivio = archivio;
            _vendite = vendite;
            _abilitata = abilitata;
        }

        public bool Abilitata => _abilitata();
        public ArchivioOperatori Archivio => _archivio;

        /// <summary>Operatore che sta lavorando adesso (turno aperto in questa sessione), null se nessuno.</summary>
        public Operatore Corrente { get; private set; }
        public Turno TurnoCorrente { get; private set; }

        /// <summary>Si può vendere: operatori spenti, oppure turno aperto in questa sessione.</summary>
        public bool PuoLavorare => !Abilitata || (Corrente != null && TurnoCorrente?.Stato == StatoTurno.Aperto);

        /// <summary>Permesso dell'operatore corrente (sempre sì con la gestione spenta).</summary>
        public bool Puo(Permesso p) => !Abilitata || Corrente?.Puo(p) == true;

        /// <summary>Turno non chiuso rimasto sulla cassa (anche dopo un riavvio).</summary>
        public Turno TurnoAttivo() => _archivio.TurnoAttivo();

        /// <summary>
        /// Accesso con PIN (all'apertura del turno). Stesso operatore del turno rimasto aperto/sospeso: lo riprende.
        /// Nessun turno: lo apre. Turno di un altro: <see cref="EsitoAccesso.TurnoAltrui"/> (non si lavora).
        /// </summary>
        public (EsitoAccesso esito, Operatore op, Turno altrui) Accedi(string pin)
        {
            var op = _archivio.TrovaPerPin(pin);
            if (op == null)
            {
                LogManager.Warning("Accesso operatore: PIN errato");
                return (EsitoAccesso.PinErrato, null, null);
            }
            lock (_lock)
            {
                var attivo = _archivio.TurnoAttivo();
                if (attivo != null && attivo.OperatoreId != op.Id) return (EsitoAccesso.TurnoAltrui, op, attivo);
                if (attivo != null)
                {
                    if (attivo.Stato != StatoTurno.Aperto) _archivio.ImpostaStato(attivo.Id, StatoTurno.Aperto);
                    attivo.Stato = StatoTurno.Aperto;
                    TurnoCorrente = attivo;
                    LogManager.Info($"TURNO RIPRESO: {op.Nome}");
                }
                else TurnoCorrente = _archivio.ApriTurno(op);
                Corrente = op;
                return (EsitoAccesso.Ok, op, null);
            }
        }

        /// <summary>Sospende il turno corrente: per lavorare di nuovo serve il PIN.</summary>
        public void Sospendi()
        {
            lock (_lock)
            {
                if (TurnoCorrente == null) return;
                _archivio.ImpostaStato(TurnoCorrente.Id, StatoTurno.Sospeso);
                LogManager.Info($"TURNO SOSPESO: {Corrente?.Nome}");
                TurnoCorrente = null;
                Corrente = null;
            }
        }

        /// <summary>Riepilogo del turno (incassi fino ad adesso, senza chiuderlo), con fondo e movimenti di cassa.</summary>
        public RiepilogoTurno Riepilogo(Turno t)
        {
            var r = _vendite.Riepilogo(t);
            r.Movimenti = _archivio.Movimenti(t.Id);
            return r;
        }

        /// <summary>Fondo cassa del turno corrente (all'apertura).</summary>
        public void ImpostaFondo(int fondoCent)
        {
            lock (_lock)
            {
                if (TurnoCorrente == null) throw new InvalidOperationException("Nessun turno aperto.");
                _archivio.ImpostaFondoIniziale(TurnoCorrente.Id, fondoCent);
                TurnoCorrente.FondoInizialeCent = fondoCent;
            }
        }

        public int FondoProposto() => _archivio.FondoProposto();

        /// <summary>Prelievo o versamento sul turno corrente, registrato con il nome dell'operatore.</summary>
        public MovimentoCassa Movimento(TipoMovimento tipo, int importoCent, string causale)
        {
            lock (_lock)
            {
                if (TurnoCorrente == null || !PuoLavorare) throw new InvalidOperationException("Nessun turno aperto.");
                return _archivio.AggiungiMovimento(TurnoCorrente.Id, tipo, importoCent, causale, Corrente?.Nome);
            }
        }

        /// <summary>
        /// Chiude un turno (il corrente o quello rimasto aperto di un altro). Chi chiude deve essere il suo operatore
        /// o un amministratore. Restituisce il riepilogo con i contanti contati (null = non contati).
        /// </summary>
        public RiepilogoTurno Chiudi(Turno t, Operatore chi, int? contantiContatiCent, int? fondoLasciatoCent = null)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (chi == null || (chi.Id != t.OperatoreId && !chi.Amministratore))
                throw new UnauthorizedAccessException($"Il turno di {t.OperatoreNome} lo può chiudere solo lui o un amministratore.");
            lock (_lock)
            {
                var chiuso = _archivio.ChiudiTurno(t.Id, contantiContatiCent, fondoLasciatoCent) ?? t;
                if (TurnoCorrente?.Id == t.Id) { TurnoCorrente = null; Corrente = null; }
                var r = Riepilogo(chiuso);
                LogManager.Info($"Riepilogo turno {chiuso.OperatoreNome}: {r.Scontrini} scontrini €{r.TotaleScontriniCent / 100m:F2}, contanti €{r.ContantiCent / 100m:F2}, carta €{r.CartaCent / 100m:F2}" +
                                (r.DifferenzaCent is int d ? $", differenza contanti €{d / 100m:F2}" : "") + $" (chiuso da {chi.Nome})");
                return r;
            }
        }

        /// <summary>
        /// Autorizzazione al volo: chi inserisce il PIN si identifica (come il PC). null = PIN errato o senza il permesso.
        /// </summary>
        public Operatore Autorizza(string pin, Permesso p)
        {
            var op = _archivio.TrovaPerPin(pin);
            if (op == null || !op.Puo(p)) return null;
            if (Corrente?.Id != op.Id) LogManager.Info($"AUTORIZZAZIONE {p}: {op.Nome} per {Corrente?.Nome ?? "nessun operatore"}");
            return op;
        }

        /// <summary>Solo un amministratore (gestione operatori, attivare/spegnere la gestione).</summary>
        public Operatore AutorizzaAmministratore(string pin)
        {
            var op = _archivio.TrovaPerPin(pin);
            return op?.Amministratore == true ? op : null;
        }

        /// <summary>Dopo una modifica agli operatori: ricarica permessi e nome dell'operatore corrente.</summary>
        public void Aggiorna()
        {
            lock (_lock)
            {
                if (Corrente == null) return;
                var nuovo = _archivio.Operatori().Find(o => o.Id == Corrente.Id);
                if (nuovo == null || !nuovo.Attivo) { Sospendi(); return; }   // eliminato o disattivato: deve rientrare
                Corrente = nuovo;
            }
        }

        /// <summary>Gestione spenta: si chiude la sessione in memoria (i turni restano nello storico).</summary>
        public void Disattiva()
        {
            lock (_lock) { Corrente = null; TurnoCorrente = null; }
        }
    }
}
