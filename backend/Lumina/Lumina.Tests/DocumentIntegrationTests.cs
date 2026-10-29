using System.Net.Http.Json;
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
}
