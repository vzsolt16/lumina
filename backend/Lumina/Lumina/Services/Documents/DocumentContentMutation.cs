using System.Text;
using Lumina.Models;

namespace Lumina.Services.Documents;

internal static class DocumentContentMutation
{
    internal static bool Apply(Document document, string content)
    {
        DocumentValidation.ValidateEditableContent(content);
        if (string.Equals(document.Content, content, StringComparison.Ordinal))
        {
            return false;
        }

        document.Content = content;
        document.ContentSize = Encoding.UTF8.GetByteCount(content);
        document.ContentVersion++;
        return true;
    }
}
