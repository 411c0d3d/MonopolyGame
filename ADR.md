System Architecture Decision Record (ADR)

## Title: ASP.NET Core with SignalR for Real-Time Multiplayer Game Server

MonopolyServer/
├── src/
│   ├── MonopolyServer.Api/          # ASP.NET Core entry point
│   │   ├── Hubs/
│   │   │   └── GameHub.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── MonopolyServer.Game/         # Game engine, pure C#, no web dependencies
│   │   ├── Engine/
│   │   │   └── GameEngine.cs
│   │   ├── Models/
│   │   │   ├── GameState.cs
│   │   │   ├── Player.cs
│   │   │   ├── Board.cs
│   │   │   └── Property.cs
│   │   └── Services/
│   │       └── GameRoomManager.cs
│   └── MonopolyServer.Tests/
├── Dockerfile
├── docker-compose.yml               # local dev with Azure SignalR emulator or local SignalR
└── .github/workflows/deploy.yml     # build, push to ACR, deploy to Container Apps