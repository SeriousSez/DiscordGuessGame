using DiscordGuessGame.Models;

namespace DiscordGuessGame.Services;

public class GameService
{
    private readonly Dictionary<string, GameRound> _activeRounds = new();
    private List<DiscordMessage> _messagePool = new();

    public void LoadMessages(List<DiscordMessage> messages)
    {
        _messagePool = messages;
    }

    public GameRound? CreateRound(int numberOfOptions = 4)
    {
        if (_messagePool.Count < numberOfOptions)
        {
            return null;
        }

        // Get unique authors
        var uniqueAuthors = _messagePool
            .GroupBy(m => m.AuthorId)
            .Where(g => g.Any())
            .ToList();

        if (uniqueAuthors.Count < numberOfOptions)
        {
            return null;
        }

        // Select a random message
        var random = new Random();
        var selectedMessage = _messagePool[random.Next(_messagePool.Count)];

        // Get the correct author
        var correctAuthor = new GameOption
        {
            AuthorId = selectedMessage.AuthorId,
            AuthorName = selectedMessage.AuthorName
        };

        // Get other random authors (excluding correct one)
        var otherAuthors = uniqueAuthors
            .Where(g => g.Key != selectedMessage.AuthorId)
            .OrderBy(_ => random.Next())
            .Take(numberOfOptions - 1)
            .Select(g => new GameOption
            {
                AuthorId = g.Key,
                AuthorName = g.First().AuthorName
            })
            .ToList();

        // Combine and shuffle options
        var options = new List<GameOption> { correctAuthor };
        options.AddRange(otherAuthors);
        options = options.OrderBy(_ => random.Next()).ToList();

        var round = new GameRound
        {
            Message = selectedMessage,
            Options = options
        };

        _activeRounds[round.Id] = round;
        return round;
    }

    public GameResult? SubmitAnswer(GameAnswer answer)
    {
        if (!_activeRounds.TryGetValue(answer.RoundId, out var round))
        {
            return null;
        }

        var result = new GameResult
        {
            IsCorrect = round.Message.AuthorId == answer.SelectedAuthorId,
            CorrectAuthorId = round.Message.AuthorId,
            CorrectAuthorName = round.Message.AuthorName
        };

        // Clean up old rounds (optional - could keep for stats)
        _activeRounds.Remove(answer.RoundId);

        return result;
    }

    public int GetMessageCount() => _messagePool.Count;

    public int GetUniqueAuthorCount() => _messagePool.Select(m => m.AuthorId).Distinct().Count();
}
