namespace DiscordGuessGame.Models;

public class LobbyPlayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool IsReady { get; set; }
    public double Score { get; set; }
    public bool? LastAnswerCorrect { get; set; }
    public int? LastAnswerTimeMs { get; set; }
    public int? LastAnswerOrder { get; set; } // Track order of answer submission (1st, 2nd, 3rd, etc.)
}

public class GameLobby
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CreatorId { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildName { get; set; } = "";
    public bool UseDms { get; set; }
    public List<DiscordMessage> LoadedMessages { get; set; } = new();
    public List<DiscordMessage> RemainingMessages { get; set; } = new();
    public Dictionary<string, LobbyPlayer> Players { get; set; } = new();
    public LobbyState State { get; set; } = LobbyState.Waiting;
    public int CurrentRoundIndex { get; set; }
    public GameRound? CurrentRound { get; set; }
    public HashSet<string> CurrentRoundAnsweredPlayers { get; set; } = new();
    public int CurrentRoundAnswerCounter { get; set; } = 0; // Track order of submissions
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public int MessageLimit { get; set; } = 100;
    public int SecondsPerRound { get; set; } = 30;
}

public enum LobbyState
{
    Waiting,      // Waiting for players to join and ready
    GameStarting, // Players ready, game about to start
    RoundActive,  // Round in progress, players answering
    RoundEnded,   // Round finished, showing results
    GameEnded,    // All rounds complete
    Closed        // Lobby closed/expired
}

public class PlayerAnswer
{
    public string PlayerId { get; set; } = "";
    public string SelectedAuthorId { get; set; } = "";
    public int SubmittedAtMs { get; set; } // Milliseconds into the round
}

public class RoundResults
{
    public List<PlayerRoundResult> PlayerResults { get; set; } = new();
    public int RoundNumber { get; set; }
}

public class PlayerRoundResult
{
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public bool IsCorrect { get; set; }
    public double PointsEarned { get; set; }
    public int SubmittedAtMs { get; set; }
}

public class LeaderboardEntry
{
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Score { get; set; }
}
