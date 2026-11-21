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
        if (DocumentFormatDescriptor.UsesCanonicalSourceText(document.FileName, document.ContentJson))
            DocumentFormatDescriptor.ValidateSourceTextSyntax(document.FileName, content);
        if (string.Equals(document.Content, content, StringComparison.Ordinal))
        {
            return false;
        }

        document.Content = content;
        if (!DocumentFormatDescriptor.UsesCanonicalSourceText(document.FileName, document.ContentJson))
        {
            // Legacy rich-text edits submitted as plain text are still converted
            // to paragraphs. Source formats keep Content as their canonical text.
            document.ContentJson = contentConverter.CreateFromPlainText(content);
        }
        document.ContentSize = Encoding.UTF8.GetByteCount(content);
        document.ContentVersion++;
        return true;
    }
}
