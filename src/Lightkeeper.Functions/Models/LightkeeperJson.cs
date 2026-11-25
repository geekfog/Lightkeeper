using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightkeeper.Functions.Models;

/// <summary>JSON settings shared by inbound service-hook parsing and outbound card serialization.</summary>
public static class LightkeeperJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
