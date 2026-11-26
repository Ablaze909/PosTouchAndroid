using PosTouch.Core.Modelli;

namespace PosTouch.App.Controlli
{
    /// <summary>
    /// Un modello di riga per ogni tipo (prestazioni): prima ogni riga dello scontrino conteneva 12 scritte
    /// e 2 barrette di tutti i tipi, nascoste o visibili con i binding; ora ne disegna solo 3-4.
    /// Il tipo di una riga non cambia mai, quindi la scelta del modello è sicura.
    /// </summary>
    public class SelettoreRiga : DataTemplateSelector
    {
        public DataTemplate Prodotto { get; set; }
        public DataTemplate Sconto { get; set; }
        public DataTemplate Subtotale { get; set; }
        public DataTemplate Separatore { get; set; }
        public DataTemplate Variante { get; set; }
        public DataTemplate Gusto { get; set; }
        public DataTemplate Portata { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => (item as RigaScontrino)?.Tipo switch
        {
            TipoRiga.Sconto => Sconto,
            TipoRiga.Subtotale => Subtotale,
            TipoRiga.Separatore => Separatore,
            TipoRiga.Variante => Variante,
            TipoRiga.Gusto => Gusto,
            TipoRiga.Portata => Portata,
            _ => Prodotto
        };
    }
}
