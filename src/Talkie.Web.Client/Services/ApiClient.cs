using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;
using Talkie.Shared;

namespace Talkie.Web.Client.Services;

public sealed class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Holds the bearer token in memory; <see cref="SessionState"/> persists it in localStorage.</summary>
public sealed class TokenStore
{
    public string? Token { get; set; }
}

public sealed class AuthHeaderHandler(TokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (tokens.Token is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Typed access to the Talkie API. Errors are surfaced as <see cref="ApiException"/> with the server's message.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Raised when the API answers 401 (token expired or account deleted).</summary>
    public event Func<Task>? Unauthorized;

    public Task<T> GetAsync<T>(string url) => SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, url));

    public Task<T> PostAsync<T>(string url, object? body = null) => SendAsync<T>(Build(HttpMethod.Post, url, body));
    public Task PostAsync(string url, object? body = null) => SendAsync(Build(HttpMethod.Post, url, body));
    public Task<T> PutAsync<T>(string url, object? body) => SendAsync<T>(Build(HttpMethod.Put, url, body));
    public Task PutAsync(string url, object? body) => SendAsync(Build(HttpMethod.Put, url, body));
    public Task DeleteAsync(string url) => SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));

    public async Task<T> UploadAsync<T>(string url, IBrowserFile file, long maxBytes)
    {
        using var content = new MultipartFormDataContent();
        var stream = new StreamContent(file.OpenReadStream(maxBytes));
        stream.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(stream, "file", file.Name);
        return await SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
    }

    public async Task<T> UploadBytesAsync<T>(string url, byte[] data, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(data);
        bytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(bytes, "file", fileName);
        return await SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, object? body) => new(method, url)
    {
        Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json),
    };

    private async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        using var response = await SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Cannot reach the Talkie server. Please try again.", HttpStatusCode.ServiceUnavailable);
        }

        if (response.IsSuccessStatusCode) return response;

        if (response.StatusCode == HttpStatusCode.Unauthorized && Unauthorized is not null)
        {
            await Unauthorized.Invoke();
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please log in again.",
            HttpStatusCode.Forbidden => "You are not allowed to do this.",
            HttpStatusCode.NotFound => "Not found.",
            _ => "Something went wrong.",
        };
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(Json);
            if (!string.IsNullOrWhiteSpace(error?.Message)) message = error.Message;
        }
        catch (Exception)
        {
            // Body was not an ApiError; keep the generic message.
        }
        var status = response.StatusCode;
        response.Dispose();
        throw new ApiException(message, status);
    }
}
