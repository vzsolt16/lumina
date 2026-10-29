using System.Net;
using System.Net.Http.Json;
using Lumina.DTOs.Auth;
using Lumina.DTOs.Document;
using Lumina.DTOs.Folder;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lumina.Tests;

public class UserFlowIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public UserFlowIntegrationTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Register_Login_Upload_CreateFolder_MoveDocument_Flow()
    {
        // 1. Register
        var email = $"user{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.Equal(email, authResponse.Email);

        // 2. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginAuthResponse = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginAuthResponse);
        Assert.Equal(email, loginAuthResponse.Email);

        // Use the token from login
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginAuthResponse.AccessToken);

        // 3. Upload document
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello"));
        content.Add(fileContent, "file", "test.txt");
        var uploadResponse = await _client.PostAsync("/api/documents", content);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var doc = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponse>();
        Assert.NotNull(doc);

        // 4. Create folder
        var folderResponse = await _client.PostAsJsonAsync("/api/folders", new { Name = "test-folder" });
        Assert.Equal(HttpStatusCode.OK, folderResponse.StatusCode);
        var folder = await folderResponse.Content.ReadFromJsonAsync<FolderResponse>();
        Assert.NotNull(folder);

        // 5. Move document
        var moveResponse = await _client.PatchAsJsonAsync($"/api/documents/{doc!.Id}/folder", new { FolderId = folder!.Id });
        Assert.Equal(HttpStatusCode.NoContent, moveResponse.StatusCode);
    }
}
