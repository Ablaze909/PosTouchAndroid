using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Servizi;

namespace PosTouch.App.Controlli
{
    /// <summary>
    /// Tema delle pagine (sale e tavoli, avvio, Strumenti, Back office, Impostazioni, Stampe…).
    /// SCURO: fondo antracite, riquadri chiari → schede scure, scritte scure → chiare (solo dove il fondo diventa scuro,
    /// così le scritte su colori scelti dall'utente restano leggibili). CHIARO e CLASSICO: pagine come sono, con i tasti
    /// grigi, verdi e gli angoli dello stile comune. La vendita ha il suo tema (VenditaPage.Tema).
    /// </summary>
    public static class StilePagine
    {
        private static readonly Color Fondo = Color.FromArgb("#1B2429"), Scheda = Color.FromArgb("#263238"), Tasto = Color.FromArgb("#37474F"),
                                      Testo = Color.FromArgb("#ECEFF1"), Muto = Color.FromArgb("#90A4AE"), Bordo = Color.FromArgb("#37474F");

        private static readonly HashSet<string> SfondiChiari = new(StringComparer.OrdinalIgnoreCase) { "#FFFFFFFF", "#FFFAFAFA", "#FFF5F5F5", "#FFECEFF1", "#FFEEEEEE", "#FFF3F4F6", "#FFF5F7F8", "#FFE5E5E5", "#FFF0F0F0" };
        private static readonly HashSet<string> TestiScuri = new(StringComparer.OrdinalIgnoreCase) { "#FF37474F", "#FF263238", "#FF111111", "#FF000000", "#FF424242", "#FF455A64", "#FF212121", "#FF333333", "#FF555555" };
        private static readonly HashSet<string> TestiGrigi = new(StringComparer.OrdinalIgnoreCase) { "#FF607D8B", "#FF78909C", "#FF90A4AE", "#FF999999", "#FF757575", "#FF666666", "#FF9E9E9E", "#FFB0BEC5" };
        private static readonly HashSet<string> TastiGrigi = new(StringComparer.OrdinalIgnoreCase) { "#FF9E9E9E", "#FF78909C", "#FF757575" };
        private static readonly HashSet<string> Verdi = new(StringComparer.OrdinalIgnoreCase) { "#FF4CAF50", "#FF43A047" };

        public static bool Scuro => (ContestoCassa.Impostazioni?.TemaInterfaccia ?? 0) == 1;

        private static string Hex(Color c) => c == null ? "" : c.ToArgbHex(true).ToUpperInvariant();

        /// <summary>Tutta la pagina, dall'alto verso il basso (i fondi prima delle scritte che ci stanno sopra).</summary>
        public static void Applica(ContentPage pagina)
        {
            if (Scuro && SfondiChiari.Contains(Hex(pagina.BackgroundColor) is "" ? "#FFFFFFFF" : Hex(pagina.BackgroundColor)))
                pagina.BackgroundColor = Fondo;
            if (pagina.Content != null) Visita(pagina.Content);
        }

        private static void Visita(Element e)
        {
            Adatta(e);
            if (e is IVisualTreeElement v)
                foreach (var figlio in v.GetVisualChildren().OfType<Element>().ToList()) Visita(figlio);
        }

        /// <summary>Fondo effettivo sotto un elemento: il primo antenato con un colore (o la pagina).</summary>
        private static Color FondoSotto(Element e)
        {
            for (var p = e.Parent; p != null; p = p.Parent)
                if (p is VisualElement ve && ve.BackgroundColor != null && ve.BackgroundColor.Alpha > 0.05f) return ve.BackgroundColor;
            return null;
        }

        private static bool FondoScuro(Color c) => c != null && (c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114) < 0.4;

        /// <summary>Un elemento (anche aggiunto dopo, es. tavoli ridisegnati): vedi la descrizione della classe.</summary>
        public static void Adatta(Element e)
        {
            bool scuro = Scuro;
            switch (e)
            {
                case Button b:
                {
                    string bg = Hex(b.BackgroundColor);
                    if (TastiGrigi.Contains(bg)) { b.BackgroundColor = scuro ? Color.FromArgb("#455A64") : Color.FromArgb("#ECEFF1"); b.TextColor = scuro ? Testo : Color.FromArgb("#37474F"); }
                    else if (Verdi.Contains(bg)) b.BackgroundColor = StileFinestre.Verde;
                    else if (scuro && SfondiChiari.Contains(bg)) { b.BackgroundColor = Tasto; if (TestiScuri.Contains(Hex(b.TextColor))) b.TextColor = Testo; b.BorderColor = Bordo; }
                    break;
                }
                case Label l when scuro:
                {
                    if (SfondiChiari.Contains(Hex(l.BackgroundColor))) l.BackgroundColor = Scheda;
                    var sotto = l.BackgroundColor != null && l.BackgroundColor.Alpha > 0.05f ? l.BackgroundColor : FondoSotto(l);
                    if (!FondoScuro(sotto)) break;   // scritta su un colore chiaro scelto dall'utente: resta com'è
                    string tc = Hex(l.TextColor);
                    if (tc == "" || TestiScuri.Contains(tc)) l.TextColor = Testo;
                    else if (TestiGrigi.Contains(tc)) l.TextColor = Muto;
                    break;
                }
                case Entry en when scuro:
                    en.TextColor = Testo; en.PlaceholderColor = Muto;
                    if (SfondiChiari.Contains(Hex(en.BackgroundColor))) en.BackgroundColor = Colors.Transparent;
                    break;
                case Editor ed when scuro:
                    ed.TextColor = Testo;
                    if (SfondiChiari.Contains(Hex(ed.BackgroundColor))) ed.BackgroundColor = Colors.Transparent;
                    break;
                case CheckBox cb:
                    break;
                case Border bo when scuro:
                    if (SfondiChiari.Contains(Hex(bo.BackgroundColor))) bo.BackgroundColor = Scheda;
                    if (bo.Stroke is SolidColorBrush sb && SfondiChiari.Contains(Hex(sb.Color)) || bo.Stroke is SolidColorBrush sb2 && Hex(sb2.Color) is "#FFB0BEC5" or "#FFE0E0E0" or "#FFCFD8DC" or "#FFDDDDDD")
                        bo.Stroke = new SolidColorBrush(Bordo);
                    break;
                case BoxView bx when scuro:
                    if (SfondiChiari.Contains(Hex(bx.Color)) || Hex(bx.Color) is "#FFE5E5E5" or "#FFE0E0E0" or "#FFDDDDDD") bx.Color = Bordo;
                    break;
                case Layout la when scuro:
                    if (SfondiChiari.Contains(Hex(la.BackgroundColor))) la.BackgroundColor = Scheda;
                    break;
                case ScrollView sv when scuro:
                    if (SfondiChiari.Contains(Hex(sv.BackgroundColor))) sv.BackgroundColor = Colors.Transparent;
                    break;
            }
        }
    }
}
