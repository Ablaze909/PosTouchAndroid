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
        public static string DueColonne(string sinistra, string destra, int larghezza = Colonne)
            => sinistra + new string(' ', System.Math.Max(larghezza - sinistra.Length - destra.Length, 1)) + destra;

        public byte[] Byte() => _b.ToArray();

        // ── testo ──

        private static string Sanitizza(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            return t.Replace("€", "euro").Replace("═", "=").Replace("─", "-")
                    .Replace("✅", "").Replace("⚠️", "").Replace("❌", "").Replace("🖨️", "");
        }

        private static readonly Dictionary<char, byte> _cp858 = new Dictionary<char, byte>
        {
            ['à'] = 0x85, ['è'] = 0x8A, ['é'] = 0x82, ['ì'] = 0x8D, ['ò'] = 0x95, ['ù'] = 0x97,
            ['À'] = 0xB7, ['È'] = 0xD4, ['É'] = 0x90, ['Ì'] = 0xDE, ['Ò'] = 0xE3, ['Ù'] = 0xEB,
            ['ç'] = 0x87, ['ñ'] = 0xA4, ['ü'] = 0x81, ['ö'] = 0x94, ['ä'] = 0x84, ['°'] = 0xF8
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
