using Microsoft.AspNetCore.Authentication.Cookies;
using DiscordGuessGame.Services;
using DiscordGuessGame.Models;
using DiscordGuessGame.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables for Discord settings
var discordSettings = new DiscordSettings
{
    ClientId = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID") ??
               builder.Configuration["Discord:ClientId"] ?? "",
    ClientSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET") ??
                   builder.Configuration["Discord:ClientSecret"] ?? "",
    BotToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ??
               builder.Configuration["Discord:BotToken"] ?? "",
    RedirectUri = builder.Configuration["Discord:RedirectUri"] ??
                  "https://discord.api.sezginsahin.dk/api/auth/callback"
};

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Discord settings
builder.Services.Configure<DiscordSettings>(options =>
{
    options.ClientId = discordSettings.ClientId;
    options.ClientSecret = discordSettings.ClientSecret;
    options.BotToken = discordSettings.BotToken;
    options.RedirectUri = discordSettings.RedirectUri;
});

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/api/auth/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.Cookie.Name = "DiscordGameAuth";
        options.Cookie.HttpOnly = true;

        // Development: HTTP with SameSite=None (allows cross-origin)
        // Production: HTTPS with SameSite=Lax (more restrictive)
        if (builder.Environment.IsDevelopment())
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.None;
        }
        else
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Domain = ".sezginsahin.dk";
        }
    });

builder.Services.AddAuthorization();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://sezginsahin.dk")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Register services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddHostedService<LobbyCleanupService>();
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameLobbyHub>("/hubs/game-lobby");

// Initialize Discord bot
var botService = app.Services.GetRequiredService<DiscordBotService>();
await botService.StartAsync();

app.Run();
