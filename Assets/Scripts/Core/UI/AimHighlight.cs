using UnityEngine;

public static class AimHighlight
{
    public static void Paint(GameObject target, bool aimed)
    {
        if (target == null) return;

        BoardCard card = target.GetComponent<BoardCard>();
        if (card != null)
        {
            card.ShowAimed(aimed);
            return;
        }

        Player hero = target.GetComponent<Player>();
        if (hero == null) return;

        PlayerPortrait portrait = PlayerPortrait.For(hero);
        if (portrait != null) portrait.ShowAimed(aimed);
    }

    public static void Show(SpellCard spell, Player caster, bool on)
    {
        if (!on)
        {
            Clear();
            return;
        }

        foreach (BoardCard card in Spellbook.Preview(spell, caster))
            if (card != null) card.ShowAimed(true);
    }

    public static void Clear()
    {
        foreach (BoardCard card in Object.FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
            if (card != null) card.ShowAimed(false);

        foreach (PlayerPortrait portrait in Object.FindObjectsByType<PlayerPortrait>(FindObjectsSortMode.None))
            if (portrait != null) portrait.ShowAimed(false);
    }
}
