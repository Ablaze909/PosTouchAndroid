using System.Net;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Pagine
{
    public partial class SaleConfigPage
    {
        // ══════════════ STAMPANTI ══════════════

        private View SchedaOpzioni()
        {
            var v = new VerticalStackLayout { Spacing = 10 };
            v.Add(Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    Spunta("Abilita stampa comande", _c.AbilitaComande, x => _c.AbilitaComande = x),
                    Spunta("Stampa riepilogo altri centri in fondo alla comanda", _c.StampaRiepilogoAltriCentri, x => _c.StampaRiepilogoAltriCentri = x),
                    Spunta("Accorpa righe uguali in comanda", _c.AccorpaStampaComanda, x => _c.AccorpaStampaComanda = x),
                    Spunta("Stampa gli storni anche con 🚫 NO STAMPA", _c.StampaStorniConNoStampa, x => _c.StampaStorniConNoStampa = x),
                }
            }));
            var stili = new Grid { ColumnSpacing = 10, RowSpacing = 8, ColumnDefinitions = { new(new GridLength(240)), new(GridLength.Star) } };
            for (int i = 0; i < 4; i++) stili.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            stili.Add(new Label { Text = "Stile articoli:", FontSize = 16, VerticalOptions = LayoutOptions.Center }, 0, 0);
            stili.Add(InBox(Scelta("Stile articoli in comanda", Stili, Math.Clamp(_c.StileComandaProdotto - 1, 0, 11), i => _c.StileComandaProdotto = i + 1)), 1, 0);
            stili.Add(new Label { Text = "Stile separatori portata:", FontSize = 16, VerticalOptions = LayoutOptions.Center }, 0, 1);
            stili.Add(InBox(Scelta("Stile separatori", Stili, Math.Clamp(_c.StileComandaSeparatore - 1, 0, 11), i => _c.StileComandaSeparatore = i + 1)), 1, 1);
            stili.Add(new Label { Text = "Cicalino comande:", FontSize = 16, VerticalOptions = LayoutOptions.Center }, 0, 2);
            stili.Add(InBox(Scelta("Cicalino", Cicalini, Math.Clamp(_c.CicalinoComande, 0, 4), i => _c.CicalinoComande = i)), 1, 2);
            v.Add(Card(stili));
            v.Add(Card(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    Spunta("Abilita portate (📑 FUNZIONI e ⏩ AVANZA al tavolo)", _c.AbilitaPortate, x => _c.AbilitaPortate = x),
                    Spunta("Abilita 🔀 SPOSTA tavolo", _c.AbilitaSpostaTavolo, x => _c.AbilitaSpostaTavolo = x),
                    Spunta("Abilita 🔗 UNISCI conti", _c.AbilitaUnisciConti, x => _c.AbilitaUnisciConti = x),
                    new Label { Text = "Separatori portate, in ordine (separati da virgola):", FontSize = 15 },
                    Campo(string.Join(", ", _c.SeparatoriPortate), t => _c.SeparatoriPortate = t.Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList(),
                          segnaposto: "SEGUE, SEGUE 2, SEGUE 3")
                }
            }));
            return v;
        }
    }
}
