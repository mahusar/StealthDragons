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

    [Header("Portrait")]
    public Sprite portrait;

    [Header("Deck")]
    public Deck deck;
    public Sprite cardback;
    [SyncVar(hook = nameof(OnTauntCountChanged))]
    public int tauntCount = 0;

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

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Assign firstPlayer to the first connected player
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
        CmdLoadPlayer(PlayerPrefs.GetString("Name"));

        // If this is the first player, wait for enemy and start the game
        if (firstPlayer)
        {
            StartCoroutine(StartGameAfterDelay());
        }
    }

    private IEnumerator StartGameAfterDelay()
    {
        // Wait for an enemy to connect
        Debug.Log($"Player {username}: Waiting for enemy to connect...");
        while (!hasEnemy)
        {
            UpdateEnemyInfo();
            yield return new WaitForSeconds(1f);
        }

        Debug.Log($"Player {username}: Enemy found, waiting 3 seconds to start game...");
        yield return new WaitForSeconds(3f);

        Debug.Log($"Player {username}: Commanding game start...");
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
    public void CmdLoadPlayer(string user)
    {
        username = user;
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