using System;
using UnityEngine;
using Mirror;

[Serializable]
public partial struct PlayerInfo
{
    public GameObject player;

    public PlayerInfo(GameObject player)
    {
        this.player = player;
    }

    public Player data
    {
        get
        {
            Player playerComponent = player.GetComponent<Player>();
            if (playerComponent != null)
            {
                return playerComponent;
            }
            else
            {
                Debug.LogError("Player component not found on the player object.");
                return null;
            }
        }
    }

    // Player's username
    public string username => data.username;
    public Sprite portrait => data.portrait;

    // Player health and mana
    public int health => data.health;
    public int mana => data.mana;

    // Cardback image
    public Sprite cardback => data.cardback;

    // Card count for UI
    public int handCount => data.deck.hand.Count;
    public int deckCount => data.deck.deckList.Count;
    public int graveCount => data.deck.graveyard.Count;

    // Taunt count
    public int tauntCount => data.tauntCount;
}

// Card List
public class SyncListPlayerInfo : SyncList<PlayerInfo> { }