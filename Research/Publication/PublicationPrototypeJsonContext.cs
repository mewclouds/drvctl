using System.Text.Json.Serialization;

namespace DrvCtl.Publication;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PublicationPrototypeResult))]
[JsonSerializable(typeof(WimPublicationResult))]
[JsonSerializable(typeof(SelfVerificationDiagnosticEntry))]
[JsonSerializable(typeof(SelfVerificationDiagnosticEntry[]))]
internal sealed partial class PublicationPrototypeJsonContext : JsonSerializerContext;

