using System.Text.Json.Serialization;
using DbRosetta.Core; // Asegúrate de que esto apunte al namespace de tu clase MigrationRequest

namespace DbRosetta.Native;

// Esta clase le indica al generador de código fuente qué tipos necesita preparar.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(MigrationRequest))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}