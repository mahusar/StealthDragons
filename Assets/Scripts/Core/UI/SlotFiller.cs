using UnityEngine;

public static class SlotFiller
{
    public static void Fill(GameObject prefab, int amount, Transform parent)
    {
        if (parent == null) return;

        if (amount < 0) amount = 0;

        while (parent.childCount > amount)
        {
            Transform spare = parent.GetChild(parent.childCount - 1);

            spare.SetParent(null, false);
            Object.Destroy(spare.gameObject);
        }

        if (prefab == null) return;

        while (parent.childCount < amount)
        {
            GameObject slot = Object.Instantiate(prefab);
            slot.transform.SetParent(parent, false);
        }
    }
}
