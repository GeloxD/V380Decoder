using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace V380Decoder.src
{
    [JsonSerializable(typeof(DispatchRequest))]
    [JsonSerializable(typeof(DispatchResult))]
    [JsonSerializable(typeof(ProblemDetails))]
    [JsonSerializable(typeof(StatusResponse))]
    [JsonSerializable(typeof(PtzStatus))]
    [JsonSerializable(typeof(PtzPreset))]
    [JsonSerializable(typeof(PtzPreset[]))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
