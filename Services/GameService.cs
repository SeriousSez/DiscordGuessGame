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

        // Select a random message
        var random = new Random();
        var selectedMessage = _messagePool[random.Next(_messagePool.Count)];

        // Get the correct author
        var correctAuthor = new GameOption
        {
            AuthorId = selectedMessage.AuthorId,
            AuthorName = selectedMessage.AuthorName
        };

        // Get other authors by random sampling from messages
        var otherMessages = _messagePool
            .Where(m => m.AuthorId != selectedMessage.AuthorId)
            .OrderBy(_ => random.Next())
            .Take(numberOfOptions - 1)
            .ToList();

        var otherAuthors = otherMessages
            .GroupBy(m => m.AuthorId)
            .Select(g => new GameOption
            {
                AuthorId = g.Key,
                AuthorName = g.First().AuthorName
            })
            .ToList();

        // If not enough unique other authors, include any other messages as options
        if (otherAuthors.Count < numberOfOptions - 1)
        {
            otherAuthors = _messagePool
                .Where(m => m.AuthorId != selectedMessage.AuthorId)
                .Select(m => new GameOption
                {
                    AuthorId = m.AuthorId,
                    AuthorName = m.AuthorName
                })
                .Distinct(new GameOptionComparer())
                .OrderBy(_ => random.Next())
                .Take(numberOfOptions - 1)
                .ToList();
        }

        // If still not enough different authors, just use the same author as filler options
        while (otherAuthors.Count < numberOfOptions - 1)
        {
            otherAuthors.Add(new GameOption
            {
                AuthorId = selectedMessage.AuthorId,
                AuthorName = selectedMessage.AuthorName
            });
        }

        // Combine and shuffle options
        var options = new List<GameOption> { correctAuthor };
        options.AddRange(otherAuthors.Take(numberOfOptions - 1));
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

    private class GameOptionComparer : IEqualityComparer<GameOption>
    {
        public bool Equals(GameOption? x, GameOption? y)
        {
            if (x == null || y == null) return x == y;
            return x.AuthorId == y.AuthorId;
        }

        public int GetHashCode(GameOption obj)
        {
            return obj.AuthorId.GetHashCode();
        }
    }
}
