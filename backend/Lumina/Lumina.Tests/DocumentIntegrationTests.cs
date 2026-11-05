using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using Lumina.DTOs.Document;
using Lumina.DTOs.Folder;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lumina.Tests;

public class DocumentIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public DocumentIntegrationTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Setup authentication
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer Test");
    }

    [Fact]
    public async Task Upload_ReturnsCreatedDocument()
    {
        // Setup a dummy file
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello"));
        content.Add(fileContent, "file", "test.txt");

        var response = await _client.PostAsync("/api/documents", content);
        
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            var contentString = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed: {response.StatusCode}, Content: {contentString}");
        }
        
        var doc = await response.Content.ReadFromJsonAsync<UploadDocumentResponse>();
        Assert.NotNull(doc);
        Assert.Equal("test.txt", doc.FileName);
    }

    [Fact]
    public async Task GetDocument_ReturnsDocument()
    {
        // Setup: Upload a document first
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello"));
        content.Add(fileContent, "file", "test.txt");
        var response = await _client.PostAsync("/api/documents", content);
        
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            var contentString = await response.Content.ReadAsStringAsync();
            throw new Exception($"Upload failed: {response.StatusCode}, Content: {contentString}");
        }
        
        var doc = await response.Content.ReadFromJsonAsync<UploadDocumentResponse>();
        
        // Act: Get it
        var getResponse = await _client.GetAsync($"/api/documents/{doc!.Id}");
        
        if (getResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            var contentString = await getResponse.Content.ReadAsStringAsync();
            throw new Exception($"Get failed: {getResponse.StatusCode}, Content: {contentString}");
        }
        
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task MoveDocument_UpdatesFolder()
    {
        // Setup:
        // 1. Create folder
        var folderResponse = await _client.PostAsJsonAsync("/api/folders", new { Name = "test-folder" });
        var folder = await folderResponse.Content.ReadFromJsonAsync<FolderResponse>();
        
        // 2. Upload document
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello"));
        content.Add(fileContent, "file", "test.txt");
        var response = await _client.PostAsync("/api/documents", content);
        var doc = await response.Content.ReadFromJsonAsync<UploadDocumentResponse>();
        
        // Act: Move it
        var moveResponse = await _client.PatchAsJsonAsync($"/api/documents/{doc!.Id}/folder", new { FolderId = folder!.Id });
        
        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NoContent, moveResponse.StatusCode);
    }

    [Fact]
    public async Task RenameDocument_CannotChangeExtension()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello"));
        content.Add(fileContent, "file", "test.txt");
        var uploadResponse = await _client.PostAsync("/api/documents", content);
        var doc = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponse>();

        var renameResponse = await _client.PatchAsJsonAsync(
            $"/api/documents/{doc!.Id}",
            new { FileName = "renamed.md" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, renameResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/documents/{doc.Id}");
        var stored = await getResponse.Content.ReadFromJsonAsync<DocumentDetailResponse>();
        Assert.Equal("test.txt", stored!.FileName);
    }

    [Fact]
    public async Task Upload_SourceCode_ExtractsAsText()
    {
        var doc = await UploadAsync("sample.cs", "Console.WriteLine(\"hello\");");

        var response = await _client.GetFromJsonAsync<DocumentDetailResponse>(
            $"/api/documents/{doc.Id}");

        Assert.Equal("Console.WriteLine(\"hello\");", response!.Content);
        Assert.Equal(1, response.ContentVersion);
    }

    [Fact]
    public async Task Upload_Docx_ExtractsParagraphText()
    {
        var doc = await UploadAsync("sample.docx", CreateDocx("Hello from DOCX"));

        var response = await _client.GetFromJsonAsync<DocumentDetailResponse>(
            $"/api/documents/{doc.Id}");

        Assert.Contains("Hello from DOCX", response!.Content);
    }

    [Fact]
    public async Task Upload_Pdf_ExtractsText()
    {
        var doc = await UploadAsync("sample.pdf", CreatePdf("Hello PDF"));

        var response = await _client.GetFromJsonAsync<DocumentDetailResponse>(
            $"/api/documents/{doc.Id}");

        Assert.Contains("Hello PDF", response!.Content);
    }

    [Fact]
    public async Task Upload_InvalidPdf_IsRejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not a pdf")), "file", "invalid.pdf");

        var response = await _client.PostAsync("/api/documents", form);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnsupportedExtension_IsRejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("echo unsafe")), "file", "run.exe");

        var response = await _client.PostAsync("/api/documents", form);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OversizedFile_IsRejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(
            new ByteArrayContent(new byte[2 * 1024 * 1024 + 1]),
            "file",
            "large.txt");

        var response = await _client.PostAsync("/api/documents", form);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditingContent_PreservesOriginalAndIncrementsVersion()
    {
        var doc = await UploadAsync("original.txt", "original content");
        var original = await _client.GetByteArrayAsync($"/api/documents/{doc.Id}/original");

        var update = await _client.PatchAsJsonAsync(
            $"/api/documents/{doc.Id}",
            new { Content = "edited content" });
        var updated = await update.Content.ReadFromJsonAsync<DocumentDetailResponse>();

        var downloaded = await _client.GetByteArrayAsync($"/api/documents/{doc.Id}/original");

        Assert.Equal(System.Net.HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(2, updated!.ContentVersion);
        Assert.Equal(Encoding.UTF8.GetByteCount("original content"), updated.FileSize);
        Assert.Equal(Encoding.UTF8.GetByteCount("edited content"), updated.ContentSize);
        Assert.Equal(original, downloaded);
    }

    [Fact]
    public async Task DeletingDocument_RemovesOriginalDownload()
    {
        var doc = await UploadAsync("delete-me.txt", "content");

        var delete = await _client.DeleteAsync($"/api/documents/{doc.Id}");
        var download = await _client.GetAsync($"/api/documents/{doc.Id}/original");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, download.StatusCode);
    }

    private async Task<UploadDocumentResponse> UploadAsync(string fileName, string content)
    {
        return await UploadAsync(fileName, Encoding.UTF8.GetBytes(content));
    }

    private async Task<UploadDocumentResponse> UploadAsync(string fileName, byte[] contentBytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(contentBytes), "file", fileName);
        var response = await _client.PostAsync("/api/documents", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadDocumentResponse>())!;
    }

    private static byte[] CreateDocx(string text)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(
                $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>");
        }

        return output.ToArray();
    }

    private static byte[] CreatePdf(string text)
    {
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
            "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n",
            $"4 0 obj\n<< /Length {Encoding.ASCII.GetByteCount($"BT /F1 12 Tf 72 720 Td ({text}) Tj ET")} >>\n" +
            $"stream\nBT /F1 12 Tf 72 720 Td ({text}) Tj ET\nendstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"
        };

        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write("%PDF-1.4\n");
        writer.Flush();

        var offsets = new List<long> { 0 };
        foreach (var value in objects)
        {
            offsets.Add(output.Position);
            writer.Write(value);
            writer.Flush();
        }

        var xrefOffset = output.Position;
        writer.Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            writer.Write($"{offset:0000000000} 00000 n \n");
        }
        writer.Write(
            $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        writer.Flush();
        return output.ToArray();
    }
}
