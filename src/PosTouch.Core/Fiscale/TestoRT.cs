using System.Text;

namespace PosTouch.Core.Fiscale
{
    /// <summary>
    /// Il protocollo 3i accetta solo caratteri ASCII 0x20-0x7E e il connettore scarta il resto:
    /// "CAFFÈ" verrebbe stampato "CAFF". Qui le lettere accentate diventano lettera + apostrofo,
    /// come si scrive comunemente sugli scontrini (CAFFE', PIU', CITTA').
    /// </summary>
    public static class TestoRT
    {
        public static string SenzaAccenti(string testo)
        {
            if (string.IsNullOrEmpty(testo)) return testo;
            var sb = new StringBuilder(testo.Length + 4);
            foreach (char c in testo)
            {
                switch (c)
                {
                    case 'à': case 'á': sb.Append("a'"); break;
                    case 'è': case 'é': sb.Append("e'"); break;
                    case 'ì': case 'í': sb.Append("i'"); break;
                    case 'ò': case 'ó': sb.Append("o'"); break;
                    case 'ù': case 'ú': sb.Append("u'"); break;
                    case 'À': case 'Á': sb.Append("A'"); break;
                    case 'È': case 'É': sb.Append("E'"); break;
                    case 'Ì': case 'Í': sb.Append("I'"); break;
                    case 'Ò': case 'Ó': sb.Append("O'"); break;
                    case 'Ù': case 'Ú': sb.Append("U'"); break;
                    case 'ç': sb.Append('c'); break;
                    case 'Ç': sb.Append('C'); break;
                    case 'ñ': sb.Append('n'); break;
                    case 'Ñ': sb.Append('N'); break;
                    case '€': sb.Append("EUR"); break;
                    case '½': sb.Append("1/2"); break;
                    case '⅓': sb.Append("1/3"); break;
                    case '¼': sb.Append("1/4"); break;
                    case '’': case '‘': sb.Append('\''); break;
                    case '“': case '”': sb.Append('\''); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
