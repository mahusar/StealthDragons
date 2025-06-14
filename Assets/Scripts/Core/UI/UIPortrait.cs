﻿using UnityEngine;
using UnityEngine.UI;

public partial class UIPortrait : MonoBehaviour
{
    public GameObject panel;
    public Image portrait;
    public Text username;
    public Text deckAmount;
    public Text graveyardAmount;
    public Text handAmount;
    public Text health;
    public Text mana;
    public PlayerType playerType;

    private PlayerInfo enemyInfo;

    void Update()
    {
        Player player = Player.localPlayer;
        if (player && player.hasEnemy && player.enemyInfo.player != null) // Check if enemyInfo.player exists
            enemyInfo = player.enemyInfo;

        if (player && playerType == PlayerType.PLAYER)
        {
            panel.SetActive(true);
            player.transform.position = portrait.transform.position;
            portrait.sprite = player.portrait;
            username.text = player.username;
            deckAmount.text = player.deck.deckList.Count.ToString();
            graveyardAmount.text = player.deck.graveyard.Count.ToString();
            handAmount.text = player.deck.hand.Count.ToString();
            health.text = player.health.ToString();
            mana.text = player.mana.ToString();
            player.spawnOffset = portrait.transform;
        }
        else if (player && player.hasEnemy && playerType == PlayerType.ENEMY && enemyInfo.player != null && enemyInfo.player.gameObject.activeInHierarchy)
        {
            panel.SetActive(true);
            enemyInfo.player.transform.position = portrait.transform.position;
            portrait.sprite = enemyInfo.portrait;
            username.text = enemyInfo.username;
            deckAmount.text = enemyInfo.deckCount.ToString();
            graveyardAmount.text = enemyInfo.graveCount.ToString();
            handAmount.text = enemyInfo.handCount.ToString();
            health.text = enemyInfo.health.ToString();
            mana.text = enemyInfo.mana.ToString();
            enemyInfo.data.spawnOffset = portrait.transform;
        }
        else
        {
            panel.SetActive(false);
        }
    }
}