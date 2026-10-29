using System.Globalization;
using System.Text;
using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Modules.Ai;

/// <summary>
/// An offline, rule-based assistant so the AI page works without any external model.
/// It knows how Talkie works, can read your stats and can find chat messages through vector search.
/// </summary>
public static class BuiltInAssistant
{
    private static readonly (string[] Keywords, string Answer)[] Topics =
    [
        (["afk", "idle", "away"], "Open **AFK** in the sidebar and click **Go AFK**, then confirm with **Yes**. While you are AFK you earn 1 shard every minute (more with a shard booster). Click **Stop AFK** to come back."),
        (["box", "boxes", "chest", "loot"], "In the **Shop** you can buy boxes: the **Common Box** ($200) gives $100–$1K, the **Rare Box** ($500) gives $200–$10K and the **Shard Box** ($1K) gives 100–5K shards. After buying, tap the box as fast as you can to open it!"),
        (["effect", "lightning", "aura", "sparkle"], "Avatar effects such as **Energy Lightning** are sold in the **Shop → Avatar effects** tab. Buy one and it is equipped right away; switch or remove effects from the same tab."),
        (["booster", "boost", "multiplier", "x2", "x4"], "Shard boosters (x2 and x4) are bought with money in **Shop → Boosters**. They are permanent and multiply the shards you earn while AFK."),
        (["exchange", "convert", "sell shard", "buy shard"], "In **Shop → Exchange** you can buy shards with money or sell shards for money."),
        (["report"], "Go to **Reports**, click **Report a user**, pick the user's e-mail and describe the reason. Only admin@talkie.com can remove reports."),
        (["channel"], "Company owners can create text channels on the server: open **Chat → Servers**, click the **+** next to *Text channels*, then set a name, description, which roles can see it and the permissions per role (send messages, delete messages, react with emoji)."),
        (["role", "roles"], "Roles are created when you create your company and can be managed in **Company → Roles** (or with the **+** in the channel dialog). Each role has a name and a color."),
        (["group"], "In **Chat → Direct Messages** click **+** and choose **New group**. Give the group a name and add people by e-mail. The group owner can delete any message in the group."),
        (["direct message", "dm", "message someone", "send a message"], "In **Chat → Direct Messages** click **+**, type the person's e-mail and start chatting. You can attach files, paste images, reply to messages and react with emoji."),
        (["avatar", "profile picture", "profile", "photo"], "Click your avatar in the top-right corner and choose **Account**. There you can upload a profile picture, change your display name and password."),
        (["member", "invite", "add someone", "employee"], "Company owners can add members in **Company → Add member** with their name, surname and e-mail. They receive a temporary password by e-mail."),
        (["playtime"], "Playtime counts how long you have had Talkie open. You can see it on the **Stats** page."),
        (["format", "1k", "number"], "Talkie shortens big numbers: 1,000 = 1K, 1,000,000 = 1M, then 1B, 1T and 1Q."),
    ];

    public static string Answer(User me, string question, IReadOnlyList<SearchResultDto> related)
    {
        var q = question.ToLowerInvariant();

        if (ContainsAny(q, "hello", "hi ", "hey", "good morning", "good afternoon") || q is "hi" or "hey")
            return $"Hi {me.DisplayName}! 👋 I'm the Talkie assistant. Ask me about your stats, how a feature works, or say *search* followed by a topic to find chat messages.";

        if (ContainsAny(q, "what can you do", "help", "commands"))
            return "I can:\n- show **your stats** (try *\"my stats\"*)\n- explain features like AFK, the shop, boxes, roles and channels\n- **search your chats** (try *\"search lunch plans\"*)\n- tell you the date and time.";

        if (ContainsAny(q, "my stats", "how much money", "my money", "my shards", "how many shards", "balance", "how rich"))
            return $"Here are your stats, {me.DisplayName}:\n- 💵 Money: **{NumberFormatter.Money(me.Money)}**\n- 💎 Shards: **{NumberFormatter.Compact(me.Shards)}**\n- ⏱️ Playtime: **{NumberFormatter.Duration(me.PlaytimeSeconds)}**\n- 💬 Messages sent: **{NumberFormatter.Compact(me.MessagesSent)}**\n- 📦 Boxes opened: **{NumberFormatter.Compact(me.BoxesOpened)}**\n- ⚡ Shard multiplier: **x{me.ShardMultiplier}**";

        if (ContainsAny(q, "what time", "what day", "date today", "today's date", "what is the date"))
            return $"It is {DateTimeOffset.UtcNow.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)} (UTC).";

        if (ContainsAny(q, "search", "find", "look for", "who said", "messages about"))
            return SearchAnswer(related);

        var topic = Topics.FirstOrDefault(t => t.Keywords.Any(k => q.Contains(k)));
        if (topic.Answer is not null) return topic.Answer;

        if (ContainsAny(q, "thank")) return "You're welcome! 😊";

        var good = related.Where(r => r.Score > 0.35).ToList();
        if (good.Count > 0) return "I'm not sure, but these messages look related:\n" + FormatResults(good);

        return "I'm a simple assistant, so I didn't quite get that. Try asking *\"what can you do?\"*, *\"my stats\"*, *\"how do I go AFK?\"* or *\"search <topic>\"*.";
    }

    private static string SearchAnswer(IReadOnlyList<SearchResultDto> related)
    {
        var hits = related.Where(r => r.Score > 0.15).ToList();
        return hits.Count == 0
            ? "I couldn't find any messages about that in your chats."
            : "Here is what I found in your chats:\n" + FormatResults(hits);
    }

    private static string FormatResults(IEnumerable<SearchResultDto> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            var text = r.Message.Content.Length > 140 ? r.Message.Content[..140] + "…" : r.Message.Content;
            sb.AppendLine($"- **{r.Message.SenderName}** in *{r.Location}*: \"{text}\"");
        }
        return sb.ToString().TrimEnd();
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);
}
