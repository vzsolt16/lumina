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
            .Where(d => d.ContentJson == string.Empty)
            .ToListAsync(cancellationToken);

        foreach (var document in documents)
        {
            document.ContentJson = converter.CreateFromPlainText(document.Content);
            document.Content = converter.ExtractPlainText(document.ContentJson);
            document.ContentSize = System.Text.Encoding.UTF8.GetByteCount(document.Content);
        }

        if (documents.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
