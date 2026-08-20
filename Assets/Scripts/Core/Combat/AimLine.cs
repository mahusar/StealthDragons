using System;
using UnityEngine;

[Serializable]
public enum Target : byte
{
    OWNER,
    OPPONENT,
    FRIENDLIES,
    ENEMIES,
    RANDOM,
    ALL,
}

public class AimLine : MonoBehaviour
{
    [Header("Arrow Head")]
    public GameObject headPrefab;

    [HideInInspector] public Entity caster;

    private AimTip head;
    private GameObject spawned;
    private bool aiming;
    private bool castingAbility;

    public bool Aiming
    {
        get { return aiming; }
    }

    public void DrawLine(Entity entity, CardInfo card, Vector2 startPosition, bool IsAbility)
    {
        if (headPrefab == null)
        {
            Debug.LogWarning("AimLine: no arrow head prefab is assigned, so nothing can be aimed.");
            return;
        }

        Clear();

        caster = entity;
        castingAbility = IsAbility;

        spawned = Instantiate(headPrefab);
        spawned.transform.SetParent(transform, false);

        head = spawned.GetComponent<AimTip>();

        if (head == null)
        {
            Debug.LogWarning("AimLine: the arrow head prefab carries no AimTip component.");
            Clear();
            return;
        }

        head.card = new CardInfo(card.data);
        aiming = true;
    }

    public void Clear()
    {
        aiming = false;
        head = null;

        if (spawned == null) return;

        Destroy(spawned);
        spawned = null;
    }

    private void Update()
    {
        if (!aiming || head == null) return;

        Camera view = Camera.main;
        if (view == null) return;

        Vector2 pointer = view.ScreenToWorldPoint(Input.mousePosition);

        head.transform.position = pointer;
        head.FindTargets(caster, pointer, castingAbility);
    }
}
