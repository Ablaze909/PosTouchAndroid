using System.Text.Json.Serialization;

namespace PosTouch.Core.Configurazione
{
    /// <summary>
    /// Serializzazione JSON generata in compilazione: funziona anche con l'ottimizzazione
    /// (trimming) della build Android Release, che può rimuovere il codice usato via reflection.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Impostazioni))]
    [JsonSerializable(typeof(ConfigStampe))]
    internal partial class JsonContesto : JsonSerializerContext
    {
    }
}
