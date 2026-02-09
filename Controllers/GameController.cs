using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DiscordGuessGame.Models;
using DiscordGuessGame.Services;

namespace DiscordGuessGame.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameController : ControllerBase
{
    private readonly GameService _gameService;
    private readonly ILogger<GameController> _logger;

    public GameController(GameService gameService, ILogger<GameController> logger)
    {
        _gameService = gameService;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        Console.WriteLine($"Checking game status - Messages: {_gameService.GetMessageCount()}, Unique Authors: {_gameService.GetUniqueAuthorCount()}");
        return Ok(new
        {
            messagesLoaded = _gameService.GetMessageCount(),
            uniqueAuthors = _gameService.GetUniqueAuthorCount(),
            ready = _gameService.GetMessageCount() >= 4 && _gameService.GetUniqueAuthorCount() >= 4
        });
    }

    [HttpPost("round")]
    public IActionResult CreateRound([FromBody] CreateRoundRequest? request)
    {
        var numberOfOptions = request?.NumberOfOptions ?? 4;

        if (numberOfOptions < 2 || numberOfOptions > 10)
        {
            return BadRequest("Number of options must be between 2 and 10");
        }

        var round = _gameService.CreateRound(numberOfOptions);

        if (round == null)
        {
            return BadRequest("Not enough messages or authors loaded. Load messages first.");
        }

        // Don't send the full message object with author info to avoid spoilers
        return Ok(new
        {
            roundId = round.Id,
            message = round.Message.Content,
            options = round.Options.Select(o => new
            {
                authorId = o.AuthorId,
                authorName = o.AuthorName
            }),
            createdAt = round.CreatedAt
        });
    }

    [HttpPost("answer")]
    public IActionResult SubmitAnswer([FromBody] GameAnswer answer)
    {
        if (string.IsNullOrEmpty(answer.RoundId) || string.IsNullOrEmpty(answer.SelectedAuthorId))
        {
            return BadRequest("RoundId and SelectedAuthorId are required");
        }

        var result = _gameService.SubmitAnswer(answer);

        if (result == null)
        {
            return NotFound("Round not found or expired");
        }

        return Ok(new
        {
            correct = result.IsCorrect,
            correctAuthorId = result.CorrectAuthorId,
            correctAuthorName = result.CorrectAuthorName
        });
    }
}

public class CreateRoundRequest
{
    public int NumberOfOptions { get; set; } = 4;
}
