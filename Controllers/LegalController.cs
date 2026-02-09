using Microsoft.AspNetCore.Mvc;

namespace DiscordGuessGame.Controllers;

[ApiController]
[Route("")]
public class LegalController : ControllerBase
{
    [HttpGet("terms")]
    public IActionResult Terms()
    {
        var html = @"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>ChatGame Terms of Service</title>
</head>
<body>
  <h1>ChatGame Terms of Service</h1>
  <p>Last updated: 2026-02-09</p>
  <p>By using ChatGame, you agree to these Terms of Service.</p>

  <h2>Use of the Service</h2>
  <ul>
    <li>You must follow Discord's Terms of Service and Community Guidelines.</li>
    <li>You are responsible for your activity and content while using the app.</li>
    <li>We may suspend access if the service is abused or used in violation of Discord policies.</li>
  </ul>

  <h2>Availability</h2>
  <p>The service is provided as-is and may change or be unavailable at any time.</p>

  <h2>Contact</h2>
  <p>Questions? Contact us at <a href=""mailto:moyumbnm@hotmail.com"">moyumbnm@hotmail.com</a>.</p>
</body>
</html>";

        return Content(html, "text/html");
    }

    [HttpGet("privacy")]
    public IActionResult Privacy()
    {
        var html = @"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>ChatGame Privacy Policy</title>
</head>
<body>
  <h1>ChatGame Privacy Policy</h1>
  <p>Last updated: 2026-02-09</p>

  <h2>Data We Process</h2>
  <ul>
    <li>Discord user ID and username for authentication.</li>
    <li>Discord guild and channel identifiers needed to operate the game.</li>
    <li>Message content only when required to run game logic.</li>
  </ul>

  <h2>How We Use Data</h2>
  <ul>
    <li>Provide and improve the game experience.</li>
    <li>Authenticate users and load their selected channels.</li>
  </ul>

  <h2>Data Retention</h2>
  <p>We do not intentionally store message content beyond the time needed to process a game session.</p>

  <h2>Contact</h2>
  <p>Questions? Contact us at <a href=""mailto:moyumbnm@hotmail.com"">moyumbnm@hotmail.com</a>.</p>
</body>
</html>";

        return Content(html, "text/html");
    }
}
