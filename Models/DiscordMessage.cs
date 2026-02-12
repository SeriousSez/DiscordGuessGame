namespace DiscordGuessGame.Models;

public class DiscordMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ChannelId { get; set; } = string.Empty;

    // Mentions - map of UserID to Username
    public Dictionary<string, string> Mentions { get; set; } = new();

    // Attachment URLs
    public List<string> AttachmentUrls { get; set; } = new();

    // Embed image URLs
    public List<string> EmbedImageUrls { get; set; } = new();
}
