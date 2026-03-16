StealthDragons is a multiplayer PvP card game, featuring a duel-based game mode where players face off.
The cards are NFT node artworks, drawn randomly, and every player has the same number of cards with identical statistics to keep the battles fair.

### TOR network 
- All game traffic is routed through the **Tor network** via a hidden service (.onion address) - no central server, fully encrypted PvP
- Requires Tor running locally (port 9050) to connect to the server
  
### Dragonator Dedicated Server
- StealthDragons now runs on a dedicated Linux server called **Dragonator**, a headless Unity build
- Server IP is never revealed - protected by Tor hidden service
- All connections (matchmaking, version check, game traffic) go through Tor
- Players connect via .onion address directly in the game client
- Server player count is tracked live and shown before joining

### Development Environment
- Unity Engine 2021.3.45f2
- Mirror Networking 86.12.2
- TextMesh Pro
- DOTween
### Network components
- MatchMakerServer
- DragonNetworkManager
- TorTelepathyTransport
- UnityMainThreadDispatcher

### Builds
- Builds are available for Windows, Linux, and macOS

![Alt text](Assets/Sprites/StealthDragonsTor.png)
Tor connection

![Alt text](Assets/Sprites/StealthDragons_03a.png)
PvP match

https://www.youtube.com/watch?v=gPVLkC-fBXw
