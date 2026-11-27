using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — operatore del turno (gestione operatori): tasto 👤, sospensione, chiusura turno, riepilogo.</summary>
    public partial class VenditaPage
    {
        private int? _operatoreMostrato;

        /// <summary>Tavolo aperto in vendita (le pagine degli operatori non chiudono turni a metà tavolo).</summary>
        public bool TavoloAperto => _sessione != null;

        /// <summary>
        /// Cambio di operatore: niente di quello che l'operatore precedente aveva attivato o autorizzato
        /// (modalità modifica, prezzo battuto con €) passa a quello nuovo.
        /// </summary>
        private void AzzeraStatoOperatore()
        {
            if (_modalitaModifica) ImpostaModalitaModifica(false);
            _tastierino.Azzera();
            _prezzoAutorizzato = false;
        }

        private void AggiornaOperatore()
        {
            var op = ContestoCassa.Operatori;
            BtnOperatore.IsVisible = op.Abilitata;
            int? id = op.Abilitata ? op.Corrente?.Id : null;
            if (id != _operatoreMostrato) { AzzeraStatoOperatore(); _operatoreMostrato = id; }
            if (!op.Abilitata) return;
            BtnOperatore.Text = TestoOperatore(op.Corrente?.Nome ?? "—");
            // turno chiuso altrove, operatore disattivato o gestione appena accesa: senza turno non si vende
            if (!op.PuoLavorare)
                Dispatcher.Dispatch(async () => await Navigazione.TornaAllInizio(this));
        }

        private async void OnOperatore(object sender, EventArgs e)
        {
            if (Occupata || CombinazioneAperta()) return;
            var op = ContestoCassa.Operatori;
            if (!op.Abilitata || op.TurnoCorrente == null) return;
            const string Riepilogo = "📊 RIEPILOGO TURNO", Prelievo = "💸 PRELIEVO", Versamento = "💰 VERSAMENTO",
                         Sospendi = "⏸ SOSPENDI TURNO", ChiudiTurno = "🔒 CHIUDI TURNO", Gestione = "👥 GESTIONE OPERATORI";
            var voci = new List<string> { Riepilogo };
            if (Cfg.AbilitaMovimentiCassa) { voci.Add(Prelievo); voci.Add(Versamento); }
            voci.Add(Sospendi); voci.Add(ChiudiTurno);
            if (op.Corrente?.Amministratore == true) voci.Add(Gestione);
            int k = await SceltaBox.Testi($"👤 {op.Corrente?.Nome} — turno dalle {op.TurnoCorrente.Apertura:HH:mm}", voci, 2);
            if (k < 0 || k >= voci.Count) return;
            try
            {
                switch (voci[k])
                {
                    case Riepilogo:
                        await MessaggioBox.Info("📊 RIEPILOGO TURNO", Turni.TestoRiepilogo(op.Riepilogo(op.TurnoCorrente)));
                        break;
                    case Prelievo:
                        await Turni.MovimentoAsync(TipoMovimento.Prelievo);
                        break;
                    case Versamento:
                        await Turni.MovimentoAsync(TipoMovimento.Versamento);
                        break;
                    case Sospendi:
                        if (!await PuoLasciareAsync("sospendere")) return;
                        AzzeraStatoOperatore();
                        op.Sospendi();
                        await Navigazione.TornaAllInizio(this);
                        break;
                    case ChiudiTurno:
                        if (!await PuoLasciareAsync("chiudere")) return;
                        AzzeraStatoOperatore();
                        if (await Turni.ChiudiAsync(op.TurnoCorrente, op.Corrente))
                            await Navigazione.TornaAllInizio(this);
                        break;
                    case Gestione:
                        var admin = await Accesso.VerificaAmministratoreAsync("GESTIONE OPERATORI");
                        if (admin != null) await Navigazione.Apri(this, () => new OperatoriPage(admin));
                        break;
                }
            }
            catch (Exception ex)
            {
                PosTouch.Core.Diagnostica.LogManager.Error($"Menu operatore: {ex}");
                await MessaggioBox.Errore("TURNO", ex.Message);
            }
        }

        /// <summary>Il turno si lascia solo a scontrino vuoto e fuori dal tavolo (niente righe di nessuno a metà).</summary>
        private async Task<bool> PuoLasciareAsync(string azione)
        {
            if (_sessione != null) { await MessaggioBox.Avviso("TAVOLO APERTO", $"Esci dal tavolo prima di {azione} il turno."); return false; }
            if (!Carrello.Vuoto) { await MessaggioBox.Avviso("SCONTRINO IN CORSO", $"Chiudi o svuota lo scontrino prima di {azione} il turno."); return false; }
            return true;
        }
    }
}
