# Discord Guess Game API

A .NET 8 Web API that allows users to sign in with Discord and play a guessing game based on messages from Discord server channels.

## Features

- **Discord OAuth Sign-In**: Users authenticate using their Discord account
- **Bot Integration**: Reads messages from Discord server channels (requires bot setup)
- **Guessing Game**: Players guess who wrote a random message from the channel
- **RESTful API**: All functionality exposed via API endpoints

## Important Limitations

⚠️ **Message Access Restrictions:**

- **Private DMs**: Cannot be accessed via Discord API (even with OAuth)
- **Server Messages**: Requires a Discord bot added to the server with appropriate permissions
- **OAuth Scope**: User OAuth only provides identity and guild list, NOT message history

## Prerequisites

- .NET 8 SDK
- Discord Developer Application (for OAuth and Bot)
- Discord Server where you have permissions to add a bot

## Setup Instructions

### 1. Create Discord Application

1. Go to [Discord Developer Portal](https://discord.com/developers/applications)
2. Click **New Application** and give it a name
3. Note your **Application ID** (Client ID)

### 2. Configure OAuth2

1. In your Discord application, go to **OAuth2 > General**
2. Add a redirect URL: `https://localhost:7000/api/auth/callback`
3. Copy your **Client Secret**

### 3. Create and Configure Bot

1. Go to **Bot** section in your Discord application
2. Click **Add Bot**
3. Under **Privileged Gateway Intents**, enable:
   - ✅ Message Content Intent
   - ✅ Server Members Intent (optional)
4. Copy your **Bot Token** (click Reset Token if needed)

### 4. Invite Bot to Your Server

1. Go to **OAuth2 > URL Generator**
2. Select scopes:
   - ✅ `bot`
3. Select bot permissions:
   - ✅ Read Messages/View Channels
   - ✅ Read Message History
4. Copy the generated URL and open it in a browser
5. Select your server and authorize the bot

### 5. Configure the Application

Edit `appsettings.json`:

```json
{
  "Discord": {
    "ClientId": "YOUR_APPLICATION_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "BotToken": "YOUR_BOT_TOKEN",
    "RedirectUri": "https://localhost:7000/api/auth/callback"
  }
}
```

### 6. Run the Application

```bash
dotnet restore
dotnet run
```

The API will be available at `https://localhost:7000`

## API Endpoints

### Authentication

#### `GET /api/auth/login`

Returns the Discord OAuth URL to initiate sign-in.

**Response:**

```json
{
  "authUrl": "https://discord.com/api/oauth2/authorize?..."
}
```

#### `GET /api/auth/callback?code=...`

OAuth callback endpoint (called by Discord after user authorizes).

**Response:**

```json
{
  "message": "Successfully authenticated",
  "user": {
    "id": "123456789",
    "username": "username",
    "discriminator": "1234"
  }
}
```

#### `GET /api/auth/me`

Get current authenticated user.

**Response:**

```json
{
  "id": "123456789",
  "username": "username"
}
```

#### `POST /api/auth/logout`

Sign out the current user.

### Discord Integration

#### `GET /api/discord/guilds`

Get list of servers where the bot is present.

**Headers:** `Cookie` (from auth)

**Response:**

```json
[
  {
    "id": 987654321,
    "name": "My Server"
  }
]
```

#### `GET /api/discord/guilds/{guildId}/channels`

Get text channels in a specific server.

**Headers:** `Cookie` (from auth)

**Response:**

```json
[
  {
    "id": 123456789,
    "name": "general"
  }
]
```

#### `POST /api/discord/load-messages`

Load messages from a channel into the game.

**Headers:** `Cookie` (from auth)

**Request Body:**

```json
{
  "guildId": 987654321,
  "channelId": 123456789,
  "limit": 100
}
```

**Response:**

```json
{
  "message": "Messages loaded successfully",
  "totalMessages": 95,
  "uniqueAuthors": 12
}
```

### Game

#### `GET /api/game/status`

Check if enough messages are loaded to play.

**Headers:** `Cookie` (from auth)

**Response:**

```json
{
  "messagesLoaded": 95,
  "uniqueAuthors": 12,
  "ready": true
}
```

#### `POST /api/game/round`

Create a new game round with a random message.

**Headers:** `Cookie` (from auth)

**Request Body:**

```json
{
  "numberOfOptions": 4
}
```

**Response:**

```json
{
  "roundId": "uuid-here",
  "message": "This is the message content",
  "options": [
    {
      "authorId": "123",
      "authorName": "User1"
    },
    {
      "authorId": "456",
      "authorName": "User2"
    }
  ],
  "createdAt": "2026-02-07T12:00:00Z"
}
```

#### `POST /api/game/answer`

Submit an answer for a round.

**Headers:** `Cookie` (from auth)

**Request Body:**

```json
{
  "roundId": "uuid-here",
  "selectedAuthorId": "123"
}
```

**Response:**

```json
{
  "correct": true,
  "correctAuthorId": "123",
  "correctAuthorName": "User1"
}
```

## Usage Flow

1. **Sign In**
   - Call `GET /api/auth/login` to get OAuth URL
   - Redirect user to the URL
   - User authorizes and is redirected to `/api/auth/callback`
   - Save the session cookie

2. **Load Messages**
   - Call `GET /api/discord/guilds` to see available servers
   - Call `GET /api/discord/guilds/{id}/channels` to see channels
   - Call `POST /api/discord/load-messages` with chosen channel

3. **Play Game**
   - Check `GET /api/game/status` to ensure ready
   - Call `POST /api/game/round` to get a random message and options
   - User guesses which author wrote the message
   - Submit answer with `POST /api/game/answer`
   - Repeat!

## Development

### Swagger UI

When running in Development mode, Swagger UI is available at:
`https://localhost:7000/swagger`

### CORS

CORS is configured to allow all origins in development. Adjust in `Program.cs` for production.

## Security Notes

- **Never commit** `appsettings.Development.json` or any file with real tokens
- Store secrets in User Secrets or environment variables for production
- The bot token grants full bot access - keep it secure
- Use HTTPS in production
- Consider rate limiting for production deployments

## Troubleshooting

### Bot Can't See Messages

- Ensure **Message Content Intent** is enabled in Discord Developer Portal
- Verify bot has **Read Message History** permission in the channel
- Check that bot is actually in the server

### OAuth Redirect Fails

- Ensure redirect URI in `appsettings.json` matches Discord Developer Portal exactly
- Check that your app is running on the correct port

### No Guilds Returned

- Bot must be invited to servers before they appear
- User must be authenticated before calling guild endpoints

## License

MIT
