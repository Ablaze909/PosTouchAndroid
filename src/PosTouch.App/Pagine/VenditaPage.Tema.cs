using PosTouch.App.Controlli;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// VenditaPage — aspetto (Impostazioni → Aspetto della vendita): CLASSICO (colori come sempre),
    /// SCURO (banda antracite, striscia della sala sotto) o CHIARO (banda bianca, striscia a sinistra).
    /// Nei temi nuovi le emoji diventano icone disegnate (Material Design Icons), uguali su ogni tablet,
    /// e in basso il colore resta solo a CASSA. Cambia solo la grafica: tasti e funzioni sono gli stessi.
    /// </summary>
    public partial class VenditaPage
    {
        private const int Classico = 0, Scuro = 1, Chiaro = 2;
        /// <summary>La vendita ha il suo tema (barre); il tema automatico delle pagine non la tocca.</summary>
        protected override bool TemaPagina => false;
        private int Tema => Math.Clamp(Cfg.TemaInterfaccia, 0, 2);
        private const string FontIcone = "MDI";

        private static string G(int codice) => char.ConvertFromUtf32(codice);
        // icone Material Design (codici della 7.4.47)
        private static readonly string IcoIndietro = G(0xF0141), IcoStrumenti = G(0xF05B7), IcoModifica = G(0xF03EB),
            IcoFunzioni = G(0xF0CF4), IcoCassa = G(0xF0116), IcoConto = G(0xF09EE), IcoPreconto = G(0xF042A),
            IcoNoStampa = G(0xF0E5D), IcoAvanza = G(0xF0211), IcoCassetto = G(0xF0120), IcoBarcode = G(0xF0072);

        /// <summary>Testo senza l'emoji iniziale ("💰 CASSA" → "CASSA").</summary>
        private static string SenzaEmoji(string t)
        {
            if (string.IsNullOrEmpty(t)) return t;
            int i = 0;
            while (i < t.Length && !char.IsLetterOrDigit(t[i]) && t[i] != '%' && t[i] != '€') i++;
            return t[i..].Trim();
        }

        private Dictionary<Button, (string testo, Color sfondo, Color colTesto)> _originaliBarre;

        /// <summary>Etichetta di un tasto delle barre in basso secondo il tema (anche per SistemaBarraTavolo).</summary>
        private string EtichettaBarra(Button b)
        {
            RicordaOriginali();
            var t = _originaliBarre.TryGetValue(b, out var o) ? o.testo : b.Text;
            return Tema == Classico ? t : SenzaEmoji(t);
        }

        private void RicordaOriginali()
        {
            if (_originaliBarre != null) return;
            _originaliBarre = BarraInferiore.Children.OfType<Button>().Concat(BarraTavolo.Children.OfType<Button>())
                .ToDictionary(b => b, b => (b.Text, b.BackgroundColor, b.TextColor));
        }

        // ══════════════ BANDA IN ALTO ══════════════

        /// <summary>Chiamata da AggiornaPosto: colori della banda, tasto di uscita e tasti a destra.</summary>
        private void ApplicaTemaBanda(Color sala)
        {
            int tema = Tema;
            StrisciaSala.IsVisible = tema != Classico;
            StrisciaLaterale.IsVisible = tema == Chiaro;
            BandaPosto.BackgroundColor = tema == Scuro ? Ui.C("#263238") : tema == Chiaro ? Colors.White : sala;
            StrisciaSala.Color = tema == Scuro ? sala : Ui.C("#CFD8DC");
            StrisciaSala.HeightRequest = tema == Scuro ? 4 : 1;
            StrisciaLaterale.Color = sala;
            var testo = tema == Chiaro ? Ui.C("#263238") : Colors.White;
            BandaTitolo.TextColor = testo;
            BandaDettaglio.TextColor = tema == Scuro ? Ui.C("#B0BEC5") : tema == Chiaro ? Ui.C("#607D8B") : Colors.White;

            // uscita: rossa piena nel classico; contorno sottile (scuro) o grigio chiaro (chiaro) nei temi nuovi
            if (tema == Classico)
            {
                BtnEsci.BackgroundColor = Ui.C("#D32F2F"); BtnEsci.TextColor = Colors.White;
                BtnEsci.BorderColor = Colors.White; BtnEsci.BorderWidth = 2;
            }
            else
            {
                BtnEsci.Text = "‹ " + SenzaEmoji(BtnEsci.Text).Replace("⬅", "").Trim();
                if (BtnEsci.Text == "‹ ") BtnEsci.Text = "‹ ESCI";
                BtnEsci.FontSize = 15;
                BtnEsci.BackgroundColor = tema == Scuro ? Colors.Transparent : Ui.C("#ECEFF1");
                BtnEsci.TextColor = testo;
                BtnEsci.BorderColor = tema == Scuro ? Ui.C("#90A4AE") : Colors.Transparent;
                BtnEsci.BorderWidth = tema == Scuro ? 1.5 : 0;
            }

            TastoBanda(BtnStrumenti, "🔧", IcoStrumenti, tema);
            TastoBanda(BtnModifica, "✏️", IcoModifica, tema);
            TastoBanda(BtnFunzioniCassa, "💰 FUNZIONI", IcoFunzioni, tema);
            BtnFunzioniCassa.WidthRequest = tema == Classico ? -1 : 44;
            BtnFunzioniCassa.Padding = tema == Classico ? new Thickness(10, 0) : new Thickness(0);
            // operatore: etichetta col nome
            BtnOperatore.BackgroundColor = tema == Scuro ? Ui.C("#37474F") : tema == Chiaro ? Colors.White : Ui.C("#4527A0");
            BtnOperatore.TextColor = testo;
            BtnOperatore.CornerRadius = tema == Classico ? 0 : 18;
            BtnOperatore.BorderColor = tema == Chiaro ? Ui.C("#CFD8DC") : Colors.Transparent;
            BtnOperatore.BorderWidth = tema == Chiaro ? 1 : 0;
            BtnOperatore.HeightRequest = tema == Classico ? 44 : 36;
        }

        private static void TastoBanda(Button b, string emoji, string icona, int tema)
        {
            if (tema == Classico)
            {
                b.FontFamily = null; b.Text = emoji; b.FontSize = emoji.Length > 3 ? 13 : 18;
                b.BackgroundColor = Ui.C("#40000000"); b.TextColor = Colors.White; b.CornerRadius = 0;
                return;
            }
            b.FontFamily = FontIcone; b.Text = icona; b.FontSize = 24; b.CornerRadius = 8;
            b.BackgroundColor = tema == Scuro ? Ui.C("#37474F") : Colors.Transparent;
            b.TextColor = tema == Scuro ? Colors.White : Ui.C("#455A64");
        }

        /// <summary>Nome dell'operatore sul tasto: con l'emoji nel classico, solo il nome nei temi nuovi.</summary>
        private string TestoOperatore(string nome) => Tema == Classico ? $"👤 {nome}" : nome;

        // ══════════════ BARRE IN BASSO (banco e tavolo) ══════════════

        /// <summary>Colori e icone delle barre in basso; i testi e le misure li sistema SistemaBarraTavolo / l'adattamento.</summary>
        private int _temaBarre = Classico;   // all'avvio i tasti sono già quelli del classico

        private void ApplicaTemaBarre()
        {
            RicordaOriginali();
            int tema = Tema;
            if (tema == _temaBarre) return;   // si ridisegna solo quando cambia l'aspetto
            _temaBarre = tema;
            var sfondoBarra = tema == Scuro ? Ui.C("#ECEFF1") : tema == Chiaro ? Ui.C("#F5F7F8") : Colors.Transparent;
            BarraInferiore.BackgroundColor = BarraTavolo.BackgroundColor = sfondoBarra;
            foreach (var (b, o) in _originaliBarre)
            {
                if (tema == Classico)
                {
                    b.ImageSource = null; b.BackgroundColor = o.sfondo; b.TextColor = o.colTesto;
                    b.CornerRadius = 0; b.BorderWidth = 0; b.Text = o.testo;
                    continue;
                }
                bool cassa = o.testo.Contains("CASSA") && !o.testo.Contains("CASSETTO");
                var (icona, colIcona) = IconaBarra(o.testo);
                Color sfondo, colTesto;
                if (cassa) { sfondo = Ui.C("#2E7D32"); colTesto = Colors.White; colIcona = Colors.White; }
                else if (tema == Scuro) { sfondo = Ui.C("#37474F"); colTesto = Colors.White; colIcona = Colors.White; }
                else { sfondo = Colors.White; colTesto = Ui.C("#263238"); }
                b.BackgroundColor = sfondo; b.TextColor = colTesto; b.CornerRadius = 8;
                b.BorderColor = tema == Chiaro && !cassa ? Ui.C("#CFD8DC") : Colors.Transparent;
                b.BorderWidth = tema == Chiaro && !cassa ? 1 : 0;
                b.ImageSource = new FontImageSource { FontFamily = FontIcone, Glyph = icona, Color = colIcona, Size = 20 };
                b.ContentLayout = new Button.ButtonContentLayout(cassa ? Button.ButtonContentLayout.ImagePosition.Left : Button.ButtonContentLayout.ImagePosition.Top, cassa ? 8 : 1);
                b.Text = SenzaEmoji(o.testo);
            }
        }

        /// <summary>Icona e colore (tema chiaro) di ogni tasto in basso, riconosciuto dalla sua scritta.</summary>
        private static (string icona, Color colore) IconaBarra(string testo)
        {
            if (testo.Contains("CASSETTO")) return (IcoCassetto, Ui.C("#EF6C00"));
            if (testo.Contains("CASSA")) return (IcoCassa, Colors.White);
            if (testo.Contains("PRECONTO")) return (IcoPreconto, Ui.C("#F9A825"));
            if (testo.Contains("CONTO")) return (IcoConto, Ui.C("#6A1B9A"));
            if (testo.Contains("NO STAMPA")) return (IcoNoStampa, Ui.C("#E65100"));
            if (testo.Contains("AVANZA")) return (IcoAvanza, Ui.C("#1565C0"));
            if (testo.Contains("BARCODE")) return (IcoBarcode, Ui.C("#5D4037"));
            return (IcoConto, Ui.C("#455A64"));
        }
    }
}
