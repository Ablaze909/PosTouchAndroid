using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using PosTouch.Core.Fiscale;

namespace PosTouch.Tests
{
    /// <summary>
    /// Registratore finto per i test: risponde "OK" a tutto, registra i comandi ricevuti e
    /// permette di far fallire un comando o di simulare la conferma mancante e lo stato dello scontrino.
    /// Costruito con DispatchProxy: non va aggiornato quando IFiscalConnector cambia.
    /// </summary>
    public class RegistratoreFinto : DispatchProxy
    {
        public List<string> Comandi { get; } = new List<string>();
        /// <summary>Nomi dei metodi che rispondono false (es. "VendiRepartoAsync").</summary>
        public HashSet<string> Rifiuta { get; } = new HashSet<string>();
        /// <summary>Comandi che lanciano un'eccezione (rete che cade a metà).</summary>
        public HashSet<string> Lancia { get; } = new HashSet<string>();
        public bool FooterConfermato = true;
        public bool? StatoDopoChiusura = false;

        public static (IFiscalConnector rt, RegistratoreFinto finto) Crea()
        {
            var rt = Create<IFiscalConnector, RegistratoreFinto>();
            return (rt, (RegistratoreFinto)(object)rt);
        }

        protected override object Invoke(MethodInfo metodo, object[] args)
        {
            string nome = metodo.Name;
            Comandi.Add(nome + (args == null || args.Length == 0 ? "" : "(" + string.Join(",", args.Select(a => a?.ToString())) + ")"));
            var tipo = metodo.ReturnType;
            if (Lancia.Contains(nome)) throw new System.IO.IOException($"rete caduta durante {nome}");
            if (nome == "LeggiTxFooterAsync")
                return Task.FromResult(new RisultatoChiusuraRT { Confermato = FooterConfermato, NumeroDocumento = FooterConfermato ? 42 : 0, NumeroAzzeramento = 7 });
            if (nome == "StatoScontrinoDopoChiusuraAsync") return Task.FromResult(StatoDopoChiusura);
            if (tipo == typeof(Task<bool>)) return Task.FromResult(!Rifiuta.Contains(nome));
            if (tipo == typeof(Task<bool?>)) return Task.FromResult<bool?>(null);
            if (tipo == typeof(Task<string>)) return Task.FromResult("");
            if (tipo == typeof(Task)) return Task.CompletedTask;
            if (tipo == typeof(string)) return "";
            if (tipo == typeof(bool)) return true;
            if (tipo == typeof(void)) return null;
            if (tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var interno = tipo.GetGenericArguments()[0];
                object predefinito = interno.IsValueType ? Activator.CreateInstance(interno) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult)).MakeGenericMethod(interno).Invoke(null, new[] { predefinito });
            }
            return tipo.IsValueType ? Activator.CreateInstance(tipo) : null;
        }
    }
}
