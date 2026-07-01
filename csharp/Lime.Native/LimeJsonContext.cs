namespace Lime.Native;

[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = global::System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    GenerationMode = global::System.Text.Json.Serialization.JsonSourceGenerationMode.Metadata)]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(RpcRequest))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(RpcResponse))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(CandidatesRequest))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(CommitRequest))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(CandidatesResult))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(MessageBody))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(ErrorBody))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(UserData))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(InputLogSnapshot))]
internal sealed partial class LimeJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext
{
    public static LimeJsonContext Relaxed { get; } = new(new global::System.Text.Json.JsonSerializerOptions
    {
        Encoder = global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = global::System.Text.Json.JsonNamingPolicy.CamelCase,
    });
}