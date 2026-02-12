using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using DiscordGuessGame.Models;
using System.Security.Claims;
using System.Text.Json;

namespace DiscordGuessGame.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly DiscordSettings _discordSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IOptions<DiscordSettings> discordSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<AuthController> logger)
    {
        _discordSettings = discordSettings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var scope = "identify+guilds";
        var authUrl = $"https://discord.com/api/oauth2/authorize" +
                     $"?client_id={_discordSettings.ClientId}" +
                     $"&redirect_uri={Uri.EscapeDataString(_discordSettings.RedirectUri)}" +
                     $"&response_type=code" +
                     $"&scope={scope}";

        _logger.LogInformation("OAuth Login URL: {AuthUrl}", authUrl.Replace(_discordSettings.ClientId, "CLIENT_ID_HIDDEN"));
        return Ok(new { authUrl });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("No authorization code provided");
        }

        try
        {
            // Exchange code for access token
            var httpClient = _httpClientFactory.CreateClient();

            _logger.LogInformation("Token exchange request - ClientId: {ClientId}, ClientSecret length: {SecretLength}, ClientSecret first 4: {SecretStart}, Code length: {CodeLength}, RedirectUri: {RedirectUri}",
                _discordSettings.ClientId,
                _discordSettings.ClientSecret?.Length ?? 0,
                _discordSettings.ClientSecret?.Length > 4 ? _discordSettings.ClientSecret.Substring(0, 4) + "..." : "N/A",
                code?.Length ?? 0,
                _discordSettings.RedirectUri);

            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _discordSettings.ClientId,
                ["client_secret"] = _discordSettings.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _discordSettings.RedirectUri
            });

            _logger.LogInformation("Sending token exchange request with body: client_id={ClientId}, client_secret_length={SecretLen}, grant_type=authorization_code, code_length={CodeLen}, redirect_uri={RedirectUri}",
                _discordSettings.ClientId,
                _discordSettings.ClientSecret?.Length ?? 0,
                code?.Length ?? 0,
                _discordSettings.RedirectUri);

            var tokenResponse = await httpClient.PostAsync(
                "https://discord.com/api/oauth2/token",
                requestContent);

            var responseBody = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to exchange code for token: StatusCode={StatusCode}, ErrorBody={ErrorBody}, ClientSecret issue: {SecretCheck}",
                    tokenResponse.StatusCode,
                    responseBody,
                    string.IsNullOrEmpty(_discordSettings.ClientSecret) ? "EMPTY" : "SET");
                return BadRequest($"Failed to exchange code for token: {responseBody}");
            }

            _logger.LogInformation("Token exchange successful: {TokenResponse}", responseBody);
            var tokenData = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var accessToken = tokenData.GetProperty("access_token").GetString();

            _logger.LogInformation("Extracted access token length: {Length}, starts with: {Start}",
                accessToken?.Length ?? 0,
                accessToken?.Substring(0, Math.Min(10, accessToken?.Length ?? 0)));


            // Get user info
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            var userResponse = await httpClient.GetAsync("https://discord.com/api/users/@me");

            if (!userResponse.IsSuccessStatusCode)
            {
                var userErrorBody = await userResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get user info: {StatusCode}, Body: {Body}", userResponse.StatusCode, userErrorBody);
                return BadRequest("Failed to get user info");
            }

            var userJson = await userResponse.Content.ReadAsStringAsync();
            var userData = JsonSerializer.Deserialize<JsonElement>(userJson);

            var userId = userData.GetProperty("id").GetString();
            var username = userData.GetProperty("username").GetString();
            var discriminator = userData.TryGetProperty("discriminator", out var disc)
                ? disc.GetString()
                : "0";

            _logger.LogInformation("OAuth callback successful. User: {Username}, Token length: {Length}, Token start: {TokenStart}",
                username,
                accessToken?.Length ?? 0,
                accessToken?.Substring(0, Math.Min(10, accessToken?.Length ?? 0)));

            // Create claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId ?? ""),
                new Claim(ClaimTypes.Name, username ?? ""),
                new Claim("AccessToken", accessToken ?? "")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Redirect to frontend
            var frontendUrl = Request.Host.Host.Contains("localhost")
                ? "http://localhost:4200/features/discord-guess-game"
                : "https://sezginsahin.dk/features/discord-guess-game";

            return Redirect(frontendUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth callback");
            return StatusCode(500, "Authentication failed");
        }
    }

    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            username = User.FindFirst(ClaimTypes.Name)?.Value
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out successfully" });
    }
}
