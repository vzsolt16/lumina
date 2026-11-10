using System.Text;
using Lumina.Models;

namespace Lumina.Services.Documents;

internal static class DocumentContentMutation
{
    internal static bool Apply(
        Document document,
        string content,
        IDocumentContentConverter contentConverter)
    {
        DocumentValidation.ValidateEditableContent(content);
        if (string.Equals(document.Content, content, StringComparison.Ordinal))
        {
            return false;
        }

        document.Content = content;
        // Raw-text edits are a legacy compatibility path. They intentionally
        // rebuild unformatted paragraphs; structured editor operations preserve
        // untouched node attributes and marks in a later stage.
        document.ContentJson = contentConverter.CreateFromPlainText(content);
        document.ContentSize = Encoding.UTF8.GetByteCount(content);
        document.ContentVersion++;
        return true;
    }
}
