using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Stampa;
using PosTouch.Core.Vendita;

namespace PosTouch.Core.Sale
{
    /// <summary>Risultato di un'uscita dal tavolo (avvisi da mostrare all'operatore, vuoto = tutto ok).</summary>
    public class EsitoTavolo
    {
        public List<string> Avvisi { get; } = new List<string>();
        public int ComandeAccodate { get; set; }
        public int StorniAccodati { get; set; }
    }

    /// <summary>Anteprima di ⏩ AVANZA (VAI portata) da confermare.</summary>
    public class AnteprimaAvanza
    {
        public bool Possibile { get; set; }
        public string Titolo { get; set; } = "";
        public string Messaggio { get; set; } = "";
        public int Portata { get; set; }
        public string NomeSeparatore { get; set; } = "";
        /// <summary>true = portata senza articoli da stampare: si avanza e basta.</summary>
        public bool SenzaStampa { get; set; }
    }
}
