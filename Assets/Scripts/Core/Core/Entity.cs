using System;
using Mirror;
using UnityEngine;

[Serializable]
public abstract partial class Entity : NetworkBehaviour
{
    [Header("Combat")]
    public Combat combat;
    [SyncVar] public Player owner;

    [Header("Stats")]
    [SyncVar] public int health = 0;
    [SyncVar] public int strength = 0;

    [Header("Targeting Arrow")]
    public Target casterType;
    public AimLine arrow;
    public Transform spawnOffset;
    [HideInInspector] public bool isTargeting = false;
    [HideInInspector] public GameObject arrowObject;

    public bool isTargetable = true;

    [Header("Special Properties")]
    [SyncVar] public int waitTurn = 1;
    [SyncVar] public bool hasAttackedThisTurn = false;
    [SyncVar] public bool shielded = false;
    public bool taunt = false;

    public bool IsDead()
    {
        return health <= 0;
    }

    public bool Ours()
    {
        return casterType == Target.FRIENDLIES &&
               Player.gameManager != null &&
               Player.gameManager.isOurTurn;
    }

    public bool CanAttack()
    {
        return Ours() && waitTurn == 0 && !hasAttackedThisTurn;
    }

    public bool CantAttack()
    {
        return Ours() && (waitTurn > 0 || hasAttackedThisTurn);
    }

    public virtual void SpawnTargetingArrow(CardInfo card, bool IsAbility = false, int handIndex = -1)
    {
        if (arrow == null)
        {
            Debug.LogWarning("Entity: " + name + " has no aim line assigned, so it cannot target.");
            return;
        }

        Vector3 from = spawnOffset != null ? spawnOffset.position : transform.position;

        arrowObject = Instantiate(arrow.gameObject, from, Quaternion.identity);

        AimLine line = arrowObject.GetComponent<AimLine>();

        if (line == null)
        {
            Debug.LogWarning("Entity: the aim line prefab carries no AimLine component.");
            Destroy(arrowObject);
            arrowObject = null;
            return;
        }

        isTargeting = true;
        if (Player.localPlayer != null) Player.localPlayer.isTargeting = true;

        Cursor.visible = false;

        line.DrawLine(this, card, from, IsAbility, handIndex);
    }

    public void DestroyTargetingArrow()
    {
        isTargeting = false;
        if (Player.localPlayer != null) Player.localPlayer.isTargeting = false;

        Cursor.visible = true;

        if (arrowObject == null) return;

        Destroy(arrowObject);
        arrowObject = null;
    }

    public virtual void Update()
    {
        if (isTargeting && Input.GetMouseButton(1)) DestroyTargetingArrow();
    }
}
