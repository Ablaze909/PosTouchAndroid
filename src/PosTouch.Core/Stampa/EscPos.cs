using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace PosTouch.Core.Stampa
{
    /// <summary>
    /// Costruisce i byte ESC/POS di una stampa (stessi comandi della StampanteTermica del PC).
    /// Il testo viene convertito in Code Page 858 (accentate italiane), senza dipendere dal sistema.
    /// </summary>
    public class EscPos
    {
        public const int Colonne = 48; // carta 80 mm

        private static readonly byte[] INIT = { 0x1B, 0x40 };
        private static readonly byte[] CENTER = { 0x1B, 0x61, 0x01 };
        private static readonly byte[] LEFT = { 0x1B, 0x61, 0x00 };
        private static readonly byte[] BOLD_ON = { 0x1B, 0x45, 0x01 };
        private static readonly byte[] BOLD_OFF = { 0x1B, 0x45, 0x00 };
        private static readonly byte[] NORMAL_SIZE = { 0x1B, 0x21, 0x00, 0x1D, 0x21, 0x00 };
        private static readonly byte[] DOUBLE_HEIGHT = { 0x1D, 0x21, 0x10 };
        private static readonly byte[] CUT_PAPER = { 0x1D, 0x56, 0x41, 0x00 };
        private static readonly byte[] DOUBLE_WIDTH = { 0x1D, 0x21, 0x01 };
        private static readonly byte[] DOUBLE_SIZE = { 0x1D, 0x21, 0x11 };
        private static readonly byte[] TRIPLE_SIZE = { 0x1D, 0x21, 0x22 };
        private static readonly byte[] TRIPLE_HEIGHT = { 0x1D, 0x21, 0x20 };
        private static readonly byte[] TRIPLE_WIDTH = { 0x1D, 0x21, 0x02 };
        private static readonly byte[] REVERSE_ON = { 0x1D, 0x42, 0x01 };
        private static readonly byte[] REVERSE_OFF = { 0x1D, 0x42, 0x00 };
        private static readonly byte[] BUZZER_BEL = { 0x07, 0x07, 0x07 };
        private static readonly byte[] BUZZER_EPSON = { 0x1B, 0x28, 0x41, 0x05, 0x00, 0x61, 0x64, 0x00, 0x01, 0x03 };
        private static readonly byte[] BUZZER_ESCB = { 0x1B, 0x42, 0x03, 0x02 };
        private static readonly byte[] BUZZER_DLE = { 0x10, 0x14, 0x01, 0x00, 0x01 };

        private readonly List<byte> _b = new List<byte>();

        public EscPos() { _b.AddRange(INIT); }

        public EscPos Testo(string testo) { _b.AddRange(Cp858(Sanitizza(testo) + "\n")); return this; }
        public EscPos Vuota() => Testo("");
        public EscPos Linea(char c = '=') => Testo(new string(c, Colonne));
        public EscPos Grassetto(bool on) { _b.AddRange(on ? BOLD_ON : BOLD_OFF); return this; }
        public EscPos DoppiaAltezza() { _b.AddRange(DOUBLE_HEIGHT); return this; }
        public EscPos Normale() { _b.AddRange(NORMAL_SIZE); return this; }
        public EscPos Centro() { _b.AddRange(CENTER); return this; }
        public EscPos Sinistra() { _b.AddRange(LEFT); return this; }
        public EscPos Taglio() { _b.AddRange(CUT_PAPER); return this; }
        public EscPos DoppiaDimensione() { _b.AddRange(DOUBLE_SIZE); return this; }
        public EscPos Inverso(bool on) { _b.AddRange(on ? REVERSE_ON : REVERSE_OFF); return this; }

        /// <summary>ApplyStyleAsync del PC: 1=normale 2=bold 3=2xAlt 4=2xAlt+bold 5=2xLarg 6=2xLarg+bold 7=2x2 8=2x2+bold 9=3x3 10=3x3+bold 11=3xAlt 12=3xLarg.</summary>
        public EscPos Stile(int id)
        {
            Normale(); Grassetto(false);
            switch (id)
            {
                case 2: Grassetto(true); break;
                case 3: _b.AddRange(DOUBLE_HEIGHT); break;
                case 4: _b.AddRange(DOUBLE_HEIGHT); Grassetto(true); break;
                case 5: _b.AddRange(DOUBLE_WIDTH); break;
                case 6: _b.AddRange(DOUBLE_WIDTH); Grassetto(true); break;
                case 7: _b.AddRange(DOUBLE_SIZE); break;
                case 8: _b.AddRange(DOUBLE_SIZE); Grassetto(true); break;
                case 9: _b.AddRange(TRIPLE_SIZE); break;
                case 10: _b.AddRange(TRIPLE_SIZE); Grassetto(true); break;
                case 11: _b.AddRange(TRIPLE_HEIGHT); break;
                case 12: _b.AddRange(TRIPLE_WIDTH); break;
            }
            return this;
        }

        /// <summary>ResetStyleAsync del PC.</summary>
        public EscPos ResetStile() { Normale(); return Grassetto(false); }

        /// <summary>Cicalino comande: 0=Off, 1=BEL, 2=Epson ESC(A, 3=ESC B, 4=DLE DC4.</summary>
        public EscPos Cicalino(int tipo)
        {
            var cmd = tipo switch { 1 => BUZZER_BEL, 2 => BUZZER_EPSON, 3 => BUZZER_ESCB, 4 => BUZZER_DLE, _ => null };
            if (cmd != null) _b.AddRange(cmd);
            return this;
        }

        /// <summary>ScriviCentrato del PC.</summary>
        public EscPos Centrato(string testo, bool grassetto, bool doppiaAltezza)
        {
            Centro();
            if (doppiaAltezza) DoppiaAltezza();
            if (grassetto) Grassetto(true);
            Testo(testo);
            if (grassetto) Grassetto(false);
            if (doppiaAltezza) Normale();
            return Sinistra();
        }

        /// <summary>Etichetta a sinistra, valore allineato a destra su <paramref name="larghezza"/> colonne.</summary>
        /// <remarks>
        /// Le lunghezze si misurano DOPO la pulizia ("€" diventa "euro"): prima la riga superava la carta di 3 caratteri
        /// e l'importo andava a capo (",00" sulla riga sotto). Se non ci sta, si accorcia l'etichetta, mai l'importo.
        /// </remarks>
        public static string DueColonne(string sinistra, string destra, int larghezza = Colonne)
        {
            sinistra = Sanitizza(sinistra);
            destra = Sanitizza(destra);
            int spazio = larghezza - destra.Length - 1;
            if (sinistra.Length > spazio) sinistra = spazio > 0 ? sinistra.Substring(0, spazio) : "";
            return sinistra + new string(' ', System.Math.Max(larghezza - sinistra.Length - destra.Length, 1)) + destra;
        }

        public byte[] Byte() => _b.ToArray();

        // ── testo ──

        private static readonly char[] Controllo = Enumerable.Range(0, 32).Where(i => i != '\n').Select(i => (char)i).Append((char)127).ToArray();

        private static string Sanitizza(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            t = t.Replace("€", "euro").Replace("⅓", "1/3").Replace("═", "=").Replace("─", "-")
                 .Replace("✅", "").Replace("⚠️", "").Replace("❌", "").Replace("🖨️", "")
                 // virgolette e trattini "tipografici" (da descrizioni importate): altrimenti escono come '?'
                 .Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"').Replace('–', '-').Replace('—', '-').Replace("…", "...");
            // caratteri di controllo (TAB, ESC, \r…) mandavano comandi casuali alla stampante; l'a capo voluto resta
            return t.IndexOfAny(Controllo) < 0 ? t : new string(t.Where(c => c == '\n' || !char.IsControl(c)).ToArray());
        }

        private static readonly Dictionary<char, byte> _cp858 = new Dictionary<char, byte>
        {
            ['à'] = 0x85, ['è'] = 0x8A, ['é'] = 0x82, ['ì'] = 0x8D, ['ò'] = 0x95, ['ù'] = 0x97,
            ['À'] = 0xB7, ['È'] = 0xD4, ['É'] = 0x90, ['Ì'] = 0xDE, ['Ò'] = 0xE3, ['Ù'] = 0xEB,
            ['ç'] = 0x87, ['ñ'] = 0xA4, ['ü'] = 0x81, ['ö'] = 0x94, ['ä'] = 0x84, ['°'] = 0xF8, ['½'] = 0xAB, ['¼'] = 0xAC
        };

        public static byte[] Cp858(string s)
        {
            var r = new List<byte>(s.Length);
            foreach (char c in s)
            {
                if (c < 0x80) r.Add((byte)c);
                else if (_cp858.TryGetValue(c, out byte b)) r.Add(b);
                else if (c == '’' || c == '‘') r.Add((byte)'\'');
                else r.Add((byte)'?');
            }
            return r.ToArray();
        }
    }
}
