# Quick Start Guide

## 1. Get Discord Credentials

### Create Application

1. Go to https://discord.com/developers/applications
2. Click "New Application"
3. Name it and create

### Get OAuth Credentials

1. Copy the **Application ID** (Client ID)
2. Go to OAuth2 > General
3. Copy the **Client Secret**
4. Add redirect: `https://localhost:7000/api/auth/callback`

### Create Bot

1. Go to Bot section
2. Click "Add Bot"
3. Enable **Message Content Intent** under Privileged Gateway Intents
4. Copy the **Bot Token**

### Invite Bot to Server

1. Go to OAuth2 > URL Generator
2. Select scopes: `bot`
3. Select permissions: "Read Messages/View Channels", "Read Message History"
4. Open generated URL and add bot to your server

## 2. Configure Application

Edit `appsettings.json`:

- Replace `YOUR_DISCORD_CLIENT_ID` with Application ID
- Replace `YOUR_DISCORD_CLIENT_SECRET` with Client Secret
- Replace `YOUR_DISCORD_BOT_TOKEN` with Bot Token

## 3. Run

```bash
dotnet run
```

Open browser to: https://localhost:7000/swagger

## 4. Test the Flow

1. **Authenticate**
   - GET /api/auth/login → Copy the authUrl
   - Open authUrl in browser and authorize
   - You'll be redirected back and authenticated

2. **Load Messages**
   - GET /api/discord/guilds → Find your server ID
   - GET /api/discord/guilds/{id}/channels → Find a channel ID
   - POST /api/discord/load-messages with guildId and channelId

3. **Play**
   - GET /api/game/status → Check if ready
   - POST /api/game/round → Get a message and options
   - POST /api/game/answer → Submit your guess

## Troubleshooting

**Bot can't see messages?**

- Enable Message Content Intent in Discord Developer Portal
- Make sure bot is in the server
- Check bot has Read Message History permission

**OAuth fails?**

- Check redirect URI matches exactly
- Make sure app is running on port 7000

**No guilds?**

- Bot must be added to servers first
- You must be authenticated
