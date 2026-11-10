namespace Lumina.Services.Documents;

/// <summary>
/// Stable-ID rules used by the structured editor:
/// existing nodes keep their IDs when their content changes or they move;
/// splitting keeps the original ID on the first fragment and generates IDs for
/// additional fragments; merging keeps the first node's ID; copied nodes always
/// receive new IDs. IDs are never reused after a node is deleted.
/// </summary>
internal static class DocumentNodeIdPolicy
{
    internal const string AttributeName = "id";

    internal static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("node-", StringComparison.Ordinal)
        && value.Length == "node-".Length + 32;
}
