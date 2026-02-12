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
            RemainingMessages = messages.ToList(),
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

    public bool ReloadMessages(string lobbyId, List<DiscordMessage> newMessages)
    {
        var lobby = GetLobby(lobbyId);
        if (lobby == null) return false;

        // Replace remaining messages with new ones
        lobby.RemainingMessages = newMessages.ToList();
        lobby.LoadedMessages = newMessages;

        _logger.LogInformation($"Reloaded {newMessages.Count} messages for lobby {lobbyId}");
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

        if (lobby.RemainingMessages.Count == 0)
        {
            lobby.State = LobbyState.GameEnded;
            return null;
        }

        // Create round from remaining messages to avoid repeats
        var round = CreateRoundFromMessages(lobby.RemainingMessages, 4, out var usedMessage);
        if (round == null)
        {
            _logger.LogWarning($"Failed to create round from {lobby.RemainingMessages.Count} remaining messages");
            return null;
        }

        if (usedMessage != null)
        {
            lobby.RemainingMessages.Remove(usedMessage);
        }

        lobby.CurrentRound = round;
        lobby.State = LobbyState.RoundActive;
        lobby.CurrentRoundIndex++;
        lobby.CurrentRoundAnsweredPlayers.Clear(); // Reset for new round
        lobby.CurrentRoundAnswerCounter = 0; // Reset answer order counter

        // Reset player answer states for new round
        foreach (var player in lobby.Players.Values)
        {
            player.LastAnswerCorrect = null;
            player.LastAnswerTimeMs = null;
        }

        return round;
    }

    private GameRound? CreateRoundFromMessages(List<DiscordMessage> messages, int numberOfOptions, out DiscordMessage? usedMessage)
    {
        usedMessage = null;
        if (messages.Count < 1) return null;

        var random = new Random();
        var selectedMessage = messages[random.Next(messages.Count)];
        usedMessage = selectedMessage;

        var correctAuthor = new GameOption
        {
            AuthorId = selectedMessage.AuthorId,
            AuthorName = selectedMessage.AuthorName
        };

        // Get all unique authors from the messages
        var allUniqueAuthors = messages
            .GroupBy(m => m.AuthorId)
            .Select(g => new GameOption
            {
                AuthorId = g.Key,
                AuthorName = g.First().AuthorName
            })
            .Where(a => a.AuthorId != selectedMessage.AuthorId)
            .ToList();

        // Randomly select wrong options from all available authors
        var incorrectOptions = allUniqueAuthors
            .OrderBy(_ => random.Next())
            .Take(numberOfOptions - 1)
            .ToList();

        // If not enough unique authors, pad with duplicates of the correct author
        while (incorrectOptions.Count < numberOfOptions - 1)
        {
            incorrectOptions.Add(new GameOption
            {
                AuthorId = selectedMessage.AuthorId,
                AuthorName = selectedMessage.AuthorName
            });
        }

        var options = new List<GameOption> { correctAuthor };
        options.AddRange(incorrectOptions);
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
            // Calculate max time in milliseconds from the round timer
            double maxTimeMs = lobby.SecondsPerRound * 1000.0;

            // Award points based on speed: 100 points for instant answer, 0 for timeout
            // Linear deduction: 100 - (elapsed / totalTime * 100)
            points = Math.Max(0.1, 100.0 - (elapsedMs / maxTimeMs * 100.0));
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

        double maxTimeMs = lobby.SecondsPerRound * 1000.0;

        var results = new RoundResults
        {
            RoundNumber = lobby.CurrentRoundIndex,
            PlayerResults = lobby.Players.Values
                .Select(p => new PlayerRoundResult
                {
                    PlayerId = p.Id,
                    PlayerName = p.Name,
                    IsCorrect = p.LastAnswerCorrect ?? false,
                    PointsEarned = CalculatePoints(p, maxTimeMs),
                    SubmittedAtMs = p.LastAnswerTimeMs ?? 0
                })
                .ToList()
        };

        return results;
    }

    private double CalculatePoints(LobbyPlayer player, double maxTimeMs = 30000.0)
    {
        if (player.LastAnswerCorrect != true) return 0;
        double elapsedMs = player.LastAnswerTimeMs ?? (int)maxTimeMs;

        // Award points based on speed: 100 points for instant answer, 0 for timeout
        // Linear deduction: 100 - (elapsed / totalTime * 100)
        return Math.Max(0.1, 100.0 - (elapsedMs / maxTimeMs * 100.0));
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
