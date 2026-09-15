using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace V380Decoder.src
{
    [JsonSerializable(typeof(DispatchRequest))]
    [JsonSerializable(typeof(DispatchResult))]
    [JsonSerializable(typeof(ProblemDetails))]
    [JsonSerializable(typeof(StatusResponse))]
    [JsonSerializable(typeof(PtzPersistentState))]
    [JsonSerializable(typeof(PtzMoveRequest))]
    [JsonSerializable(typeof(PtzPresetRequest))]
    [JsonSerializable(typeof(PtzMoveResult))]
    [JsonSerializable(typeof(PtzStatusResponse))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
