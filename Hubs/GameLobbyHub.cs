using Microsoft.AspNetCore.SignalR;
using DiscordGuessGame.Models;
using DiscordGuessGame.Services;

namespace DiscordGuessGame.Hubs;

public class GameLobbyHub : Hub
{
    private readonly LobbyService _lobbyService;
    private readonly ILogger<GameLobbyHub> _logger;

    public class JoinLobbyResult
    {
        public bool Success { get; set; }
        public string? PlayerId { get; set; }
        public string? Error { get; set; }
    }

    public GameLobbyHub(LobbyService lobbyService, ILogger<GameLobbyHub> logger)
    {
        _lobbyService = lobbyService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client {Context.ConnectionId} connected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client {Context.ConnectionId} disconnected");

        // Clean up lobbies if creator disconnects
        // This would be handled by lobby tracking connection IDs
        await base.OnDisconnectedAsync(exception);
    }

    // Lobby Creator: Create a lobby after loading messages
    public async Task<string> CreateLobby(string guildId, string channelId, string channelName, string guildName, bool useDms, List<DiscordMessage> messages, int messageLimit, int secondsPerRound = 30)
    {
        try
        {
            var lobby = _lobbyService.CreateLobby(
                Context.ConnectionId,
                guildId,
                channelId,
                useDms,
                messages,
                messageLimit
            );

            lobby.ChannelName = channelName;
            lobby.GuildName = guildName;
            lobby.SecondsPerRound = secondsPerRound;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby-{lobby.Id}");

            _logger.LogInformation($"Lobby {lobby.Id} created by {Context.ConnectionId}");

            return lobby.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating lobby");
            throw;
        }
    }

    // Player: Join a lobby
    public async Task<JoinLobbyResult> JoinLobby(string lobbyId, string playerName)
    {
        try
        {
            // Check if this connection already joined this lobby
            if (Context.Items.TryGetValue("LobbyId", out var existingLobbyId) &&
                existingLobbyId as string == lobbyId)
            {
                var existingPlayerId = Context.Items["PlayerId"] as string;
                _logger.LogWarning($"Connection {Context.ConnectionId} already joined lobby {lobbyId} as {existingPlayerId}");
                return new JoinLobbyResult { Success = true, PlayerId = existingPlayerId };
            }

            var lobby = _lobbyService.GetLobby(lobbyId);
            if (lobby == null)
                return new JoinLobbyResult { Success = false, Error = "Lobby not found" };

            if (lobby.State != LobbyState.Waiting)
                return new JoinLobbyResult { Success = false, Error = "Lobby is not accepting new players" };

            var player = _lobbyService.AddPlayerToLobby(lobbyId, playerName);
            if (player == null)
                return new JoinLobbyResult { Success = false, Error = "Failed to add player to lobby" };

            // If the creator is joining, switch creator ID to the player ID for stable identity
            if (lobby.CreatorId == Context.ConnectionId)
            {
                lobby.CreatorId = player.Id;
            }

            // Map connection ID to player for tracking
            await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby-{lobbyId}");

            // Store mapping for later reference
            Context.Items["LobbyId"] = lobbyId;
            Context.Items["PlayerId"] = player.Id;

            await Clients.Group($"lobby-{lobbyId}").SendAsync("PlayerJoined", playerName, GetLobbyState(lobbyId));

            _logger.LogInformation($"Player {playerName} ({player.Id}) joined lobby {lobbyId}");

            return new JoinLobbyResult { Success = true, PlayerId = player.Id };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining lobby");
            return new JoinLobbyResult { Success = false, Error = ex.Message };
        }
    }

    // Player: Ready up
    public async Task SetPlayerReady(string lobbyId, string playerId, bool isReady)
    {
        try
        {
            _lobbyService.SetPlayerReady(lobbyId, playerId, isReady);

            var lobbyState = GetLobbyState(lobbyId);
            await Clients.Group($"lobby-{lobbyId}").SendAsync("PlayerReadyStatusChanged", playerId, isReady, lobbyState);

            _logger.LogInformation($"Player {playerId} ready status: {isReady}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting player ready status");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    // Player: Update name
    public async Task UpdatePlayerName(string lobbyId, string playerId, string newName)
    {
        try
        {
            var trimmedName = (newName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                await Clients.Caller.SendAsync("Error", "Name cannot be empty");
                return;
            }

            if (!_lobbyService.UpdatePlayerName(lobbyId, playerId, trimmedName))
            {
                await Clients.Caller.SendAsync("Error", "Player not found in lobby");
                return;
            }

            var lobbyState = GetLobbyState(lobbyId);
            await Clients.Group($"lobby-{lobbyId}").SendAsync("PlayerNameChanged", playerId, trimmedName, lobbyState);

            _logger.LogInformation($"Player {playerId} updated name to {trimmedName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player name");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    // Player: Send chat message
    public async Task SendChatMessage(string lobbyId, string playerId, string message)
    {
        try
        {
            var trimmedMessage = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedMessage))
            {
                return;
            }

            var lobby = _lobbyService.GetLobby(lobbyId);
            if (lobby == null)
            {
                await Clients.Caller.SendAsync("Error", "Lobby not found");
                return;
            }

            if (!lobby.Players.TryGetValue(playerId, out var player))
            {
                await Clients.Caller.SendAsync("Error", "Player not found in lobby");
                return;
            }

            await Clients.Group($"lobby-{lobbyId}").SendAsync("ChatMessage", new
            {
                playerId,
                playerName = player.Name,
                message = trimmedMessage,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _logger.LogInformation($"Chat message from {playerId} in lobby {lobbyId}: {trimmedMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending chat message");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    // Creator: Reload messages for a new game in the same lobby
    public async Task ReloadMessages(string lobbyId, List<Models.DiscordMessage> messages)
    {
        try
        {
            var creatorKey = Context.Items["PlayerId"] as string ?? Context.ConnectionId;
            var lobby = _lobbyService.GetLobby(lobbyId);

            if (lobby == null || lobby.CreatorId != creatorKey)
            {
                await Clients.Caller.SendAsync("Error", "Only the creator can reload messages");
                return;
            }

            if (!_lobbyService.ReloadMessages(lobbyId, messages))
            {
                await Clients.Caller.SendAsync("Error", "Failed to reload messages");
                return;
            }

            _logger.LogInformation($"Messages reloaded in lobby {lobbyId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading messages");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    // Creator: Start the game
    public async Task StartGame(string lobbyId)
    {
        try
        {
            var creatorKey = Context.Items["PlayerId"] as string ?? Context.ConnectionId;
            if (!_lobbyService.StartGame(lobbyId, creatorKey))
            {
                await Clients.Caller.SendAsync("Error", "Only the creator can start the game");
                return;
            }

            var lobbyState = GetLobbyState(lobbyId);
            await Clients.Group($"lobby-{lobbyId}").SendAsync("GameStarting", lobbyState);

            _logger.LogInformation($"Game started in lobby {lobbyId}, starting first round");

            // Start first round immediately
            await StartNextRound(lobbyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting game");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    // Start next round (called by server)
    private async Task StartNextRound(string lobbyId)
    {
        try
        {
            var round = _lobbyService.StartNextRound(lobbyId);
            if (round == null)
            {
                // Game ended
                var leaderboard = _lobbyService.GetLeaderboard(lobbyId);
                await Clients.Group($"lobby-{lobbyId}").SendAsync("GameEnded", leaderboard);
                return;
            }

            _logger.LogInformation($"=== ROUND CREATED FOR LOBBY {lobbyId} ===");
            _logger.LogInformation($"Round ID: {round.Id}");
            _logger.LogInformation($"Message: {round.Message?.Content} (AuthorId: {round.Message?.AuthorId}, AuthorName: {round.Message?.AuthorName})");
            _logger.LogInformation($"Options count: {round.Options?.Count ?? 0}");
            if (round.Options != null)
            {
                for (int i = 0; i < round.Options.Count; i++)
                {
                    _logger.LogInformation($"  Option {i}: AuthorId={round.Options[i].AuthorId}, AuthorName={round.Options[i].AuthorName}");
                }
            }
            _logger.LogInformation($"====================================");

            var lobbyState = GetLobbyState(lobbyId);
            await Clients.Group($"lobby-{lobbyId}").SendAsync("RoundStarted", round, lobbyState);

            _logger.LogInformation($"Round {round.Id} started in lobby {lobbyId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting next round");
            await Clients.Group($"lobby-{lobbyId}").SendAsync("Error", ex.Message);
        }
    }

    // Player: Submit answer
    public async Task SubmitAnswer(string lobbyId, string playerId, string selectedAuthorId, int elapsedMs)
    {
        try
        {
            var (results, allPlayersAnswered) = _lobbyService.SubmitAnswer(lobbyId, playerId, selectedAuthorId, elapsedMs);

            // Notify all players of answer submission
            await Clients.Group($"lobby-{lobbyId}").SendAsync("AnswerSubmitted", playerId);

            _logger.LogInformation($"Player {playerId} answered in lobby {lobbyId}. All answered: {allPlayersAnswered}");

            // If all players have answered, automatically end the round
            if (allPlayersAnswered)
            {
                _logger.LogInformation($"All players answered in lobby {lobbyId}, ending round automatically");
                await EndRound(lobbyId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting answer");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    // End round and show results
    public async Task EndRound(string lobbyId)
    {
        try
        {
            var results = _lobbyService.GetRoundResults(lobbyId);
            var leaderboard = _lobbyService.GetLeaderboard(lobbyId);

            await Clients.Group($"lobby-{lobbyId}").SendAsync("RoundEnded", results, leaderboard);

            _logger.LogInformation($"Round ended in lobby {lobbyId}");

            // Wait 7 seconds before starting next round to show results
            await Task.Delay(7000);
            await StartNextRound(lobbyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending round");
            await Clients.Group($"lobby-{lobbyId}").SendAsync("Error", ex.Message);
        }
    }

    // Get current lobby state
    public object GetLobbyState(string lobbyId)
    {
        var lobby = _lobbyService.GetLobby(lobbyId);
        if (lobby == null) return new { error = "Lobby not found" };

        return new
        {
            lobbyId = lobby.Id,
            state = lobby.State.ToString(),
            currentRound = lobby.CurrentRoundIndex,
            totalRounds = lobby.LoadedMessages.Count,
            channelName = lobby.ChannelName,
            guildName = lobby.GuildName,
            players = lobby.Players.Select(p => new
            {
                id = p.Key,
                name = p.Value.Name,
                isReady = p.Value.IsReady,
                score = p.Value.Score
            }).ToList()
        };
    }

    // Leave lobby
    public async Task LeaveLobby(string lobbyId, string playerId)
    {
        try
        {
            _lobbyService.RemovePlayerFromLobby(lobbyId, playerId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"lobby-{lobbyId}");
            await Clients.Group($"lobby-{lobbyId}").SendAsync("PlayerLeft", playerId);

            _logger.LogInformation($"Player {playerId} left lobby {lobbyId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving lobby");
        }
    }
}
