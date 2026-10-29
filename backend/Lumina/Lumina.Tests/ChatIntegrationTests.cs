using System.Net;
using System.Net.Http.Json;
using Lumina.DTOs.Chat;
using Lumina.DTOs.Document;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lumina.Tests;

public class ChatIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public ChatIntegrationTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Setup authentication
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer Test");
    }

    [Fact]
    public async Task CreateConversation_GetConversations_DeleteConversation_Flow()
    {
        // 1. Upload document (needed for chat)
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello"));
        content.Add(fileContent, "file", "test.txt");
        var uploadResponse = await _client.PostAsync("/api/documents", content);
        var doc = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponse>();
        var documentId = doc!.Id;

        // 2. Create conversation
        var createResponse = await _client.PostAsync($"/api/documents/{documentId}/chat/conversations", null);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var conversation = await createResponse.Content.ReadFromJsonAsync<ChatConversationResponse>();
        Assert.NotNull(conversation);

        // 3. Get conversations
        var getResponse = await _client.GetAsync($"/api/documents/{documentId}/chat/conversations");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var conversations = await getResponse.Content.ReadFromJsonAsync<List<ChatConversationResponse>>();
        Assert.Single(conversations!);
        Assert.Equal(conversation!.Id, conversations[0].Id);

        // 4. Delete conversation
        var deleteResponse = await _client.DeleteAsync($"/api/documents/{documentId}/chat/conversations/{conversation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        
        // 5. Verify deleted
        var getAfterDeleteResponse = await _client.GetAsync($"/api/documents/{documentId}/chat/conversations");
        var conversationsAfterDelete = await getAfterDeleteResponse.Content.ReadFromJsonAsync<List<ChatConversationResponse>>();
        Assert.Empty(conversationsAfterDelete!);
    }
}
