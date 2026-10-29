using System.Net;
using System.Net.Http.Json;
using Lumina.DTOs.Folder;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lumina.Tests;

public class FolderIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public FolderIntegrationTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Setup authentication
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer Test");
    }

    [Fact]
    public async Task CreateFolder_ReturnsCreatedFolder()
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new { Name = "new-folder" });
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var folder = await response.Content.ReadFromJsonAsync<FolderResponse>();
        Assert.NotNull(folder);
        Assert.Equal("new-folder", folder.Name);
    }

    [Fact]
    public async Task RenameFolder_UpdatesName()
    {
        // Setup: Create folder
        var createResponse = await _client.PostAsJsonAsync("/api/folders", new { Name = "old-name" });
        var folder = await createResponse.Content.ReadFromJsonAsync<FolderResponse>();
        
        // Act: Rename
        var renameResponse = await _client.PatchAsJsonAsync($"/api/folders/{folder!.Id}", new { Name = "new-name" });
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        var renamedFolder = await renameResponse.Content.ReadFromJsonAsync<FolderResponse>();
        Assert.Equal("new-name", renamedFolder!.Name);
    }

    [Fact]
    public async Task DeleteFolder_ReturnsNoContent()
    {
        // Setup: Create folder
        var createResponse = await _client.PostAsJsonAsync("/api/folders", new { Name = "to-be-deleted" });
        var folder = await createResponse.Content.ReadFromJsonAsync<FolderResponse>();
        
        // Act: Delete
        var deleteResponse = await _client.DeleteAsync($"/api/folders/{folder!.Id}");
        
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
