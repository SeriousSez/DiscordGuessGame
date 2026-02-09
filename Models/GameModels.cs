namespace DiscordGuessGame.Models;

public class GameRound
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DiscordMessage Message { get; set; } = new();
    public List<GameOption> Options { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GameOption
{
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
}

public class GameAnswer
{
    public string RoundId { get; set; } = string.Empty;
    public string SelectedAuthorId { get; set; } = string.Empty;
}

public class GameResult
{
    public bool IsCorrect { get; set; }
    public string CorrectAuthorId { get; set; } = string.Empty;
    public string CorrectAuthorName { get; set; } = string.Empty;
}
