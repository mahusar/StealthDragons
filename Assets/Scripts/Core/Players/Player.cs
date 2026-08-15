using System;
using UnityEngine;
using Mirror;
using System.Collections;

public enum PlayerType { PLAYER, ENEMY };

[RequireComponent(typeof(Deck))]
[Serializable]
public class Player : Entity
{
    [Header("Player Info")]
    [SyncVar(hook = nameof(UpdatePlayerName))] public string username;
    [SyncVar, HideInInspector] public string publicKey = "";

    [Header("Portrait")]
    public Sprite portrait;

    [Header("Deck")]
    public Deck deck;
    public Sprite cardback;
    [SyncVar(hook = nameof(OnTauntCountChanged))]
    public int tauntCount = 0;

    [SyncVar(hook = nameof(OnHandCountChanged))] public int handCount = 0;
    [SyncVar] public int deckCount = 0;
    [SyncVar] public int graveCount = 0;

    [Header("Stats")]
    [SyncVar] public int maxMana = 10;
    [SyncVar] public int currentMax = 0;
    [SyncVar] public int _mana = 0;
    public int mana
    {
        get { return Mathf.Min(_mana, maxMana); }
        set { _mana = Mathf.Clamp(value, 0, maxMana); }
    }

    [HideInInspector] public static Player localPlayer;
    [HideInInspector] public bool hasEnemy = false;
    [HideInInspector] public PlayerInfo enemyInfo;
    [HideInInspector] public static GameManager gameManager;
    [SyncVar, HideInInspector] public bool firstPlayer = false;

    private void OnTauntCountChanged(int oldCount, int newCount)
    {
        tauntCount = newCount;
        Debug.Log($"Player {username}: tauntCount changed to {tauntCount}");
    }

    private void OnHandCountChanged(int oldCount, int newCount)
    {
        if (!isClient || localPlayer == null || gameManager == null || gameManager.enemyHand == null)
            return;

        if (isLocalPlayer) return;

        if (!localPlayer.hasEnemy)
        {
            StartCoroutine(WaitForEnemyThenUpdateHand());
            return;
        }

        gameManager.enemyHand.UpdateHandCards();
    }

    private IEnumerator WaitForEnemyThenUpdateHand()
    {
        while (localPlayer == null)
            yield return null;

        while (!localPlayer.hasEnemy)
        {
            localPlayer.UpdateEnemyInfo();
            yield return new WaitForSeconds(0.5f);
        }

        if (gameManager != null && gameManager.enemyHand != null)
            gameManager.enemyHand.UpdateHandCards();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Player[] onlinePlayers = FindObjectsOfType<Player>();
        if (onlinePlayers.Length == 1)
        {
            firstPlayer = true;
            Debug.Log($"Player: Set {username} as first player on server.");
        }
    }

    public override void OnStartLocalPlayer()
    {
        localPlayer = this;
        Debug.Log($"Player: Local player set for {username}, firstPlayer: {firstPlayer}");
        CmdLoadPlayer(PlayerName.Resolve(), PlayerIdentity.Mine.PublicKeyHex);

        if (firstPlayer)
        {
            StartCoroutine(StartGameAfterDelay());
        }
    }

    private IEnumerator StartGameAfterDelay()
    {
        Debug.Log($"Player {username}: Waiting for enemy to connect...");
        while (!hasEnemy)
        {
            UpdateEnemyInfo();
            yield return new WaitForSeconds(1f);
        }

        Debug.Log($"Player {username}: Enemy found, waiting for bet validation...");

        DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();
        if (wallet == null)
        {
            Debug.LogError($"Player {username}: DragonatorWallet not found!");
            yield break;
        }

        while (!wallet.BothPlayersValidated())
            yield return new WaitForSeconds(1f);

        Debug.Log($"Player {username}: Bets validated, starting game...");
        CmdStartGame();
    }

    [Command]
    private void CmdStartGame()
    {
        Debug.Log($"Player {username}: CmdStartGame called on server.");
        gameManager.StartGameForPlayer(netIdentity);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"Player: OnStartClient called for {username}");
    }

    [Command]
    public void CmdLoadPlayer(string user, string publicKeyHex)
    {
        username = PlayerName.Sanitize(user);

        if (CardShuffle.FromHex(publicKeyHex) != null
            && publicKeyHex.Length == PlayerIdentity.PublicKeyBytes * 2)
        {
            publicKey = publicKeyHex;
        }
        else
        {
            publicKey = "";
            Debug.LogWarning($"Player {username}: sent no usable identity key - this match cannot produce a signed receipt.");
        }
    }

    [Command]
    public void CmdRequestAttack(uint attackerNetId, uint targetNetId)
    {
        ServerRequestAttack(attackerNetId, targetNetId);
    }

    [Server]
    public void ServerRequestAttack(uint attackerNetId, uint targetNetId)
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null || !gameManager.IsTurnOf(this))
        {
            Debug.LogWarning($"CmdRequestAttack rejected: not {username}'s turn.");
            return;
        }

        if (!NetworkServer.spawned.TryGetValue(attackerNetId, out NetworkIdentity attackerId) ||
            !NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetId))
        {
            Debug.LogWarning($"CmdRequestAttack rejected: attacker {attackerNetId} or target {targetNetId} not spawned.");
            return;
        }

        FieldCard attacker = attackerId.GetComponent<FieldCard>();
        Entity target = targetId.GetComponent<Entity>();

        if (attacker == null || target == null)
        {
            Debug.LogWarning("CmdRequestAttack rejected: attacker must be a FieldCard and target an Entity.");
            return;
        }

        if (attacker.owner != this)
        {
            Debug.LogWarning($"CmdRequestAttack rejected: {username} does not own {attacker.gameObject.name}.");
            return;
        }

        if (attacker.waitTurn > 0)
        {
            Debug.LogWarning($"CmdRequestAttack rejected: {attacker.gameObject.name} still has waitTurn {attacker.waitTurn}.");
            return;
        }

        if (attacker.hasAttackedThisTurn)
        {
            Debug.LogWarning($"CmdRequestAttack rejected: {attacker.gameObject.name} already attacked this turn.");
            return;
        }

        if (attacker.health <= 0 || target.health <= 0 || !target.isTargetable)
        {
            Debug.LogWarning("CmdRequestAttack rejected: attacker or target is dead or untargetable.");
            return;
        }

        Player defender = target is Player targetPlayer ? targetPlayer : target.owner;
        if (defender == null || defender == this)
        {
            Debug.LogWarning($"CmdRequestAttack rejected: {username} cannot attack their own side.");
            return;
        }

        if (defender.tauntCount > 0 && !(target is FieldCard targetCard && targetCard.taunt))
        {
            Debug.LogWarning($"CmdRequestAttack rejected: {defender.username} has {defender.tauntCount} taunt creature(s); target must be one of them.");
            return;
        }

        attacker.hasAttackedThisTurn = true;
        attacker.combat.ServerResolveAttack(attacker.gameObject, target.gameObject);
    }

    void UpdatePlayerName(string oldUser, string newUser)
    {
        username = newUser;
        gameObject.name = newUser;
    }

    private void Start()
    {
        gameManager = FindObjectOfType<GameManager>();
        health = gameManager.maxHealth;
        maxMana = gameManager.maxMana;
        deck.deckSize = gameManager.deckSize;
        deck.handSize = gameManager.handSize;
    }

    public override void Update()
    {
        base.Update();

        if (!hasEnemy && username != "")
        {
            UpdateEnemyInfo();
        }
    }

    public void UpdateEnemyInfo()
    {
        Player[] onlinePlayers = FindObjectsOfType<Player>();
        foreach (Player players in onlinePlayers)
        {
            if (players.username != "" && players != this)
            {
                PlayerInfo currentPlayer = new PlayerInfo(players.gameObject);
                enemyInfo = currentPlayer;
                hasEnemy = true;
                enemyInfo.data.casterType = Target.OPPONENT;
                Debug.Log($"Player {username}: Enemy set to {enemyInfo.username}, tauntCount: {enemyInfo.tauntCount}");
            }
        }
    }

    public bool IsOurTurn() => gameManager.isOurTurn;
}
