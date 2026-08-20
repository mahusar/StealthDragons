using UnityEngine;
using Mirror;
using System.Collections;

public class Combat : NetworkBehaviour
{
    public Entity entity;

    private void Awake()
    {
        if (entity == null)
        {
            entity = GetComponent<Entity>();
            if (entity == null)
            {
                Debug.LogError($"Combat: Entity component missing on {gameObject.name}!");
            }
            else
            {
                Debug.Log($"Combat: Auto-assigned entity to {entity.gameObject.name} on {gameObject.name}");
            }
        }
    }

    [Server]
    public void ServerChangeHealth(int amount)
    {
        if (entity == null || entity.gameObject == null)
        {
            Debug.LogError($"ServerChangeHealth: Entity is null or destroyed on {gameObject.name}");
            return;
        }

        int oldHealth = entity.health;
        int newHealth = entity.health + amount;
        bool shouldDestroy = newHealth <= 0;
        entity.health = Mathf.Max(0, newHealth);
        Debug.Log($"ServerChangeHealth: Health changed by {amount} for {entity.gameObject.name} (netId: {entity.GetComponent<NetworkIdentity>()?.netId}). Old health: {oldHealth}, New health: {entity.health}");

        if (shouldDestroy)
        {
            Debug.Log($"ServerChangeHealth: {entity.gameObject.name} health <= 0. Initiating destruction.");
            if (entity is BoardCard fieldCard)
            {
                if (fieldCard.owner != null && fieldCard.owner.deck != null && !string.IsNullOrEmpty(fieldCard.card.name))
                {
                    if (fieldCard.taunt)
                    {
                        fieldCard.owner.tauntCount = Mathf.Max(0, fieldCard.owner.tauntCount - 1);
                        Debug.Log($"Player {fieldCard.owner.username}: Taunt creature destroyed. tauntCount: {fieldCard.owner.tauntCount}");
                    }

                    fieldCard.owner.deck.graveyard.Add(fieldCard.card);
                    fieldCard.owner.deck.playerField.Remove(fieldCard.card);
                    Debug.Log($"Card {fieldCard.card.name} destroyed. {fieldCard.owner.username}'s graveyard count: {fieldCard.owner.deck.graveyard.Count}");
                    StartCoroutine(DestroyCardAfterAnimation(fieldCard.gameObject));
                    BoardCard struck = Deathrattle.Resolve(fieldCard);
                    if (struck != null) RpcDeathrattle(struck.netId);
                }
                else
                {
                    Debug.LogError($"ServerChangeHealth: BoardCard {fieldCard.gameObject.name} has invalid owner: {fieldCard.owner}, deck: {fieldCard.owner?.deck}, or card name: {fieldCard.card.name}. Destroying directly.");
                    NetworkServer.Destroy(fieldCard.gameObject);
                }
            }
            else if (entity is Player player)
            {
                Debug.Log($"ServerChangeHealth: Player {player.username} defeated!");
                GameManager gameManager = FindFirstObjectByType<GameManager>();
                DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();

                if (gameManager != null)
                {
                    bool survivorFound = false;

                    Player[] onlinePlayers = FindObjectsByType<Player>(FindObjectsSortMode.None);
                    foreach (Player p in onlinePlayers)
                    {
                        if (p != player && p.health > 0)
                        {
                            survivorFound = true;
                            gameManager.ServerEndMatch(p, player, "defeat");

                            if (gameManager.practiceMode)
                            {
                                Debug.Log($"Practice match won by {p.username} - no stake was taken, so no payout is sent.");
                            }
                            else if (wallet != null)
                            {
                                foreach (var conn in NetworkServer.connections.Values)
                                {
                                    Player connPlayer = conn.identity != null ? conn.identity.GetComponent<Player>() : null;
                                    if (connPlayer == p)
                                    {
                                        wallet.PayWinner(conn);
                                        Debug.Log($"[DragonatorWallet] PayWinner called for {p.username}");
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogError("[DragonatorWallet] Wallet not found, cannot pay winner!");
                            }
                            break;
                        }
                    }

                    if (!survivorFound)
                    {
                        Debug.LogWarning($"ServerChangeHealth: {player.username} was defeated with nobody left standing - recording the loss with no winner.");
                        gameManager.ServerEndMatch(null, player, "defeat");
                    }
                }

                StartCoroutine(DestroyPlayerAfterDelay(entity.gameObject));
            }
        }
    }
    private IEnumerator DestroyPlayerAfterDelay(GameObject playerObj)
    {
        yield return new WaitForSeconds(0.5f);
        if (playerObj != null)
            NetworkServer.Destroy(playerObj);
    }

    [Server]
    public void ServerChangeMana(int amount)
    {
        if (entity == null)
        {
            Debug.LogError($"ServerChangeMana: Entity is null on {gameObject.name}");
            return;
        }
        if (entity is Player player)
        {
            player.mana += amount;
            Debug.Log($"ServerChangeMana: Mana changed by {amount} for {player.gameObject.name}. New mana: {player.mana}");
        }
        else
        {
            Debug.LogError($"ServerChangeMana: Entity is not a Player on {gameObject.name}");
        }
    }

    [Server]
    public void ServerChangeStrength(int amount)
    {
        if (entity == null)
        {
            Debug.LogError($"ServerChangeStrength: Entity is null on {gameObject.name}");
            return;
        }
        entity.strength += amount;
        Debug.Log($"ServerChangeStrength: Strength changed by {amount} for {entity.gameObject.name}. New strength: {entity.strength}");
    }

    [Server]
    public void ServerIncreaseWaitTurn()
    {
        if (entity == null)
        {
            Debug.LogError($"ServerIncreaseWaitTurn: Entity is null on {gameObject.name}");
            return;
        }
        entity.waitTurn++;
        Debug.Log($"ServerIncreaseWaitTurn: WaitTurn incremented for {entity.gameObject.name}. New waitTurn: {entity.waitTurn}");
    }

    [Server]
    public void ServerResolveAttack(GameObject attackerObj, GameObject targetObj)
    {
        if (attackerObj == null || targetObj == null)
        {
            Debug.LogError($"ServerResolveAttack: Attacker ({attackerObj}) or Target ({targetObj}) is null!");
            return;
        }

        if (!attackerObj.activeInHierarchy || !targetObj.activeInHierarchy)
        {
            Debug.LogError($"ServerResolveAttack: Attacker ({SafeName(attackerObj)}) or Target ({SafeName(targetObj)}) is inactive!");
            return;
        }

        NetworkIdentity attackerIdentity = attackerObj.GetComponent<NetworkIdentity>();
        NetworkIdentity targetIdentity = targetObj.GetComponent<NetworkIdentity>();
        if (attackerIdentity == null || targetIdentity == null)
        {
            Debug.LogError($"ServerResolveAttack: NetworkIdentity missing on Attacker ({SafeName(attackerObj)}) or Target ({SafeName(targetObj)})!");
            return;
        }

        Entity attackerEntity = attackerObj.GetComponent<Entity>();
        Entity targetEntity = targetObj.GetComponent<Entity>();
        if (attackerEntity == null || targetEntity == null)
        {
            Debug.LogError($"ServerResolveAttack: Entity component missing. Attacker: {attackerEntity}, Target: {targetEntity}");
            return;
        }

        int attackerStrength = Mathf.Max(0, attackerEntity.strength);
        int targetStrength = Mathf.Max(0, targetEntity.strength);

        Debug.Log($"ServerResolveAttack: {attackerObj.name} (strength: {attackerStrength}) attacks {targetObj.name} (strength: {targetStrength})");

        RpcAnimateAttack(attackerIdentity.netId, targetIdentity.netId);

        Combat attackerCombat = attackerObj.GetComponent<Combat>();
        Combat targetCombat = targetObj.GetComponent<Combat>();
        if (attackerCombat == null || targetCombat == null)
        {
            Debug.LogError($"ServerResolveAttack: Combat component missing. Attacker: {attackerCombat}, Target: {targetCombat}");
            return;
        }

        if (attackerCombat.entity == null) attackerCombat.entity = attackerEntity;
        if (targetCombat.entity == null) targetCombat.entity = targetEntity;

        int dealtToTarget = ServerApplyDamage(targetCombat, targetEntity, attackerStrength);
        int dealtToAttacker = ServerApplyDamage(attackerCombat, attackerEntity, targetStrength);

        ServerDrainLife(attackerEntity, dealtToTarget);
        ServerDrainLife(targetEntity, dealtToAttacker);
    }

    [Server]
    public static int ServerDealDamage(Entity target, int amount)
    {
        if (target == null || amount <= 0) return 0;

        Combat combat = target.GetComponent<Combat>();
        if (combat == null)
        {
            Debug.LogError($"ServerDealDamage: {target.name} has no Combat component, so {amount} damage was dropped.");
            return 0;
        }

        if (combat.entity == null) combat.entity = target;

        return ServerApplyDamage(combat, target, amount);
    }

    [Server]
    private static int ServerApplyDamage(Combat combat, Entity entity, int amount)
    {
        if (combat == null || entity == null || amount <= 0) return 0;

        if (entity.shielded)
        {
            entity.shielded = false;
            Debug.Log($"ServerResolveAttack: {entity.name} absorbed {amount} with its shield.");
            return 0;
        }

        combat.ServerChangeHealth(-amount);
        return amount;
    }

    [Server]
    private static void ServerDrainLife(Entity dealer, int damage)
    {
        if (dealer == null || damage <= 0) return;
        if (!HasLifesteal(dealer)) return;

        Player owner = dealer as Player;
        if (owner == null) owner = dealer.owner;
        if (owner == null) return;

        GameManager manager = FindFirstObjectByType<GameManager>();
        int ceiling = manager != null ? manager.maxHealth : owner.health + damage;

        int healed = Mathf.Min(ceiling, owner.health + damage) - owner.health;
        if (healed <= 0) return;

        owner.health += healed;
        Debug.Log($"ServerResolveAttack: {dealer.name} drained {healed} back to {owner.username}.");
    }

    private static bool HasLifesteal(Entity entity)
    {
        BoardCard board = entity as BoardCard;
        if (board == null || !board.card.Known) return false;

        CreatureCard creature = board.card.data as CreatureCard;
        return creature != null && creature.hasLifesteal;
    }

    private static GameObject FindSpawnedObject(uint netId)
    {
        if (!NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity)) return null;
        if (identity == null) return null;
        return identity.gameObject;
    }

    private static string SafeName(GameObject go)
    {
        return go == null ? "<destroyed>" : go.name;
    }

    [ClientRpc]
    void RpcAnimateAttack(uint attackerNetId, uint targetNetId)
    {
        GameObject attackerObj = FindSpawnedObject(attackerNetId);
        GameObject targetObj = FindSpawnedObject(targetNetId);

        if (attackerObj == null || targetObj == null)
        {
            Debug.LogError($"RpcAnimateAttack: Attacker (netId: {attackerNetId}) or Target (netId: {targetNetId}) not found! Spawned netIds: {string.Join(", ", NetworkClient.spawned.Keys)}");
            return;
        }

        Debug.Log($"RpcAnimateAttack: Playing animation for {attackerObj.name} attacking {targetObj.name}");

        CardAnimator animator = attackerObj.GetComponent<CardAnimator>();
        if (animator != null)
        {
            animator.AnimateAttack(attackerObj.transform, targetObj.transform, null);
        }
        else
        {
            Debug.LogWarning($"RpcAnimateAttack: CardAnimator not found on {attackerObj.name}");
        }
    }

    [ClientRpc]
    void RpcDeathrattle(uint targetNetId)
    {
        GameObject targetObject = FindSpawnedObject(targetNetId);
        if (targetObject == null) return;

        BoardCard struck = targetObject.GetComponent<BoardCard>();
        if (struck == null) return;

        struck.ShowDeathrattle();
    }

    private IEnumerator DestroyCardAfterAnimation(GameObject cardObject)
    {
        if (cardObject == null)
        {
            Debug.LogError("DestroyCardAfterAnimation: Card object is null!");
            yield break;
        }

        NetworkIdentity cardIdentity = cardObject.GetComponent<NetworkIdentity>();
        if (cardIdentity == null)
        {
            Debug.LogError($"DestroyCardAfterAnimation: NetworkIdentity missing on {cardObject.name}!");
            yield break;
        }

        CardAnimator animator = cardObject.GetComponent<CardAnimator>();
        float animationDuration = animator != null
            ? animator.moveDuration + animator.attackPause + animator.returnDuration + 0.05f
            : 1.05f;

        yield return new WaitForSeconds(animationDuration);

        if (cardObject == null) yield break;

        if (!cardObject.activeInHierarchy)
        {
            Debug.LogWarning($"DestroyCardAfterAnimation: {cardObject.name} already inactive, skipping destruction.");
            yield break;
        }

        Debug.Log($"DestroyCardAfterAnimation: Destroying {cardObject.name} with netId {cardIdentity.netId}");
        DG.Tweening.DOTween.Kill(cardObject.transform);
        RpcDestroyCard(cardIdentity.netId);
        NetworkServer.Destroy(cardObject);
    }

    [ClientRpc]
    void RpcDestroyCard(uint cardNetId)
    {
        GameObject cardObject = FindSpawnedObject(cardNetId);
        if (cardObject == null)
        {
            Debug.LogWarning($"RpcDestroyCard: Card with netId {cardNetId} not found or already destroyed. Spawned netIds: {string.Join(", ", NetworkClient.spawned.Keys)}");
            return;
        }

        Debug.Log($"RpcDestroyCard: Deactivating {cardObject.name} on client");

        DG.Tweening.DOTween.Kill(cardObject.transform);

        if (Player.gameManager != null)
        {
            cardObject.transform.SetParent(null);
            cardObject.SetActive(false);
        }
    }
}
