using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — Lista scontrino: selezione, doppio tocco, lista espansa, frecce, totale.</summary>
    public partial class VenditaPage
    {
        // ══════════════ LISTA SCONTRINO: gesti ══════════════

        /// <summary>
        /// Tocco = seleziona/deseleziona (selezione multipla, come la ListView del PC).
        /// Doppio tocco sulla quantità = 🔢 QUANTITÀ; doppio tocco sull'importo = 💶 PREZZO UNITARIO.
        /// </summary>
        private async void OnRigaToccata(object sender, TappedEventArgs e)
        {
            if (sender is not View vista || vista.BindingContext is not RigaScontrino riga) return;

            bool doppio = ReferenceEquals(riga, _ultimaRigaToccata) && (DateTime.Now - _ultimoTocco).TotalMilliseconds < 400;
            _ultimaRigaToccata = riga;
            _ultimoTocco = DateTime.Now;

            if (!doppio)
            {
                riga.Selezionata = !riga.Selezionata;
                return;
            }

            // doppio tocco: annulla il cambio di selezione del primo tocco
            riga.Selezionata = !riga.Selezionata;
            _ultimaRigaToccata = null;
            if (riga.Tipo != TipoRiga.Prodotto) return;

            double x = e.GetPosition(vista)?.X ?? -1;
            double larghezza = vista.Width;
            if (x >= 0 && x <= 40) await ModificaQuantitaAsync(riga);
            else if (x >= larghezza - 80) await ModificaPrezzoAsync(riga);
        }

        private async Task ModificaQuantitaAsync(RigaScontrino riga)
        {
            if (Occupata) return;
            if (!Cfg.AbilitaModificaVendita)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            var q = await TastierinoPrezzo.ChiediQuantita($"🔢 QUANTITÀ\n{riga.Descrizione}", riga.Quantita);
            if (q == null) return;
            if (!Carrello.ImpostaQuantita(riga, q.Value))
                await MessaggioBox.Avviso("QUANTITÀ NON VALIDA", Carrello.UltimoErrore);
        }

        private async Task ModificaPrezzoAsync(RigaScontrino riga)
        {
            if (Occupata) return;
            if (!Cfg.AbilitaCambioPrezzo)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            if (!await Accesso.VerificaAsync(Permesso.ModificaPrezzi, "CAMBIO PREZZO")) return;
            var p = await TastierinoPrezzo.ChiediEuro($"💶 PREZZO UNITARIO\n{riga.Descrizione}", riga.PrezzoEuro);
            if (p == null) return;
            if (!Carrello.CambiaPrezzo(riga, (int)Math.Round(p.Value * 100, MidpointRounding.AwayFromZero)))
                await MessaggioBox.Avviso("PREZZO NON VALIDO", Carrello.UltimoErrore);
        }

        /// <summary>Tocco su 📋 SCONTRINO: lista espansa (nasconde la tastiera) / normale.</summary>
        private void OnIntestazioneScontrino(object sender, TappedEventArgs e) => ApplicaModalitaEspansa(!_scontrinoEspanso);
        private void OnChiudiLista(object sender, EventArgs e) => ApplicaModalitaEspansa(false);

        /// <summary>Lista espansa: cancella le righe selezionate (con un tocco sulla riga).</summary>
        private async void OnCancellaRigheEspanso(object sender, EventArgs e)
        {
            if (Occupata) return;
            var selezionate = Carrello.Righe.Where(r => r.Selezionata).ToList();
            if (selezionate.Count == 0)
            {
                await MessaggioBox.Info("CANCELLA RIGHE", "Tocca le righe da cancellare per selezionarle, poi premi 🗑️ CANCELLA RIGHE.");
                return;
            }
            if (!await ConfermaStornoAsync(selezionate)) return;
            Carrello.Rimuovi(selezionate);
            if (Carrello.UltimoErrore != null) _ = MessaggioBox.Avviso("ELIMINA", Carrello.UltimoErrore);
            if (Carrello.Vuoto) ApplicaModalitaEspansa(false);
        }

        private void ApplicaModalitaEspansa(bool espanso)
        {
            _scontrinoEspanso = espanso;
            TastieraNumerica.IsVisible = !espanso;
            SistemaRigaTasti();   // lista allungata: niente SCONTO e ANNULLA (nessun tocco per sbaglio)
            BarraListaEspansa.IsVisible = espanso;
        }

        private int _primaVisibile, _ultimaVisibile;

        private void OnListaScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            _primaVisibile = e.FirstVisibleItemIndex;
            _ultimaVisibile = e.LastVisibleItemIndex;
            AggiornaFrecce();
        }

        private RigaScontrino _daMostrare;
        private bool _scorrimentoProgrammato;

        /// <summary>
        /// Porta in vista una riga appena battuta. Lo scorrimento parte DOPO che la lista ha disegnato la riga nuova
        /// (subito non c'è ancora e restava fuori vista); battendo veloce si scorre una volta sola, all'ultima riga.
        /// </summary>
        private void MostraInFondo(RigaScontrino riga)
        {
            if (riga == null) return;
            _daMostrare = riga;
            if (_scorrimentoProgrammato) return;
            _scorrimentoProgrammato = true;
            Dispatcher.Dispatch(() =>
            {
                _scorrimentoProgrammato = false;
                var r = _daMostrare;
                _daMostrare = null;
                if (r == null || !Carrello.Righe.Contains(r)) return;
                // l'articolo accorpato su una riga già presente si mostra lì; se è l'ultima si va proprio in fondo
                if (ReferenceEquals(r, Carrello.Righe.LastOrDefault()))
                    ListaRighe.ScrollTo(Carrello.Righe.Count - 1, position: ScrollToPosition.End, animate: false);
                else
                    ListaRighe.ScrollTo(r, position: ScrollToPosition.MakeVisible, animate: false);
            });
        }

        /// <summary>Senza frecce: quando le righe non stanno tutte a video la barra nera dice quante sono.</summary>
        private void AggiornaFrecce()
        {
            // niente più "N righe": la barra nera dice solo SCONTRINO (si scorre col dito, barra di scorrimento visibile)
            if (TitoloScontrino.Text != "📋 SCONTRINO") TitoloScontrino.Text = "📋 SCONTRINO";
        }

        private void AggiornaTotale()
        {
            if (_combinazione != null && !_combinazione.Presente) EsciDaCombinazione();   // tolta con elimina righe
            EtichettaTotale.Text = $"€ {Carrello.TotaleEuro:F2}".Replace('.', ',');
            EtichettaPezzi.Text = $"{Carrello.Pezzi} pz";
            if (_sessione != null) AggiornaPosto();   // coperti, preconto e portata possono cambiare
            if (Carrello.Righe.Count == 0) { _primaVisibile = 0; _ultimaVisibile = 0; }
            // si sta battendo: si sveglia la rete verso RT e stampanti, così scontrino e comanda partono subito
            else PosTouch.Core.Stampa.RisveglioRete.Sveglia(Cfg);
            AggiornaFrecce();
            AggiornaBarraPortate();   // la casella accesa segue la portata di destinazione
            ColoraPortate();
        }

        private void SvuotaScontrino()
        {
            _eliminacodeInSospeso = 0;   // vendita chiusa o annullata: il prossimo documento prende un numero nuovo
            TornaAllaHome();   // scontrino/conto chiuso o annullato: si riparte dai preferiti
            if (_scontrinoEspanso) ApplicaModalitaEspansa(false);
            Carrello.Svuota();
            _tastierino.Azzera();
        }
    }
}
