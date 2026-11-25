using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightkeeper.Functions.Models;

/// <summary>JSON settings shared by inbound service-hook parsing and outbound card serialization.</summary>
public static class LightkeeperJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Cards go to Teams as JSON, never inside HTML, so emoji and symbols are written as UTF-8 rather than \u escapes that triple their size.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
