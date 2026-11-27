using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;

namespace PosTouch.Core.Stampa
{
    /// <summary>
    /// "Sveglia" la rete verso registratore e stampanti mentre si batte, PRIMA di stampare: un piccolo pacchetto
    /// (UDP, porta 9 "discard": nessun apparecchio lo considera) fa ritrovare subito l'indirizzo in rete e tiene
    /// sveglio il Wi-Fi. Così scontrino e comanda partono senza il ritardo del primo collegamento.
    /// Al massimo una volta ogni 15 secondi per apparecchio; non blocca mai la cassa.
    /// </summary>
    public static class RisveglioRete
    {
        private static readonly TimeSpan Intervallo = TimeSpan.FromSeconds(15);
        private static readonly ConcurrentDictionary<string, DateTime> _ultimo = new();
        private static readonly byte[] Pacchetto = { 0 };

        public static void Sveglia(Impostazioni cfg)
        {
            if (cfg == null) return;
            var ora = DateTime.UtcNow;
            var daSvegliare = Indirizzi(cfg).Where(ip => !_ultimo.TryGetValue(ip, out var t) || ora - t >= Intervallo).ToList();
            if (daSvegliare.Count == 0) return;
            foreach (var ip in daSvegliare) _ultimo[ip] = ora;
            _ = Task.Run(() =>
            {
                foreach (var ip in daSvegliare)
                {
                    try
                    {
                        if (!IPAddress.TryParse(ip, out var indirizzo)) continue;
                        using var udp = new UdpClient(indirizzo.AddressFamily);
                        udp.Send(Pacchetto, Pacchetto.Length, new IPEndPoint(indirizzo, 9));
                    }
                    catch (Exception ex) { LogManager.Debug($"Risveglio {ip}: {ex.Message}"); }
                }
            });
        }

        /// <summary>Registratore, termica, stampanti delle comande e dei preconti (tutte le sale).</summary>
        public static IEnumerable<string> Indirizzi(Impostazioni cfg)
        {
            var l = new HashSet<string>(StringComparer.Ordinal);
            void Aggiungi(string ip) { if (!string.IsNullOrWhiteSpace(ip)) l.Add(ip.Trim()); }
            Aggiungi(cfg.IP_RT);
            Aggiungi(cfg.IP_Termica);
            var st = cfg.Stampe;
            if (st != null)
            {
                foreach (var ip in st.IndirizziIP ?? new List<string>()) Aggiungi(ip);
                foreach (var sala in new[] { st.Base }.Concat(st.PerSala?.Values ?? Enumerable.Empty<StampeSala>()))
                {
                    if (sala == null) continue;
                    Aggiungi(sala.Preconto?.IP);
                    Aggiungi(sala.Riepiloghi?.IP);
                    foreach (var u in sala.Uscite?.Values ?? Enumerable.Empty<ParametriStampa>()) Aggiungi(u?.IP);
                }
            }
            return l;
        }
    }
}
