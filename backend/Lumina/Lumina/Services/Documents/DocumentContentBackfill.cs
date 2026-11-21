using Lumina.Data;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Documents;

public static class DocumentContentBackfill
{
    public static async Task RunAsync(
        LuminaDbContext db,
        IDocumentContentConverter converter,
        CancellationToken cancellationToken = default)
    {
        var documents = await db.Documents
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var document in documents)
        {
            if (string.IsNullOrEmpty(document.ContentJson))
            {
                if (DocumentFormatDescriptor.UsesCanonicalSourceText(document.FileName, document.ContentJson))
                {
                    continue;
                }

                document.ContentJson = converter.CreateFromPlainText(document.Content);
                changed = true;
            }

            var normalized = converter.Normalize(document.ContentJson);
            var content = converter.ExtractPlainText(normalized);
            if (!string.Equals(document.ContentJson, normalized, StringComparison.Ordinal)
                || !string.Equals(document.Content, content, StringComparison.Ordinal))
            {
                document.ContentJson = normalized;
                document.Content = content;
                document.ContentSize = System.Text.Encoding.UTF8.GetByteCount(document.Content);
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
