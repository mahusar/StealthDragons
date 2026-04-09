StealthDragons is a multiplayer PvP card game that runs through the Tor network and integrates the Stealth (XST) blockchain.
The cards are NFT node artworks, drawn randomly, and every player has the same number of cards with identical statistics to keep the battles fair.

### TOR network 
- All game traffic is routed through the **Tor network** via a hidden service (.onion address) - no central server, fully encrypted PvP
- Requires Tor running locally (port 9050) to connect to the server

### Stealth (XST)
- Stealth is a fast, feelesss, private, and scalable blockchain built on the Junaeth cryptocurrency protocol
- Stealth offer 5 second feeless transaction confirmations and unparalleled spam resistance
  
### Dragonator
- StealthDragons network runs on a dedicated Linux server called **Dragonator**, a headless Unity build
- Server IP is never revealed - protected by Tor hidden service
- All connections (matchmaking, version check, game traffic) go through Tor
- Players connect via .onion address directly in the game client
- Server player count is tracked live and shown before joining
 
 ### Setup Client
##### Windows
- Install Tor standalone via [Expert Bundle](https://www.torproject.org/download/tor/)
- Add torrc config file: SocksPort 9050 | ControlPort 9051 | DataDirectory tor_data | HiddenServiceDir C:\Tor\hidden\ | HiddenServiceVersion 3 | HiddenServicePort 7777 127.0.0.1:7777
- Run .\tor.exe -f torrc
- Launch StealthDragons.exe
- Set Player name (default is StealthDragon)
- Enter the Dragonator Server .onion address, connect to begin

 ### Setup Dragonator
- Requires a fully synced Stealth daemon v3.3.4.0
- Unlocked wallet for transactions
##### Setup TOR   
- sudo apt install tor
- sudo systemctl start tor
- sudo systemctl status tor
##### Check ports 
- ss -tlnp | grep 7780   # game port
- ss -tlnp | grep 5555   # matchmaker port
##### Find Address
- sudo cat /var/lib/tor/hidden_service/hostname
##### Add at the bottom
- sudo nano /etc/tor/torrc
- HiddenServiceDir /var/lib/tor/hidden_service/
- HiddenServicePort 7780 127.0.0.1:7780
- HiddenServicePort 5555 127.0.0.1:5555
- sudo systemctl restart tor
##### Add into StealthCoin.conf 
- rpcbind=127.0.0.1
- rpcallowip=127.0.0.1
##### Create rpc.config 
- cd ~/.config/unity3d/StealthDragons/StealthDragons
- nano ~/.config/unity3d/StealthDragons/StealthDragons/rpc.conf
- rpcuser=stealthuser
- rpcpassword=stealthpassword
- rpcurl=http://127.0.0.1:46502/
##### Start Dragonator
- chmod +x dragonator.x86_64
- ./dragonator.x86_64 -batchmode -nographics

### Development Environment
- Unity Engine 6000.0.71f1
- Mirror Networking 96.0.1 
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

![Alt text](Assets/Sprites/StealthDragonsMatch.png)
![Alt text](Assets/Sprites/StealthDragonsVictory.png)
PvP match with Stealth (XST) integration

https://www.youtube.com/watch?v=gPVLkC-fBXw

### Experimental Software Notice
StealthDragons and Dragonator are experimental software provided for testing and development purposes only.
Use this software at your own risk. No guarantees are made regarding stability, security, or reliability. Funds may be lost due to bugs, crashes, or unexpected behavior.

### No Gambling or Betting Service Disclaimer
This software is a peer-to-peer experimental game integration and does not operate, provide, or promote any gambling or betting service.
The developer does not host, manage, or control user funds beyond the automated blockchain transaction functionality built into the software.
Users are fully responsible for how they use this software and must comply with their local laws and regulations.
