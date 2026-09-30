using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radar.Scanner;

public static class RadarJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
