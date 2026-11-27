#if ANDROID
using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
#endif
using PosTouch.Core.Diagnostica;

namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Rete del locale (stampanti, registratore) sempre pronta:
    /// - Wi-Fi tenuto sveglio mentre l'app è aperta (niente risparmio energetico che ritarda o fa fallire la prima stampa);
    /// - l'app usa sempre la rete del locale (cavo LAN o Wi-Fi), anche se Android la considera "senza internet";
    /// - quando la rete torna, le comande in coda ripartono subito;
    /// - <see cref="Collegata"/> / <see cref="Cambiata"/> per l'avviso a schermo.
    /// </summary>
    public static class ReteLocale
    {
        /// <summary>true finché c'è una rete locale (cavo o Wi-Fi). Prima del primo controllo si considera collegata.</summary>
        public static bool Collegata { get; private set; } = true;

        /// <summary>Scatta sul thread dello schermo quando la rete si scollega o torna.</summary>
        public static event Action Cambiata;

#if ANDROID
        private static WifiManager.WifiLock _lucchetto;
        private static ConnectivityManager _cm;
        private static Ascolto _ascolto;
        private static readonly object _blocco = new();
        private static readonly Dictionary<string, (Network rete, bool cavo)> _reti = new();
        private static string _legata;

        public static void Avvia(Context ctx)
        {
            try
            {
                if (_lucchetto == null && ctx.GetSystemService(Context.WifiService) is WifiManager wm)
                {
                    var modo = Build.VERSION.SdkInt >= BuildVersionCodes.Q ? WifiMode.FullLowLatency : WifiMode.FullHighPerf;
#pragma warning disable CA1422
                    _lucchetto = wm.CreateWifiLock(modo, "PosTouch-rete-locale");
#pragma warning restore CA1422
                    _lucchetto.SetReferenceCounted(false);
                    _lucchetto.Acquire();
                }
            }
            catch (Exception ex) { LogManager.Warning($"Wi-Fi sempre attivo non disponibile: {ex.Message}"); }

            try
            {
                if (_ascolto != null) return;
                _cm = ctx.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
                if (_cm == null) return;
                // cavo o Wi-Fi, anche SENZA internet (la rete del locale spesso non ce l'ha)
                var richiesta = new NetworkRequest.Builder()
                    .AddTransportType(TransportType.Wifi)
                    .AddTransportType(TransportType.Ethernet)
                    .RemoveCapability(NetCapability.Internet)
                    .Build();
                _ascolto = new Ascolto();
                _cm.RegisterNetworkCallback(richiesta, _ascolto);
            }
            catch (Exception ex) { LogManager.Warning($"Controllo rete non disponibile: {ex.Message}"); }
        }

        /// <summary>Quando l'app torna in primo piano: il Wi-Fi resta sveglio (alcuni tablet lo rilasciano).</summary>
        public static void Riprendi()
        {
            try { if (_lucchetto != null && !_lucchetto.IsHeld) _lucchetto.Acquire(); } catch { }
        }

        private static void Aggiorna()
        {
            bool collegata;
            lock (_blocco)
            {
                // il cavo ha la precedenza sul Wi-Fi
                var scelta = _reti.Values.OrderByDescending(x => x.cavo).Select(x => x.rete).FirstOrDefault();
                string chiave = scelta?.ToString();
                if (chiave != _legata)
                {
                    try
                    {
                        _cm?.BindProcessToNetwork(scelta);   // null = torna alla rete predefinita di Android
                        _legata = chiave;
                        LogManager.Info(scelta == null ? "RETE: nessuna rete locale" : $"RETE: in uso la rete locale {(_reti[chiave].cavo ? "via cavo" : "Wi-Fi")}");
                    }
                    catch (Exception ex) { LogManager.Warning($"RETE: collegamento alla rete locale non riuscito: {ex.Message}"); }
                }
                collegata = scelta != null;
            }
            bool tornata = collegata && !Collegata;
            if (collegata != Collegata)
            {
                Collegata = collegata;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { Cambiata?.Invoke(); } catch (Exception ex) { LogManager.Error($"Avviso rete: {ex}"); }
                });
            }
            if (tornata) { try { ContestoCassa.Spooler?.ReteTornata(); } catch { } }
        }

        private sealed class Ascolto : ConnectivityManager.NetworkCallback
        {
            public override void OnAvailable(Network network)
            {
                base.OnAvailable(network);
                bool cavo = false;
                try { cavo = _cm?.GetNetworkCapabilities(network)?.HasTransport(TransportType.Ethernet) == true; } catch { }
                lock (_blocco) _reti[network.ToString()] = (network, cavo);
                Aggiorna();
            }

            public override void OnLost(Network network)
            {
                base.OnLost(network);
                lock (_blocco) _reti.Remove(network.ToString());
                Aggiorna();
            }
        }

        /// <summary>
        /// Una volta sola: chiede ad Android di non "addormentare" POS Touch per risparmiare batteria
        /// (finestra di sistema, risponde l'utente).
        /// </summary>
        public static void ChiediEsclusioneBatteria(Context ctx)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.M) return;
                if (Preferences.Default.Get("batteria_chiesta", false)) return;
                var pm = ctx.GetSystemService(Context.PowerService) as PowerManager;
                if (pm == null || pm.IsIgnoringBatteryOptimizations(ctx.PackageName)) return;
                Preferences.Default.Set("batteria_chiesta", true);
                var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse("package:" + ctx.PackageName));
                intent.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(intent);
            }
            catch (Exception ex) { LogManager.Warning($"Richiesta batteria non riuscita: {ex.Message}"); }
        }
#endif
    }
}
