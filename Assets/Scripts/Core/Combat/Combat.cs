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

    [Command(requiresAuthority = false)]
    public void CmdChangeHealth(int amount)
    {
        if (entity == null || entity.gameObject == null)
        {
            Debug.LogError($"CmdChangeHealth: Entity is null or destroyed on {gameObject.name}");
            return;
        }

        int oldHealth = entity.health;
        int newHealth = entity.health + amount;
        bool shouldDestroy = newHealth <= 0;
        entity.health = Mathf.Max(0, newHealth);
        Debug.Log($"CmdChangeHealth: Health changed by {amount} for {entity.gameObject.name} (netId: {entity.GetComponent<NetworkIdentity>()?.netId}). Old health: {oldHealth}, New health: {entity.health}");

        if (shouldDestroy && isServer)
        {
            Debug.Log($"CmdChangeHealth: {entity.gameObject.name} health <= 0. Initiating destruction.");
            if (entity is FieldCard fieldCard)
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
                }
                else
                {
                    Debug.LogError($"CmdChangeHealth: FieldCard {fieldCard.gameObject.name} has invalid owner: {fieldCard.owner}, deck: {fieldCard.owner?.deck}, or card name: {fieldCard.card.name}. Destroying directly.");
                    NetworkServer.Destroy(fieldCard.gameObject);
                }
            }
            else if (entity is Player player)
            {
                Debug.Log($"CmdChangeHealth: Player {player.username} defeated!");
                GameManager gameManager = FindObjectOfType<GameManager>();
                if (gameManager != null)
                {
                    gameManager.RecordGameOutcome(player, false); // Record loser
                    Player[] onlinePlayers = FindObjectsOfType<Player>();
                    foreach (Player p in onlinePlayers)
                    {
                        if (p != player && p.health > 0)
                        {
                            gameManager.RecordGameOutcome(p, true); // Record winner
                            break;
                        }
                    }
                }
                NetworkServer.Destroy(entity.gameObject);
            }
            else
            {
                Debug.Log($"Destroying non-FieldCard entity: {entity.gameObject.name}");
                NetworkServer.Destroy(entity.gameObject);
            }
        }
    }

    // Existing methods (CmdChangeMana, CmdChangeStrength, etc.) remain unchanged
    [Command(requiresAuthority = false)]
    public void CmdChangeMana(int amount)
    {
        if (entity == null)
        {
            Debug.LogError($"CmdChangeMana: Entity is null on {gameObject.name}");
            return;
        }
        if (entity is Player player)
        {
            player.mana += amount;
            Debug.Log($"CmdChangeMana: Mana changed by {amount} for {player.gameObject.name}. New mana: {player.mana}");
        }
        else
        {
            Debug.LogError($"CmdChangeMana: Entity is not a Player on {gameObject.name}");
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdChangeStrength(int amount)
    {
        if (entity == null)
        {
            Debug.LogError($"CmdChangeStrength: Entity is null on {gameObject.name}");
            return;
        }
        entity.strength += amount;
        Debug.Log($"CmdChangeStrength: Strength changed by {amount} for {entity.gameObject.name}. New strength: {entity.strength}");
    }

    [Command(requiresAuthority = false)]
    public void CmdIncreaseWaitTurn()
    {
        if (entity == null)
        {
            Debug.LogError($"CmdIncreaseWaitTurn: Entity is null on {gameObject.name}");
            return;
        }
        entity.waitTurn++;
        Debug.Log($"CmdIncreaseWaitTurn: WaitTurn incremented for {entity.gameObject.name}. New waitTurn: {entity.waitTurn}");
    }

    [Command(requiresAuthority = false)]
    public void CmdAnimateAttack(GameObject attackerObj, GameObject targetObj, int attackerStrength, int targetStrength)
    {
        if (attackerObj == null || targetObj == null)
        {
            Debug.LogError($"CmdAnimateAttack: Attacker ({attackerObj}) or Target ({targetObj}) is null!");
            return;
        }

        if (!attackerObj.activeInHierarchy || !targetObj.activeInHierarchy)
        {
            Debug.LogError($"CmdAnimateAttack: Attacker ({attackerObj?.name}) or Target ({targetObj?.name}) is inactive!");
            return;
        }

        NetworkIdentity attackerIdentity = attackerObj.GetComponent<NetworkIdentity>();
        NetworkIdentity targetIdentity = targetObj.GetComponent<NetworkIdentity>();
        if (attackerIdentity == null || targetIdentity == null)
        {
            Debug.LogError($"CmdAnimateAttack: NetworkIdentity missing on Attacker ({attackerObj?.name}) or Target ({targetObj?.name})!");
            return;
        }

        Entity attackerEntity = attackerObj.GetComponent<Entity>();
        Entity targetEntity = targetObj.GetComponent<Entity>();
        if (attackerEntity == null || targetEntity == null)
        {
            Debug.LogError($"CmdAnimateAttack: Entity component missing. Attacker: {attackerEntity}, Target: {targetEntity}");
            return;
        }
        if (attackerStrength <= 0 || targetStrength < 0)
        {
            Debug.LogWarning($"CmdAnimateAttack: Invalid strength. Attacker: {attackerStrength}, Target: {targetStrength}. Using entity values.");
            attackerStrength = Mathf.Max(0, attackerEntity.strength);
            targetStrength = Mathf.Max(0, targetEntity.strength);
        }
        else if (attackerStrength != attackerEntity.strength || targetStrength != targetEntity.strength)
        {
            Debug.LogWarning($"CmdAnimateAttack: Strength mismatch. Attacker: {attackerStrength} vs {attackerEntity.strength}, Target: {targetStrength} vs {targetEntity.strength}. Using entity values.");
            attackerStrength = attackerEntity.strength;
            targetStrength = targetEntity.strength;
        }

        Debug.Log($"CmdAnimateAttack: Initiating attack from {attackerObj.name} (strength: {attackerStrength}) to {targetObj.name} (strength: {targetStrength})");

        RpcAnimateAttack(attackerIdentity.netId, targetIdentity.netId);

        CardAnimator animator = attackerObj.GetComponent<CardAnimator>();
        float totalDuration = animator != null
            ? animator.moveDuration + animator.attackPause + animator.returnDuration + 1.0f
            : 2.0f;
        Debug.Log($"CmdAnimateAttack: Animation duration set to {totalDuration}s");

        StartCoroutine(ApplyDamageAfterAnimation(attackerObj, targetObj, attackerStrength, targetStrength, totalDuration));
    }

    [ClientRpc]
    void RpcAnimateAttack(uint attackerNetId, uint targetNetId)
    {
        GameObject attackerObj = NetworkClient.spawned.ContainsKey(attackerNetId) ? NetworkClient.spawned[attackerNetId].gameObject : null;
        GameObject targetObj = NetworkClient.spawned.ContainsKey(targetNetId) ? NetworkClient.spawned[targetNetId].gameObject : null;

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

    private IEnumerator ApplyDamageAfterAnimation(GameObject attackerObj, GameObject targetObj, int attackerStrength, int targetStrength, float delay)
    {
        Debug.Log($"ApplyDamageAfterAnimation: Starting for {attackerObj?.name} attacking {targetObj?.name}. Waiting {delay}s");
        yield return new WaitForSeconds(delay);

        if (attackerObj == null || targetObj == null)
        {
            Debug.LogError($"ApplyDamageAfterAnimation: Attacker ({attackerObj?.name}) or Target ({targetObj?.name}) is null!");
            yield break;
        }

        if (!attackerObj.activeInHierarchy || !targetObj.activeInHierarchy)
        {
            Debug.LogError($"ApplyDamageAfterAnimation: Attacker ({attackerObj.name}) or Target ({targetObj.name}) is inactive!");
            yield break;
        }

        Entity attackerEntity = attackerObj.GetComponent<Entity>();
        Entity targetEntity = targetObj.GetComponent<Entity>();
        if (attackerEntity == null || targetEntity == null)
        {
            Debug.LogError($"ApplyDamageAfterAnimation: Entity component missing. Attacker Entity: {attackerEntity}, Target Entity: {targetEntity}");
            yield break;
        }

        Combat attackerCombat = attackerObj.GetComponent<Combat>();
        Combat targetCombat = targetObj.GetComponent<Combat>();
        if (attackerCombat == null || targetCombat == null)
        {
            Debug.LogError($"ApplyDamageAfterAnimation: Combat component missing. Attacker Combat: {attackerCombat}, Target Combat: {targetCombat}");
            yield break;
        }

        if (attackerCombat.entity == null || targetCombat.entity == null)
        {
            Debug.LogWarning($"ApplyDamageAfterAnimation: Combat.entity is null. Attacker: {attackerCombat.entity}, Target: {targetCombat.entity}. Assigning entities.");
            attackerCombat.entity = attackerEntity;
            targetCombat.entity = targetEntity;
        }

        Debug.Log($"ApplyDamageAfterAnimation: Applying damage. {attackerObj.name} deals {attackerStrength} to {targetObj.name}, {targetObj.name} deals {targetStrength} to {attackerObj.name}");

        targetCombat.CmdChangeHealth(-attackerStrength);
        attackerCombat.CmdChangeHealth(-targetStrength);
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
            ? animator.moveDuration + animator.attackPause + animator.returnDuration + 0.5f
            : 1.0f;
        Debug.Log($"DestroyCardAfterAnimation: Waiting for {animationDuration}s before destroying {cardObject.name}");

        yield return new WaitForSeconds(animationDuration);

        if (!cardObject.activeInHierarchy)
        {
            Debug.LogWarning($"DestroyCardAfterAnimation: {cardObject.name} already inactive, skipping destruction.");
            yield break;
        }

        Debug.Log($"DestroyCardAfterAnimation: Destroying {cardObject.name} with netId {cardIdentity.netId}");
        RpcDestroyCard(cardIdentity.netId);
        NetworkServer.Destroy(cardObject);
    }

    [ClientRpc]
    void RpcDestroyCard(uint cardNetId)
    {
        if (!NetworkClient.spawned.ContainsKey(cardNetId))
        {
            Debug.LogWarning($"RpcDestroyCard: Card with netId {cardNetId} not found! Spawned netIds: {string.Join(", ", NetworkClient.spawned.Keys)}");
            return;
        }

        GameObject cardObject = NetworkClient.spawned[cardNetId].gameObject;
        Debug.Log($"RpcDestroyCard: Deactivating {cardObject.name} on client");
        if (Player.gameManager != null)
        {
            cardObject.transform.SetParent(null);
            cardObject.SetActive(false);
        }
    }
}