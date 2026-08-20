using UnityEngine;

[CreateAssetMenu(menuName = "Card/Spell Card", order = 111)]
public partial class SpellCard : CardDefinition
{
    [Header("Propeties")]
    public bool targeted = false;
    public int healthChange = 0;
    public int strengthChange = 0;
    public int cardDraw = 0;
    public bool untilEndOfTurn = false;

    public bool Harmful
    {
        get { return healthChange < 0 || strengthChange < 0; }
    }

    public bool Draws
    {
        get { return cardDraw != 0; }
    }
}
