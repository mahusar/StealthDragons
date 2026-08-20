using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[Serializable]
public struct DeckEntry
{
    public CardDefinition card;
    public int amount;
}

public partial class CardDefinition : ScriptableObject
{
    [SerializeField] string id = "";

    [Header("Image")]
    public Sprite image;

    [Header("Properties")]
    public int cost;
    public string category;

    [Header("Description")]
    [SerializeField, TextArea(1, 30)] public string description;

    private static Dictionary<string, CardDefinition> loaded;

    public string CardID
    {
        get { return id; }
    }

    public static Dictionary<string, CardDefinition> Cache
    {
        get
        {
            if (loaded == null) loaded = Load();

            return loaded;
        }
    }

    public static void Forget()
    {
        loaded = null;
    }

    private static Dictionary<string, CardDefinition> Load()
    {
        Dictionary<string, CardDefinition> byId = new Dictionary<string, CardDefinition>();

        foreach (CardDefinition card in Resources.LoadAll<CardDefinition>(""))
        {
            if (card == null) continue;

            if (string.IsNullOrEmpty(card.CardID))
            {
                Debug.LogError("CardDefinition: " + card.name + " has no id and was left out of the card cache.");
                continue;
            }

            CardDefinition clash;
            if (byId.TryGetValue(card.CardID, out clash))
            {
                Debug.LogError("CardDefinition: " + card.name + " and " + clash.name +
                               " share the id " + card.CardID + ". Clear one of them in the inspector to regenerate it.");
                continue;
            }

            byId[card.CardID] = card;
        }

        return byId;
    }

    public virtual void Cast(Entity caster, Entity target)
    {
    }

    private void OnValidate()
    {
        if (!string.IsNullOrEmpty(id)) return;

#if UNITY_EDITOR
        id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(this));
#endif
    }
}
