using DiscordGuessGame.Models;

namespace DiscordGuessGame.Services;

public class LobbyService
{
    private readonly Dictionary<string, GameLobby> _lobbies = new();
    private readonly GameService _gameService;
    private readonly ILogger<LobbyService> _logger;

    public LobbyService(GameService gameService, ILogger<LobbyService> logger)
    {
        _gameService = gameService;
        _logger = logger;
    }

    public GameLobby CreateLobby(string creatorId, string guildId, string channelId, bool useDms, List<DiscordMessage> messages, int messageLimit)
    {
        var lobby = new GameLobby
        {
            CreatorId = creatorId,
            GuildId = guildId,
            ChannelId = channelId,
            UseDms = useDms,
            LoadedMessages = messages,
            MessageLimit = messageLimit
        };

        _lobbies[lobby.Id] = lobby;
        _logger.LogInformation($"Created lobby {lobby.Id} by user {creatorId}");
        return lobby;
    }

    public GameLobby? GetLobby(string lobbyId)
    {
        if (_lobbies.TryGetValue(lobbyId, out var lobby))
        {
            lobby.LastActivityAt = DateTime.UtcNow;
            return lobby;
        }
        return null;
    }

    public LobbyPlayer? AddPlayerToLobby(string lobbyId, string playerName)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null) return null;

        var player = new LobbyPlayer { Name = playerName };
        lobby.Players[player.Id] = player;
        _logger.LogInformation($"Player {playerName} joined lobby {lobbyId}");
        return player;
    }

    public bool RemovePlayerFromLobby(string lobbyId, string playerId)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null) return false;

        lobby.Players.Remove(playerId);
        _logger.LogInformation($"Player {playerId} removed from lobby {lobbyId}");

        if (playerId == lobby.CreatorId)
        {
            _lobbies.Remove(lobbyId);
            _logger.LogInformation($"Lobby {lobbyId} closed (creator left)");
            return true;
        }

        if (lobby.Players.Count == 0)
        {
            _lobbies.Remove(lobbyId);
            _logger.LogInformation($"Lobby {lobbyId} closed (empty)");
        }

        return true;
    }

    public bool SetPlayerReady(string lobbyId, string playerId, bool isReady)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby?.Players.TryGetValue(playerId, out var player) != true) return false;

        player.IsReady = isReady;
        return true;
    }

    public bool UpdatePlayerName(string lobbyId, string playerId, string newName)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby?.Players.TryGetValue(playerId, out var player) != true) return false;

        player.Name = newName;
        return true;
    }

    public bool StartGame(string lobbyId, string creatorId)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null || lobby.CreatorId != creatorId) return false;
        if (lobby.Players.Count == 0) return false;

        lobby.State = LobbyState.GameStarting;
        lobby.CurrentRoundIndex = 0;
        return true;
    }

    public GameRound? StartNextRound(string lobbyId)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null) return null;

        if (lobby.LoadedMessages.Count == 0)
        {
            _logger.LogWarning($"Lobby {lobbyId} has no loaded messages");
            return null;
        }

        if (lobby.CurrentRoundIndex >= lobby.LoadedMessages.Count)
        {
            lobby.State = LobbyState.GameEnded;
            return null;
        }

        // Create round from lobby's loaded messages instead of GameService pool
        var round = CreateRoundFromMessages(lobby.LoadedMessages, 4);
        if (round == null)
        {
            _logger.LogWarning($"Failed to create round from {lobby.LoadedMessages.Count} messages");
            return null;
        }

        lobby.CurrentRound = round;
        lobby.State = LobbyState.RoundActive;
        lobby.CurrentRoundIndex++;
        lobby.CurrentRoundAnsweredPlayers.Clear(); // Reset for new round
        lobby.CurrentRoundAnswerCounter = 0; // Reset answer order counter

        return round;
    }

    private GameRound? CreateRoundFromMessages(List<DiscordMessage> messages, int numberOfOptions)
    {
        if (messages.Count < 1) return null;

        var random = new Random();
        var selectedMessage = messages[random.Next(messages.Count)];

        var correctAuthor = new GameOption
        {
            AuthorId = selectedMessage.AuthorId,
            AuthorName = selectedMessage.AuthorName
        };

        // Get unique authors for options
        var uniqueAuthors = messages
            .GroupBy(m => m.AuthorId)
            .Select(g => new GameOption
            {
                AuthorId = g.Key,
                AuthorName = g.First().AuthorName
            })
            .Where(a => a.AuthorId != selectedMessage.AuthorId)
            .OrderBy(_ => random.Next())
            .Take(numberOfOptions - 1)
            .ToList();

        // If not enough unique authors, pad with duplicates
        while (uniqueAuthors.Count < numberOfOptions - 1)
        {
            uniqueAuthors.Add(new GameOption
            {
                AuthorId = selectedMessage.AuthorId,
                AuthorName = selectedMessage.AuthorName
            });
        }

        var options = new List<GameOption> { correctAuthor };
        options.AddRange(uniqueAuthors);
        options = options.OrderBy(_ => random.Next()).ToList();

        return new GameRound
        {
            Message = selectedMessage,
            Options = options
        };
    }

    public (RoundResults? results, bool allPlayersAnswered) SubmitAnswer(string lobbyId, string playerId, string selectedAuthorId, int elapsedMs)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby?.CurrentRound == null) return (null, false);
        if (!lobby.Players.TryGetValue(playerId, out var player)) return (null, false);

        var isCorrect = lobby.CurrentRound.Message.AuthorId == selectedAuthorId;

        double points = 0;
        if (isCorrect)
        {
            // Track answer order only for correct answers (1st, 2nd, 3rd, etc.)
            lobby.CurrentRoundAnswerCounter++;
            player.LastAnswerOrder = lobby.CurrentRoundAnswerCounter;

            // Award points based on answer order (primary factor):
            // 1st correct: 100 base, 2nd: 90 base, 3rd: 80 base, etc.
            double basePoints = Math.Max(10.0, 110.0 - (lobby.CurrentRoundAnswerCounter * 10.0));

            // Apply time penalty (secondary factor): slower answers lose up to -9.99 points
            // Fast answer (0ms) = 0 penalty, slow answer (30s) = -9.99 penalty
            // This ensures order is dominant but time still matters within the same order position
            double timePenalty = (elapsedMs / 30000.0) * 9.99;

            points = Math.Max(0.1, basePoints - timePenalty); // Floor at 0.1 to prevent negative scores
        }
        else
        {
            player.LastAnswerOrder = null; // No order for incorrect answers
        }

        player.Score += points;
        player.LastAnswerCorrect = isCorrect;
        player.LastAnswerTimeMs = elapsedMs;

        // Track that this player has answered
        lobby.CurrentRoundAnsweredPlayers.Add(playerId);

        // Check if all players have answered
        bool allAnswered = lobby.CurrentRoundAnsweredPlayers.Count >= lobby.Players.Count;

        return (GetRoundResults(lobbyId), allAnswered);
    }

    public RoundResults? GetRoundResults(string lobbyId)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null || lobby.CurrentRound == null) return null;

        var results = new RoundResults
        {
            RoundNumber = lobby.CurrentRoundIndex,
            PlayerResults = lobby.Players.Values
                .Select(p => new PlayerRoundResult
                {
                    PlayerId = p.Id,
                    PlayerName = p.Name,
                    IsCorrect = p.LastAnswerCorrect ?? false,
                    PointsEarned = CalculatePoints(p),
                    SubmittedAtMs = p.LastAnswerTimeMs ?? 0
                })
                .ToList()
        };

        return results;
    }

    private double CalculatePoints(LobbyPlayer player)
    {
        if (player.LastAnswerCorrect != true) return 0;
        var answerOrder = player.LastAnswerOrder ?? 99;
        var elapsedMs = player.LastAnswerTimeMs ?? 30000;

        // Base points from order
        double basePoints = Math.Max(10.0, 110.0 - (answerOrder * 10.0));

        // Time penalty: slower answers lose up to -9.99 points
        double timePenalty = (elapsedMs / 30000.0) * 9.99;

        return Math.Max(0.1, basePoints - timePenalty); // Floor at 0.1 to prevent negative scores
    }

    public List<LeaderboardEntry> GetLeaderboard(string lobbyId)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null) return new();

        return lobby.Players.Values
            .OrderByDescending(p => p.Score)
            .Select(p => new LeaderboardEntry
            {
                PlayerId = p.Id,
                Name = p.Name,
                Score = p.Score
            })
            .ToList();
    }

    public void CleanupExpiredLobbies()
    {
        var expiredLobbies = _lobbies
            .Where(kvp => (DateTime.UtcNow - kvp.Value.LastActivityAt).TotalMinutes > 10)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var lobbyId in expiredLobbies)
        {
            _lobbies.Remove(lobbyId);
            _logger.LogInformation($"Cleaned up expired lobby {lobbyId}");
        }
    }
}
