using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class AIBot : MonoBehaviour
{
    [Header("Pacing")]
    [Tooltip("Seconds the bot waits before its first action, so the player can read the board.")]
    public float thinkDelay = 1.2f;

    [Tooltip("Seconds between cards the bot plays.")]
    public float playDelay = 0.9f;

    [Tooltip("Seconds between bot attacks. Must exceed the full attack animation or actions overlap.")]
    public float attackDelay = 1.6f;

    [Tooltip("Hard cap on actions per turn. Guards against a policy bug looping forever.")]
    public int maxActionsPerTurn = 24;

    private Player self;
    private GameManager gameManager;
    private bool takingTurn;
    private uint lastTurnSeen = uint.MaxValue;

    public void ServerInitialize(Player botPlayer)
    {
        self = botPlayer;
        Debug.Log($"AIBot: driving {self.username} (netId {self.netId}).");
    }

    void Update()
    {
        if (!NetworkServer.active || self == null) return;

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null) return;
        }

        uint turn = gameManager.currentTurnNetId;
        if (turn == lastTurnSeen) return;
        lastTurnSeen = turn;

        if (turn == self.netId && !takingTurn)
            StartCoroutine(TakeTurn());
    }

    private IEnumerator TakeTurn()
    {
        takingTurn = true;
        Debug.Log($"AIBot: {self.username} starting turn {gameManager.turnCount} with {self.mana} mana and {self.deck.hand.Count} cards.");

        yield return new WaitForSeconds(thinkDelay);

        int actions = 0;

        while (actions < maxActionsPerTurn && StillOurTurn() && PlayBestAffordableCreature())
        {
            actions++;
            yield return new WaitForSeconds(playDelay);
        }

        while (actions < maxActionsPerTurn && StillOurTurn() && AttackWithNextReadyCreature())
        {
            actions++;
            yield return new WaitForSeconds(attackDelay);
        }

        if (actions >= maxActionsPerTurn)
            Debug.LogWarning($"AIBot: {self.username} hit the {maxActionsPerTurn}-action cap; ending turn.");

        if (StillOurTurn())
        {
            Debug.Log($"AIBot: {self.username} ending turn after {actions} action(s).");
            gameManager.ServerEndTurn(self);
        }

        takingTurn = false;
    }

    private bool StillOurTurn() =>
        NetworkServer.active && self != null && self.health > 0 &&
        gameManager != null && gameManager.currentTurnNetId == self.netId;

    private bool PlayBestAffordableCreature()
    {
        int bestIndex = -1;
        int bestCost = -1;

        for (int i = 0; i < self.deck.hand.Count; i++)
        {
            CardInfo card = self.deck.hand[i];
            if (!(card.data is CreatureCard)) continue;

            int cost = card.data.cost;
            if (cost > self.mana) continue;

            if (cost > bestCost)
            {
                bestCost = cost;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return false;

        Debug.Log($"AIBot: {self.username} plays {self.deck.hand[bestIndex].name} for {bestCost} (mana {self.mana}).");
        self.deck.ServerPlayCard(bestIndex);
        return true;
    }

    private bool AttackWithNextReadyCreature()
    {
        Player opponent = FindOpponent();
        if (opponent == null) return false;

        FieldCard attacker = FindReadyAttacker();
        if (attacker == null) return false;

        Entity target = ChooseTarget(attacker, opponent);
        if (target == null) return false;

        Debug.Log($"AIBot: {self.username} attacks {target.gameObject.name} with {attacker.card.name} (str {attacker.strength}).");
        self.ServerRequestAttack(attacker.netId, target.netId);
        return true;
    }

    private Player FindOpponent()
    {
        foreach (Player p in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (p != self && p.health > 0) return p;
        return null;
    }

    private FieldCard FindReadyAttacker()
    {
        foreach (FieldCard card in Object.FindObjectsByType<FieldCard>(FindObjectsSortMode.None))
        {
            if (card.owner != self) continue;
            if (card.health <= 0) continue;
            if (card.waitTurn > 0) continue;
            if (card.hasAttackedThisTurn) continue;
            if (card.strength <= 0) continue;
            return card;
        }
        return null;
    }

    private Entity ChooseTarget(FieldCard attacker, Player opponent)
    {
        List<FieldCard> enemyCards = new List<FieldCard>();
        foreach (FieldCard card in Object.FindObjectsByType<FieldCard>(FindObjectsSortMode.None))
        {
            if (card.owner == self) continue;
            if (card.health <= 0) continue;
            if (!card.isTargetable) continue;
            enemyCards.Add(card);
        }

        if (opponent.tauntCount > 0)
        {
            FieldCard bestTaunt = null;
            foreach (FieldCard card in enemyCards)
            {
                if (!card.taunt) continue;
                if (bestTaunt == null || card.health < bestTaunt.health) bestTaunt = card;
            }

            if (bestTaunt != null) return bestTaunt;

            Debug.LogWarning($"AIBot: {opponent.username} reports tauntCount {opponent.tauntCount} but no taunt creature is on the board; skipping attack.");
            return null;
        }

        FieldCard freeKill = null;
        foreach (FieldCard card in enemyCards)
        {
            if (card.health > attacker.strength) continue;
            if (card.strength >= attacker.health) continue;
            if (freeKill == null || card.health > freeKill.health) freeKill = card;
        }

        if (freeKill != null) return freeKill;

        return opponent.isTargetable ? opponent : null;
    }
}
