StealthDragons is a multiplayer PvP card game that runs through the Tor network and integrates the Stealth (XST) blockchain.
The cards are NFT node artworks. Each player builds their own deck from a shared pool, so no two decks need be alike, and the battles are kept fair by proof rather than by everyone playing the same cards.

### TOR network 
- All game traffic is routed through the **Tor network** via a hidden service (.onion address) - no central server, fully encrypted PvP
- Tor ships with the client and starts by itself, so there is nothing to install or configure

### Stealth (XST)
- Stealth is a fast, feelesss, private, and scalable blockchain built on the Junaeth cryptocurrency protocol
- Stealth offer 5 second feeless transaction confirmations and unparalleled spam resistance
  
### Dragonator
- StealthDragons network runs on a dedicated Linux server called **Dragonator**, a headless Unity build
- Server IP is never revealed - protected by Tor hidden service
- All connections (matchmaking, version check, game traffic) go through Tor
- Players connect via .onion address directly in the game client
- Server player count is tracked live and shown before joining

### Deckbuilding
- 47 cards in the pool. A deck is 40 cards with at most 2 copies of any one card, and both seats build under the same two rules
- Build it in the DECK panel on the menu. Point at a row and the card itself is shown
- Creatures carry keywords such as charge, taunt, shield, lifesteal and deathrattle, some carry a battlecry, and some reward a tribe
- A bot brings its own decklist too, so an add-on bot's deck is on the record like any other seat's

### Provably fair matches
- The server commits to the shuffle before a single card is dealt, and every player mixes in their own seed
- Each seat's decklist is committed before a single card is dealt and revealed at the end, so which deck won is something a third party can check
- The seed is revealed at the end, so each client recomputes the whole deal and checks its own hand and the opponent's
- Every completed match produces a receipt that both players sign, with keys the server never holds, so a result cannot be invented or altered afterwards
- A client that cannot verify the deal refuses to sign, so a signed receipt means both sides checked it and agreed
- Publishing those receipts on the Stealth chain, so a match can also be proved to have happened by a certain time, is an optional server add-on

### Practice mode
- Play the AI bot with no Tor and no server, straight from the menu
- The bot builds its own deck from the same pool and plays under the same rules, so practice is a real match

### Match replays
- A match is recorded as its seed and its moves rather than as snapshots, so a whole match is a short digest and a move list
- Matches you played are kept on your machine and listed in the REPLAY panel. Pick one from the list, or paste its 64-character digest, and watch it back move by move
- Playback rebuilds both decks from the record, and refuses to play anything that does not match the digest asked for

### Server add-ons
- A stock Dragonator hosts matches and does nothing else. Anything beyond that is an optional add-on the operator drops into an Addons folder, and the connect screen lists what a server has loaded before you join
- Add-ons are developed separately in https://github.com/mahusar/dragonator-addons, which also carries the protocol a third-party bot dials in over
 
 ### Setup Client
##### Windows
- Launch StealthDragons.exe
- Set Player name (default is StealthDragon)
- Enter the Dragonator Server .onion address, connect to begin

**Tor is included and starts on its own.** The first connection takes about ten
seconds while it builds a circuit, and the progress is shown on the connect
screen. Nothing to install and no torrc to write.

- Practice against the AI needs no Tor at all, so you can play straight away
- If you already run Tor on port 9050, the game uses that instead of starting its own
- The bundled Tor is The Tor Project's, unmodified, run as a separate process - see `StreamingAssets/Tor/NOTICE.txt` in the build

 ### Setup Dragonator
- Requires a fully synced Stealth daemon v3.3.4.0
- Unlocked wallet for transactions
##### Setup TOR   
- sudo apt install tor
- sudo systemctl start tor
- sudo systemctl status tor
##### Add at the bottom
- sudo nano /etc/tor/torrc
- HiddenServiceDir /var/lib/tor/hidden_service/
- HiddenServicePort 7780 127.0.0.1:7780
- HiddenServicePort 5555 127.0.0.1:5555
- sudo systemctl restart tor
##### Add into StealthCoin.conf 
- cd ~/.StealthCoin
- nano StealthCoin.conf
- rpcbind=127.0.0.1
- rpcallowip=127.0.0.1
##### Start Dragonator
- chmod +x dragonator.x86_64
- ./dragonator.x86_64 -batchmode -nographics
- stop dragonator
##### Create rpc.config 
- cd ~/.config/unity3d/StealthDragons/StealthDragons
- nano ~/.config/unity3d/StealthDragons/StealthDragons/rpc.conf
- rpcuser=stealthuser
- rpcpassword=stealthpassword
- rpcurl=http://127.0.0.1:46502/
##### Find Address
- sudo cat /var/lib/tor/hidden_service/hostname
##### Start Dragonator
- chmod +x dragonator.x86_64
- ./dragonator.x86_64 -batchmode -nographics
##### Check ports 
- ss -tlnp | grep 7780   # game port
- ss -tlnp | grep 5555   # matchmaker port
##### Start Dragonator
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

![Deck builder and connect screen](screens/StealthDeck.png)
Building a 40 card deck

![Practice match against the bot](screens/StealthPractice.png)
A match against the bot in practice mode

![Replay playback](screens/StealthReplay.png)
Watching a recorded match back

###### Experimental Software Notice
StealthDragons and Dragonator are experimental software provided for testing and development purposes only.
Use this software at your own risk. No guarantees are made regarding stability, security, or reliability. Funds may be lost due to bugs, crashes, or unexpected behavior.

###### No Gambling or Betting Service Disclaimer
A stock StealthDragons build cannot take a stake or send a payout, and contains no wallet operations of any kind. Bets require a separate server add-on that an operator installs deliberately, and that add-on is not part of this software.
The developer operates no server, does not host or manage user funds, and does not provide or promote any gambling or betting service.
Users are fully responsible for how they use this software and must comply with their local laws and regulations.

###### No Public Server
The author writes this software and operates no public server. Any server claiming to be operated by the author is not.

###### Source and Licensing
Copyright (C) 2026 Martin Husar. No license is granted. The source is published for review and verification only, not for reuse, modification or redistribution.
Unity, Mirror, TextMesh Pro, DOTween and other third-party components remain under their own licenses.
