namespace DiscordGuessGame.Models;

public class DiscordUser
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Discriminator { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string AccessToken { get; set; } = string.Empty;
}
