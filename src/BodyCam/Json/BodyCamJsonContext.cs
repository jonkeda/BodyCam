using System.Text.Json;
using System.Text.Json.Serialization;

namespace BodyCam.Json;

/// <summary>
/// Source-generated JSON serialization context for BodyCam types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Services.Glasses.HeyCyan.Media.RecordedMediaSidecar))]
internal partial class BodyCamJsonContext : JsonSerializerContext
{
}
