using System;
using System.Collections.Generic;

namespace PosTouch.Tests
{
    /// <summary>Mini-framework di test (niente pacchetti esterni): ogni controllo stampa ok / FAIL.</summary>
    public static class Verifica
    {
        public static int Errori { get; private set; }
        public static int Totale { get; private set; }
        private static string _gruppo = "";

        public static void Gruppo(string nome) { _gruppo = nome; Console.WriteLine($"\n── {nome} ──"); }

        public static void Ok(bool condizione, string descrizione)
        {
            Totale++;
            if (condizione) Console.WriteLine($"  ok    {descrizione}");
            else { Errori++; Console.WriteLine($"  FAIL  [{_gruppo}] {descrizione}"); }
        }

        public static void Uguale<T>(T atteso, T reale, string descrizione) =>
            Ok(EqualityComparer<T>.Default.Equals(atteso, reale), $"{descrizione} (atteso {atteso}, ottenuto {reale})");

        /// <summary>Esegue un gruppo di test: un'eccezione conta come errore e non ferma gli altri gruppi.</summary>
        public static void Esegui(string nome, Action test)
        {
            Gruppo(nome);
            try { test(); }
            catch (Exception ex) { Errori++; Totale++; Console.WriteLine($"  FAIL  [{nome}] eccezione: {ex}"); }
        }
    }
}
