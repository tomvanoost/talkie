using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Talkie.Api.Infrastructure;
using Talkie.Api.Modules.Chat;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules.Ai;

/// <summary>
/// A simple AI assistant. When <c>AI:Endpoint</c> and <c>AI:Model</c> are configured, it talks to any
/// OpenAI-compatible endpoint (OpenAI, Azure OpenAI, Ollama, LM Studio, ...). Otherwise the built-in,
/// offline <see cref="BuiltInAssistant"/> answers. Either way the assistant is grounded with the user's
/// stats and with chat messages found through vector search.
/// </summary>
public sealed class AiModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, HashingEmbeddingGenerator>();
        services.AddScoped<AiService>();

        var endpoint = configuration["AI:Endpoint"];
        var model = configuration["AI:Model"];
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(model))
        {
            var key = configuration["AI:ApiKey"];
            services.AddChatClient(_ => new OpenAIClient(
                    new ApiKeyCredential(string.IsNullOrWhiteSpace(key) ? "unused" : key),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
                .GetChatClient(model)
                .AsIChatClient());
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ai").WithTags("AI").RequireTalkieUser();

        g.MapGet("/history", async (CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
            await db.AiMessages.AsNoTracking().Where(m => m.UserId == current.Id).OrderBy(m => m.CreatedAt)
                .Select(m => new AiMessageDto(m.Id, m.Role, m.Content, m.CreatedAt)).ToListAsync(ct));

        g.MapPost("/ask", async (AiAskRequest request, AiService ai, CancellationToken ct) =>
        {
            var text = request.Message?.Trim() ?? "";
            if (text.Length is 0 or > 2000) return BadRequest("Ask something (max 2000 characters).");
            return Results.Ok(await ai.AskAsync(text, ct));
        });

        g.MapDelete("/history", async (CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
        {
            await db.AiMessages.Where(m => m.UserId == current.Id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}

public sealed class AiService(
    TalkieDbContext db,
    CurrentUser current,
    ChatService chat,
    ILogger<AiService> logger,
    IChatClient? chatClient = null)
{
    public async Task<AiMessageDto> AskAsync(string question, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        db.AiMessages.Add(new AiMessage { UserId = me.Id, Role = "user", Content = question });
        await db.SaveChangesAsync(ct);

        var related = await chat.SearchAsync(me, question, 5, ct);
        string answer;
        if (chatClient is not null)
        {
            try
            {
                answer = await AskModelAsync(me, question, related, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI endpoint failed, falling back to the built-in assistant");
                answer = BuiltInAssistant.Answer(me, question, related);
            }
        }
        else
        {
            answer = BuiltInAssistant.Answer(me, question, related);
        }

        var reply = new AiMessage { UserId = me.Id, Role = "assistant", Content = answer };
        db.AiMessages.Add(reply);
        await db.SaveChangesAsync(ct);
        return new AiMessageDto(reply.Id, reply.Role, reply.Content, reply.CreatedAt);
    }

    private async Task<string> AskModelAsync(User me, string question, List<SearchResultDto> related, CancellationToken ct)
    {
        var history = await db.AiMessages.AsNoTracking().Where(m => m.UserId == me.Id)
            .OrderByDescending(m => m.CreatedAt).Take(12).OrderBy(m => m.CreatedAt).ToListAsync(ct);

        var context = string.Join("\n", related.Where(r => r.Score > 0.2)
            .Select(r => $"- [{r.Location}] {r.Message.SenderName}: {r.Message.Content}"));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                $"""
                You are the Talkie assistant, a friendly helper inside the Talkie chat app.
                Keep answers short. Talkie has: Chat (direct messages, groups, company server channels), AI assistant,
                Company (members & roles), Reports, Stats, Shop (exchange, boxes, avatar effects, shard boosters) and AFK (earn shards).
                The user is {me.DisplayName} ({me.Email}) of company {me.Company?.Name}.
                Their stats: money {NumberFormatter.Money(me.Money)}, shards {NumberFormatter.Compact(me.Shards)},
                messages sent {me.MessagesSent}, boxes opened {me.BoxesOpened}, shard multiplier x{me.ShardMultiplier}.
                Possibly relevant chat messages (found with semantic search):
                {(context.Length == 0 ? "(none)" : context)}
                """),
        };
        messages.AddRange(history.Select(h => new ChatMessage(h.Role == "user" ? ChatRole.User : ChatRole.Assistant, h.Content)));

        var response = await chatClient!.GetResponseAsync(messages, cancellationToken: ct);
        return string.IsNullOrWhiteSpace(response.Text) ? BuiltInAssistant.Answer(me, question, related) : response.Text;
    }
}
