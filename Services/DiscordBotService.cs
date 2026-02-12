using Discord;
using Discord.WebSocket;
using DiscordGuessGame.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DiscordGuessGame.Services;

public class DiscordBotService
{
    private readonly DiscordSocketClient _client;
    private readonly string _botToken;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordBotService(IOptions<DiscordSettings> settings, IHttpClientFactory httpClientFactory, ILogger<DiscordBotService> logger)
    {
        _botToken = settings.Value.BotToken;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds |
                           GatewayIntents.GuildMessages |
                           GatewayIntents.MessageContent
        };

        _client = new DiscordSocketClient(config);
        _client.Log += LogAsync;
    }

    public async Task StartAsync()
    {
        if (string.IsNullOrEmpty(_botToken) || _botToken == "YOUR_DISCORD_BOT_TOKEN")
        {
            _logger.LogWarning("Discord bot token not configured. Bot will not start.");
            return;
        }

        try
        {
            await _client.LoginAsync(TokenType.Bot, _botToken);
            await _client.StartAsync();

            // Wait for the bot to connect and cache guilds
            var maxRetries = 50;
            var retryCount = 0;
            while (_client.Guilds.Count == 0 && retryCount < maxRetries)
            {
                await Task.Delay(100);
                retryCount++;
            }

            if (_client.Guilds.Count > 0)
            {
                _logger.LogInformation("✅ Bot connected successfully. Cached {GuildCount} guilds. Guild IDs: {GuildIds}",
                    _client.Guilds.Count,
                    string.Join(", ", _client.Guilds.Select(g => $"{g.Name}({g.Id})")));
            }
            else
            {
                _logger.LogWarning("❌ Bot did not cache any guilds after {Timeout}ms. Bot token may be invalid or bot is not added to any servers.",
                    maxRetries * 100);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error starting Discord bot. Token may be invalid: {TokenStart}",
                _botToken?.Substring(0, Math.Min(20, _botToken.Length)));
            throw;
        }
    }

    public async Task<List<DiscordMessage>> GetChannelMessagesAsync(ulong guildId, ulong channelId, int limit = 100)
    {
        // Handle DMs (guildId = 0)
        if (guildId == 0)
        {
            throw new Exception("DM messages require user access token and must be fetched via GetDmMessagesAsync");
        }

        var guild = _client.GetGuild(guildId);
        if (guild == null)
        {
            throw new Exception($"Bot is not in guild {guildId}");
        }

        var channel = guild.GetTextChannel(channelId);
        if (channel == null)
        {
            throw new Exception($"Channel {channelId} not found in guild {guildId}");
        }

        // Fetch more messages to account for filtering (bots and empty messages)
        // Request up to 2x the limit to ensure we get enough after filtering
        int requestLimit = Math.Min(limit * 2, 1000); // Discord API max is 100 per page, so cap at reasonable amount
        var messages = await channel.GetMessagesAsync(requestLimit).FlattenAsync();

        var filtered = messages
            .Where(m =>
            {
                var hasContent = !string.IsNullOrWhiteSpace(m.Content);
                var hasAttachments = m.Attachments.Any();
                var hasEmbedImages = m.Embeds.Any(e => e.Image.HasValue);
                return !m.Author.IsBot && (hasContent || hasAttachments || hasEmbedImages);
            })
            .Select(m =>
            {
                // Build mentions dictionary from mentioned user IDs
                var mentions = new Dictionary<string, string>();
                foreach (var userId in m.MentionedUserIds)
                {
                    var user = guild.GetUser(userId);
                    if (user != null)
                    {
                        mentions[userId.ToString()] = user.Username;
                    }
                }

                return new DiscordMessage
                {
                    MessageId = m.Id.ToString(),
                    Content = m.Content,
                    AuthorId = m.Author.Id.ToString(),
                    AuthorName = m.Author.Username,
                    Timestamp = m.Timestamp.UtcDateTime,
                    ChannelId = channelId.ToString(),
                    // Capture mentions
                    Mentions = mentions,
                    // Capture attachment URLs
                    AttachmentUrls = m.Attachments.Select(a => a.ProxyUrl).ToList(),
                    // Capture embed image URLs
                    EmbedImageUrls = m.Embeds
                        .Where(e => e.Image.HasValue)
                        .Select(e => e.Image.Value.ProxyUrl)
                        .ToList()
                };
            })
            .Take(limit)  // Return only up to the requested limit
            .ToList();

        return filtered;
    }

    public async Task<List<DiscordMessage>> GetDmMessagesAsync(ulong channelId, string userAccessToken, int limit = 100)
    {
        if (string.IsNullOrEmpty(userAccessToken))
        {
            throw new Exception("User access token required to fetch DM messages");
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {userAccessToken}");

            // Request 2x limit to account for filtering (bots and empty messages)
            int requestLimit = Math.Min(limit * 2, 100); // Discord API max is 100 per request
            var response = await httpClient.GetAsync($"https://discord.com/api/channels/{channelId}/messages?limit={requestLimit}");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to fetch DM messages: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var messages = JsonSerializer.Deserialize<JsonElement[]>(json);

            return messages
                .Where(m =>
                {
                    var content = m.GetProperty("content").GetString();
                    var author = m.GetProperty("author");
                    var isBot = author.GetProperty("bot").GetBoolean();
                    var hasContent = !string.IsNullOrWhiteSpace(content);
                    var hasAttachments = m.TryGetProperty("attachments", out var attachmentsArray)
                                         && attachmentsArray.ValueKind == JsonValueKind.Array
                                         && attachmentsArray.GetArrayLength() > 0;
                    var hasEmbedImages = m.TryGetProperty("embeds", out var embedsArray)
                                         && embedsArray.ValueKind == JsonValueKind.Array
                                         && embedsArray.EnumerateArray().Any(e => e.TryGetProperty("image", out _));
                    return !isBot && (hasContent || hasAttachments || hasEmbedImages);
                })
                .Select(m =>
                {
                    var author = m.GetProperty("author");
                    var mentions = new Dictionary<string, string>();
                    var attachmentUrls = new List<string>();
                    var embedImageUrls = new List<string>();

                    // Extract mentions from the raw JSON
                    if (m.TryGetProperty("mentions", out var mentionsArray))
                    {
                        foreach (var mention in mentionsArray.EnumerateArray())
                        {
                            var userId = mention.GetProperty("id").GetString() ?? "";
                            var username = mention.GetProperty("username").GetString() ?? "Unknown";
                            mentions[userId] = username;
                        }
                    }

                    // Extract attachment URLs
                    if (m.TryGetProperty("attachments", out var attachmentsArray))
                    {
                        foreach (var attachment in attachmentsArray.EnumerateArray())
                        {
                            var url = attachment.GetProperty("proxy_url").GetString();
                            if (!string.IsNullOrEmpty(url))
                                attachmentUrls.Add(url);
                        }
                    }

                    // Extract embed image URLs
                    if (m.TryGetProperty("embeds", out var embedsArray))
                    {
                        foreach (var embed in embedsArray.EnumerateArray())
                        {
                            if (embed.TryGetProperty("image", out var image))
                            {
                                var imageUrl = image.GetProperty("proxy_url").GetString();
                                if (!string.IsNullOrEmpty(imageUrl))
                                    embedImageUrls.Add(imageUrl);
                            }
                        }
                    }

                    return new DiscordMessage
                    {
                        MessageId = m.GetProperty("id").GetString() ?? "",
                        Content = m.GetProperty("content").GetString() ?? "",
                        AuthorId = author.GetProperty("id").GetString() ?? "",
                        AuthorName = author.GetProperty("username").GetString() ?? "Unknown",
                        Timestamp = DateTime.Parse(m.GetProperty("timestamp").GetString() ?? DateTime.UtcNow.ToString()),
                        ChannelId = channelId.ToString(),
                        Mentions = mentions,
                        AttachmentUrls = attachmentUrls,
                        EmbedImageUrls = embedImageUrls
                    };
                })
                .Take(limit)  // Return only up to the requested limit
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching DM messages for channel {ChannelId}", channelId);
            throw;
        }
    }

    public async Task<List<(ulong Id, string Name)>> GetGuildsAsync(string? userAccessToken = null)
    {
        // If user access token is provided, fetch guilds where the user is a member
        if (!string.IsNullOrEmpty(userAccessToken))
        {
            try
            {
                var botGuildIds = _client.Guilds.Select(g => g.Id).ToHashSet();

                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {userAccessToken}");

                var response = await httpClient.GetAsync("https://discord.com/api/users/@me/guilds");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var guilds = JsonSerializer.Deserialize<JsonElement[]>(json);

                    var userGuilds = guilds.Select(g =>
                    {
                        var id = ulong.Parse(g.GetProperty("id").GetString() ?? "0");
                        var name = g.GetProperty("name").GetString() ?? "Unknown";
                        return (Id: id, Name: name);
                    });

                    if (botGuildIds.Count == 0)
                    {
                        _logger.LogWarning("Bot guild cache is empty while filtering user guilds.");
                        return new List<(ulong, string)>();
                    }

                    return userGuilds
                        .Where(g => botGuildIds.Contains(g.Id))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user guilds from Discord API");
            }
        }

        // Fallback: return guilds where bot is a member
        return _client.Guilds.Select(g => (g.Id, g.Name)).ToList();
    }

    public async Task<List<(ulong Id, string Name)>> GetChannelsAsync(ulong guildId, string? userAccessToken = null)
    {
        // Try multiple times to get the guild - it might not be fully cached yet
        SocketGuild? guild = null;

        for (int i = 0; i < 15; i++)
        {
            guild = _client.GetGuild(guildId);
            if (guild != null)
            {
                break;
            }

            // Check if guild is in the collection (exists but not fully loaded)
            if (_client.Guilds.Any(g => g.Id == guildId))
            {
                _logger.LogInformation("Guild {GuildId} is in bot's guild list but not fully loaded. Waiting... (attempt {Attempt}/15)", guildId, i + 1);
                await Task.Delay(200);
            }
            else
            {
                break;
            }
        }

        if (guild != null && guild.TextChannels.Count > 0)
        {
            var channels = guild.TextChannels
                .Select(c => (c.Id, c.Name))
                .ToList();
            _logger.LogInformation("✅ Bot found {Count} text channels in guild {GuildId}", channels.Count, guildId);
            return channels;
        }

        if (guild != null && guild.TextChannels.Count == 0)
        {
            _logger.LogWarning("Guild {GuildId} loaded but has 0 text channels. Guild has {TotalChannels} channels total.", guildId, guild.Channels.Count);
            return new List<(ulong, string)>();
        }

        _logger.LogWarning("❌ Bot not in guild {GuildId}, cannot fetch channels. Bot is in: {BotGuildIds}",
            guildId,
            string.Join(", ", _client.Guilds.Select(g => $"{g.Name}({g.Id})")));
        return new List<(ulong, string)>();
    }

    public async Task<List<(ulong Id, string Name)>> GetDmsAsync(string userAccessToken)
    {
        if (string.IsNullOrEmpty(userAccessToken))
        {
            _logger.LogWarning("GetDmsAsync called without access token");
            return new List<(ulong, string)>();
        }

        _logger.LogInformation("GetDmsAsync called with token length: {Length}, Token start: {TokenStart}",
            userAccessToken.Length,
            userAccessToken.Substring(0, Math.Min(10, userAccessToken.Length)));

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {userAccessToken}");

            var response = await httpClient.GetAsync("https://discord.com/api/users/@me/channels");
            _logger.LogInformation("Discord API DM response: {StatusCode}", response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("DM channels JSON: {Json}", json);
                var channels = JsonSerializer.Deserialize<JsonElement[]>(json);

                if (channels == null || channels.Length == 0)
                {
                    _logger.LogWarning("No DM channels returned from Discord API. JSON was: {Json}", json);
                    return new List<(ulong, string)>();
                }

                var result = channels
                    .Select(c =>
                    {
                        try
                        {
                            var id = ulong.Parse(c.GetProperty("id").GetString() ?? "0");
                            var type = c.TryGetProperty("type", out var typeElement) ? typeElement.GetInt32() : 0;

                            string name;
                            // Type 1 = DM, Type 3 = Group DM
                            // For group DMs, prefer the "name" property
                            if (type == 3 && c.TryGetProperty("name", out var groupNameElement) &&
                                groupNameElement.ValueKind == System.Text.Json.JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(groupNameElement.GetString()))
                            {
                                name = groupNameElement.GetString() ?? "Group DM";
                            }
                            else if (c.TryGetProperty("recipients", out var recipients) && recipients.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                var recipientList = recipients.EnumerateArray()
                                    .Where(r => r.TryGetProperty("username", out _))
                                    .Select(r => r.GetProperty("username").GetString())
                                    .Where(n => !string.IsNullOrWhiteSpace(n))
                                    .ToList();

                                if (recipientList.Any())
                                {
                                    // For group DMs without a name, show first few recipients
                                    name = type == 3 && recipientList.Count > 1
                                        ? string.Join(", ", recipientList.Take(3)) + (recipientList.Count > 3 ? "..." : "")
                                        : recipientList.First()!;
                                }
                                else
                                {
                                    name = "Unknown User";
                                }
                            }
                            else if (c.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                name = nameElement.GetString() ?? "Unknown Channel";
                            }
                            else
                            {
                                name = "Unknown Channel";
                            }

                            _logger.LogInformation("Parsed DM channel: Id={Id}, Name={Name}, Type={Type}", id, name, type);
                            return (id, name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing DM channel");
                            return ((ulong)0, "Error");
                        }
                    })
                    .Where(x => x.Item1 != 0 && x.Item2 != "Error")
                    .ToList();

                _logger.LogInformation("Parsed {Count} DM channels", result.Count);
                return result;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Discord API returned {StatusCode}: {Error}", response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching DM channels from Discord API");
        }

        return new List<(ulong, string)>();
    }

    private Task LogAsync(LogMessage log)
    {
        _logger.LogInformation(log.ToString());
        return Task.CompletedTask;
    }
}
