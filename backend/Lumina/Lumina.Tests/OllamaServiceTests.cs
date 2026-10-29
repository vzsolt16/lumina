using System.Net;
using System.Text.Json;
using Lumina.DTOs.AI;
using Lumina.Services.AI;
using Microsoft.Extensions.Configuration;

namespace Lumina.Tests;

public class OllamaServiceTests
{
    private readonly IConfiguration _configuration;

    public OllamaServiceTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Ollama:Model", "test-model"}
            }!)
            .Build();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    [Fact]
    public async Task GenerateAsync_Success_ReturnsResponse()
    {
        var expectedResponse = new OllamaGenerateResponse
        {
            Response = "Hello world!",
            Done = true
        };

        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(expectedResponse))
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new OllamaService(httpClient, _configuration);

        var result = await service.GenerateAsync("hi");

        Assert.Equal("Hello world!", result);
    }

    [Fact]
    public async Task GenerateAsync_IncompleteResponse_ThrowsInvalidOperationException()
    {
        var incompleteResponse = new OllamaGenerateResponse
        {
            Response = "Hello",
            Done = false
        };

        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(incompleteResponse))
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new OllamaService(httpClient, _configuration);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync("hi"));
    }

    [Fact]
    public async Task GenerateAsync_TruncatedResponse_ThrowsInvalidOperationException()
    {
        var truncatedResponse = new OllamaGenerateResponse
        {
            Response = "Hello",
            Done = true,
            DoneReason = "length"
        };

        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(truncatedResponse))
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new OllamaService(httpClient, _configuration);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync("hi"));
    }

    [Fact]
    public async Task GenerateAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Bad request error")
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new OllamaService(httpClient, _configuration);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GenerateAsync("hi"));
    }

    [Fact]
    public async Task GenerateStreamAsync_StreamsTokens()
    {
        var chunks = new[]
        {
            new OllamaGenerateResponse { Response = "Hello", Done = false },
            new OllamaGenerateResponse { Response = " world!", Done = true }
        };

        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var content = string.Join("\n", chunks.Select(c => JsonSerializer.Serialize(c)));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new OllamaService(httpClient, _configuration);

        var results = new List<string>();
        await foreach (var token in service.GenerateStreamAsync("hi"))
        {
            results.Add(token);
        }

        Assert.Equal(new[] { "Hello", " world!" }, results);
    }

    [Fact]
    public async Task ChatStreamAsync_StreamsContentAndTools()
    {
        var chunks = new[]
        {
            new OllamaChatResponse
            {
                Message = new OllamaChatMessage { Content = "Hello" },
                Done = false
            },
            new OllamaChatResponse
            {
                Message = new OllamaChatMessage
                {
                    ToolCalls = new List<OllamaToolCall>
                    {
                        new OllamaToolCall { Function = new OllamaToolCallFunction { Name = "test-tool", Arguments = JsonSerializer.Deserialize<JsonElement>("{}") } }
                    }
                },
                Done = true
            }
        };

        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var content = string.Join("\n", chunks.Select(c => JsonSerializer.Serialize(c)));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new OllamaService(httpClient, _configuration);

        var results = new List<AiChatDelta>();
        await foreach (var delta in service.ChatStreamAsync(new List<AiMessage>(), new List<AiTool>()))
        {
            results.Add(delta);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Hello", results[0].Content);
        Assert.Null(results[0].ToolCalls);
        Assert.True(string.IsNullOrEmpty(results[1].Content));
        Assert.Single(results[1].ToolCalls!);
        Assert.Equal("test-tool", results[1].ToolCalls![0].Name);
    }
}
