using System;
using System.Security.Cryptography;

namespace PosTouch.Core.Configurazione
{
    /// <summary>
    /// Password dell'assistenza tecnica: vale ovunque si chieda la password amministratore o il PIN di un operatore autorizzato
    /// (anche gestione operatori e cambio password). Salvata solo come hash; per cambiarla serve quella attuale.
    /// </summary>
    public static class PasswordAssistenza
    {
        private const int Iterazioni = 10000;
        public const int LunghezzaMinima = 6;

        public static bool Impostata(Impostazioni i) => !string.IsNullOrEmpty(i?.PasswordAssistenzaHash) && !string.IsNullOrEmpty(i.PasswordAssistenzaSale);

        /// <summary>true se la password inserita è quella dell'assistenza (false se non è impostata).</summary>
        public static bool Verifica(Impostazioni i, string inserita)
        {
            if (!Impostata(i) || string.IsNullOrEmpty(inserita)) return false;
            try
            {
                byte[] atteso = Convert.FromBase64String(i.PasswordAssistenzaHash);
                if (atteso.Length < 16) return false;
                byte[] h = Rfc2898DeriveBytes.Pbkdf2(inserita, Convert.FromBase64String(i.PasswordAssistenzaSale), Iterazioni, HashAlgorithmName.SHA256, atteso.Length);
                return CryptographicOperations.FixedTimeEquals(h, atteso);
            }
            catch (Exception) { return false; }   // impostazioni rovinate: la password non vale (mai "aperto", mai crash)
        }

        /// <summary>Imposta (o con null/vuota toglie) la password sull'oggetto indicato. Solo cifre, almeno 6.</summary>
        public static void Imposta(Impostazioni i, string nuova)
        {
            if (string.IsNullOrEmpty(nuova)) { i.PasswordAssistenzaHash = ""; i.PasswordAssistenzaSale = ""; return; }
            if (nuova.Length < LunghezzaMinima || !SoloCifre(nuova))
                throw new ArgumentException($"La password assistenza deve avere almeno {LunghezzaMinima} cifre.");
            byte[] sale = RandomNumberGenerator.GetBytes(16);
            byte[] h = Rfc2898DeriveBytes.Pbkdf2(nuova, sale, Iterazioni, HashAlgorithmName.SHA256, 32);
            i.PasswordAssistenzaHash = Convert.ToBase64String(h);
            i.PasswordAssistenzaSale = Convert.ToBase64String(sale);
        }

        private static bool SoloCifre(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
