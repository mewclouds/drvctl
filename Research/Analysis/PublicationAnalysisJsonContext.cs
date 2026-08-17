using System.Text.Json.Serialization;

namespace DrvCtl.Analysis;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PublicationAnalysisReport))]
internal sealed partial class PublicationAnalysisJsonContext : JsonSerializerContext;
