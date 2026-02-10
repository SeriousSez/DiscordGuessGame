using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DiscordGuessGame.Services;

namespace DiscordGuessGame.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiscordController : ControllerBase
{
    private readonly DiscordBotService _botService;
    private readonly GameService _gameService;
    private readonly ILogger<DiscordController> _logger;

    public DiscordController(
        DiscordBotService botService,
        GameService gameService,
        ILogger<DiscordController> logger)
    {
        _botService = botService;
        _gameService = gameService;
        _logger = logger;
    }

    [HttpGet("guilds")]
    public async Task<IActionResult> GetGuilds()
    {
        try
        {
            var accessToken = User.FindFirst("AccessToken")?.Value;
            var guilds = await _botService.GetGuildsAsync(accessToken);
            return Ok(guilds.Select(g => new { id = g.Id.ToString(), name = g.Name }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching guilds");
            return StatusCode(500, "Failed to fetch guilds");
        }
    }

    [HttpGet("guilds/{guildId}/channels")]
    public async Task<IActionResult> GetChannels(ulong guildId)
    {
        try
        {
            var accessToken = User.FindFirst("AccessToken")?.Value;
            var channels = await _botService.GetChannelsAsync(guildId, accessToken);
            return Ok(channels.Select(c => new { id = c.Id.ToString(), name = c.Name }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching channels for guild {GuildId}", guildId);
            return StatusCode(500, "Failed to fetch channels");
        }
    }

    [HttpGet("dms")]
    public async Task<IActionResult> GetDms()
    {
        try
        {
            _logger.LogInformation("GetDms called. User authenticated: {IsAuth}, Claims count: {Count}",
                User.Identity?.IsAuthenticated ?? false,
                User.Claims.Count());

            foreach (var claim in User.Claims)
            {
                var value = claim.Type == "AccessToken"
                    ? $"{claim.Value.Substring(0, Math.Min(10, claim.Value.Length))}... (length: {claim.Value.Length})"
                    : claim.Value;
                _logger.LogInformation("Claim: {Type} = {Value}", claim.Type, value);
            }

            var accessToken = User.FindFirst("AccessToken")?.Value;
            _logger.LogInformation("GetDms access token: {HasToken}, Length: {Length}, Start: {Start}",
                !string.IsNullOrEmpty(accessToken),
                accessToken?.Length ?? 0,
                accessToken?.Substring(0, Math.Min(10, accessToken?.Length ?? 0)) ?? "null");

            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized("Access token not found");
            }

            var dms = await _botService.GetDmsAsync(accessToken);
            _logger.LogInformation("Returning {Count} DMs to frontend", dms.Count);
            return Ok(dms.Select(d => new { id = d.Id.ToString(), name = d.Name }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching DMs");
            return StatusCode(500, "Failed to fetch DMs");
        }
    }

    [HttpPost("load-messages")]
    public async Task<IActionResult> LoadMessages(
        [FromBody] LoadMessagesRequest request)
    {
        try
        {
            var accessToken = User.FindFirst("AccessToken")?.Value;
            List<DiscordGuessGame.Models.DiscordMessage> messages;

            // Parse string IDs to ulongs
            bool isValidGuildId = ulong.TryParse(request.GuildId, out var guildId);
            bool isValidChannelId = ulong.TryParse(request.ChannelId, out var channelId);

            if (!isValidGuildId || !isValidChannelId)
            {
                return BadRequest(new { error = "Invalid guild or channel ID" });
            }

            // Check if loading from DMs (guildId = 0)
            if (guildId == 0)
            {
                if (string.IsNullOrEmpty(accessToken))
                {
                    return Unauthorized("Access token required for DM messages");
                }
                messages = await _botService.GetDmMessagesAsync(channelId, accessToken, request.Limit);
            }
            else
            {
                messages = await _botService.GetChannelMessagesAsync(
                    guildId,
                    channelId,
                    request.Limit);
            }

            _gameService.LoadMessages(messages);

            return Ok(new
            {
                message = "Messages loaded successfully",
                totalMessages = messages.Count,
                uniqueAuthors = _gameService.GetUniqueAuthorCount()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading messages");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("status")]
    public IActionResult GetBotStatus()
    {
        try
        {
            var accessToken = User.FindFirst("AccessToken")?.Value;
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;

            return Ok(new
            {
                authenticated = isAuthenticated,
                hasAccessToken = !string.IsNullOrEmpty(accessToken),
                accessTokenLength = accessToken?.Length ?? 0,
                username = User.FindFirst("name")?.Value ?? "Not found"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bot status");
            return StatusCode(500, "Failed to get status");
        }
    }
}

public class LoadMessagesRequest
{
    public string GuildId { get; set; } = "0";
    public string ChannelId { get; set; } = "0";
    public int Limit { get; set; } = 100;
}
