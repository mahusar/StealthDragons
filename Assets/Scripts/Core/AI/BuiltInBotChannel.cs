using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum BotSkill
{
    Easy,
    Normal,
}

public class BuiltInBotChannel : IBotChannel
{
    private readonly Dictionary<int, string> answers = new Dictionary<int, string>();

    private readonly BotSkill skill;
    private readonly PlayerIdentity identity;

    public BuiltInBotChannel() : this(BotSkill.Normal, null)
    {
    }

    public BuiltInBotChannel(BotSkill skill) : this(skill, null)
    {
    }

    public BuiltInBotChannel(BotSkill skill, PlayerIdentity identity)
    {
        this.skill = skill;
        this.identity = identity;
    }

    public string Name
    {
        get { return NameOf(skill); }
    }

    public string Key
    {
        get { return identity == null ? "" : identity.PublicKeyHex; }
    }

    public static string NameOf(BotSkill skill)
    {
        return skill == BotSkill.Easy ? "easy" : "normal";
    }

    public void Request(int token, string state)
    {
        answers[token] = Decide(state, skill);
    }

    public void RequestSignature(int token, string digestHex)
    {
        if (identity == null) return;

        byte[] digest = CardShuffle.FromHex(digestHex);
        if (digest == null || digest.Length == 0) return;

        answers[token] = identity.SignHex(digest);
    }

    public string Poll(int token)
    {
        string answer;
        if (!answers.TryGetValue(token, out answer)) return null;

        answers.Remove(token);
        return answer;
    }

    public void Cancel(int token)
    {
        answers.Remove(token);
    }

    public void Close(string result)
    {
        answers.Clear();
    }

    public static string Decide(string state)
    {
        return Decide(state, BotSkill.Normal);
    }

    public static string Decide(string state, BotSkill skill)
    {
        JObject board;

        try
        {
            board = JObject.Parse(state);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("BuiltInBotChannel: could not read the board - " + e.Message);
            return BotAction.End;
        }

        if (!Flag(board, "yourTurn")) return BotAction.End;

        if (skill != BotSkill.Easy)
        {
            string swing = Finisher(board);
            if (swing != null) return swing;
        }

        string play = ChooseCreature(board, skill);
        if (play != null) return play;

        string attack = ChooseAttack(board, skill);
        if (attack != null) return attack;

        return BotAction.End;
    }

    private static string Finisher(JObject board)
    {
        if (Walled(board)) return null;

        JToken opponent = board["opponent"];
        if (opponent == null || opponent.Type == JTokenType.Null) return null;
        if (!Flag(opponent, "targetable")) return null;

        int face = Number(opponent, "health");

        List<JToken> mine = Ready(board);
        int reach = 0;

        foreach (JToken card in mine) reach += Number(card, "strength");

        if (mine.Count > 0 && reach >= face) return Swing(mine[0], opponent);

        int mana = Number(board["you"], "mana");
        int first = -1;

        foreach (JToken card in Rushers(board))
        {
            int cost = Number(card, "cost");
            if (cost > mana) continue;

            mana -= cost;
            reach += Number(card, "strength");

            if (first < 0) first = Number(card, "index");
        }

        if (first >= 0 && reach >= face)
            return BotAction.Play + " " + first.ToString(CultureInfo.InvariantCulture);

        return null;
    }

    private static List<JToken> Rushers(JObject board)
    {
        List<JToken> found = new List<JToken>();

        foreach (JToken card in Array(board, "hand"))
        {
            if (Word(card, "kind") != "creature") continue;
            if (!Flag(card, "charge")) continue;

            found.Add(card);
        }

        found.Sort((a, b) => Number(a, "cost").CompareTo(Number(b, "cost")));

        return found;
    }

    private static List<JToken> Ready(JObject board)
    {
        List<JToken> live = new List<JToken>();

        foreach (JToken card in Array(board, "yourField"))
        {
            if (Number(card, "waitTurn") > 0) continue;
            if (Flag(card, "attacked")) continue;
            if (Number(card, "strength") <= 0) continue;
            if (Number(card, "health") <= 0) continue;

            live.Add(card);
        }

        return live;
    }

    private static bool Walled(JObject board)
    {
        return Number(board["opponent"], "taunt") > 0;
    }

    private static string ChooseCreature(JObject board, BotSkill skill)
    {
        int mana = Number(board["you"], "mana");

        if (skill == BotSkill.Easy) return EasyCreature(board, mana);

        bool hurt = Number(board["you"], "health") <= 12;
        bool behind = Count(board, "enemyField") > Count(board, "yourField");

        int bestIndex = -1;
        int bestScore = int.MinValue;

        foreach (JToken card in Array(board, "hand"))
        {
            if (Word(card, "kind") != "creature") continue;

            int cost = Number(card, "cost");
            if (cost > mana) continue;

            int score = cost * 2 + Number(card, "strength") + Number(card, "health");

            if (Flag(card, "shield")) score += 4;
            if (Flag(card, "taunt") && (hurt || behind)) score += 6;
            if (Flag(card, "lifesteal") && hurt) score += 4;
            if (Flag(card, "charge")) score += 2;
            if (Flag(card, "deathrattle")) score += Number(card, "deathrattleDamage") + 2;

            if (score <= bestScore) continue;

            bestScore = score;
            bestIndex = Number(card, "index");
        }

        return bestIndex < 0
            ? null
            : BotAction.Play + " " + bestIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static string EasyCreature(JObject board, int mana)
    {
        int bestIndex = -1;
        int bestCost = int.MaxValue;

        foreach (JToken card in Array(board, "hand"))
        {
            if (Word(card, "kind") != "creature") continue;

            int cost = Number(card, "cost");
            if (cost > mana || cost >= bestCost) continue;

            bestCost = cost;
            bestIndex = Number(card, "index");
        }

        return bestIndex < 0
            ? null
            : BotAction.Play + " " + bestIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static string ChooseAttack(JObject board, BotSkill skill)
    {
        if (skill == BotSkill.Easy) return EasyAttack(board);

        List<JToken> mine = Ready(board);
        List<JToken> targets = LegalTargets(board);

        if (mine.Count == 0 || targets.Count == 0) return null;

        JToken bestAttacker = null;
        JToken bestTarget = null;
        int bestScore = 0;

        foreach (JToken attacker in mine)
        {
            foreach (JToken target in targets)
            {
                int score = Worth(board, attacker, target);
                if (score <= bestScore) continue;

                bestScore = score;
                bestAttacker = attacker;
                bestTarget = target;
            }
        }

        return bestAttacker == null ? null : Swing(bestAttacker, bestTarget);
    }

    private static List<JToken> LegalTargets(JObject board)
    {
        List<JToken> live = new List<JToken>();
        bool walled = Walled(board);

        foreach (JToken card in Array(board, "enemyField"))
        {
            if (!Flag(card, "targetable")) continue;
            if (Number(card, "health") <= 0) continue;
            if (walled && !Flag(card, "taunt")) continue;

            live.Add(card);
        }

        if (walled) return live;

        JToken opponent = board["opponent"];

        if (opponent != null && opponent.Type != JTokenType.Null && Flag(opponent, "targetable"))
            live.Add(opponent);

        return live;
    }

    private static int Worth(JObject board, JToken attacker, JToken target)
    {
        int hit = Number(attacker, "strength");
        int mine = Number(board["you"], "health");

        bool starving = Flag(attacker, "lifesteal") && mine <= 15;

        JToken opponent = board["opponent"];

        if (opponent != null && opponent.Type != JTokenType.Null &&
            Number(target, "netId") == Number(opponent, "netId"))
            return hit * 3 + (starving ? hit : 0);

        if (Flag(target, "shield")) return Mathf.Max(1, 20 - hit * 2);

        int health = Number(target, "health");

        bool kills = health > 0 && health <= hit;
        bool dies = Number(target, "strength") >= Number(attacker, "health") && !Flag(attacker, "shield");

        int rattle = Flag(target, "deathrattle") ? Number(target, "deathrattleDamage") * 2 : 0;

        if (kills && !dies) return Mathf.Max(1, 60 + Body(target) - rattle);
        if (kills && dies) return Mathf.Max(1, 25 + Body(target) - Body(attacker) - rattle);
        if (dies) return 0;

        return Mathf.Max(1, hit - 2 + (starving ? hit : 0));
    }

    private static int Body(JToken card)
    {
        int score = Number(card, "strength") + Number(card, "health");

        if (Flag(card, "taunt")) score += 3;
        if (Flag(card, "lifesteal")) score += 3;
        if (Flag(card, "shield")) score += 3;

        return score;
    }

    private static string EasyAttack(JObject board)
    {
        List<JToken> mine = Ready(board);
        if (mine.Count == 0) return null;

        JToken opponent = board["opponent"];
        if (opponent == null || opponent.Type == JTokenType.Null) return null;

        JToken target = null;

        if (Walled(board))
        {
            int toughest = -1;

            foreach (JToken card in Array(board, "enemyField"))
            {
                if (!Flag(card, "taunt")) continue;
                if (!Flag(card, "targetable")) continue;

                int health = Number(card, "health");
                if (health <= 0 || health <= toughest) continue;

                toughest = health;
                target = card;
            }
        }
        else if (Flag(opponent, "targetable"))
        {
            target = opponent;
        }

        return target == null ? null : Swing(mine[0], target);
    }

    private static string Swing(JToken attacker, JToken target)
    {
        return BotAction.Attack + " " +
               Number(attacker, "netId").ToString(CultureInfo.InvariantCulture) + " " +
               Number(target, "netId").ToString(CultureInfo.InvariantCulture);
    }

    private static int Count(JObject board, string name)
    {
        int found = 0;

        foreach (JToken card in Array(board, name)) found++;

        return found;
    }

    private static IEnumerable<JToken> Array(JObject board, string name)
    {
        JToken found = board[name];
        if (found is JArray list) return list;

        return new JToken[0];
    }

    private static int Number(JToken owner, string name)
    {
        if (owner == null) return 0;

        JToken found = owner[name];
        if (found == null || found.Type == JTokenType.Null) return 0;

        int value;
        return int.TryParse(found.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? value
            : 0;
    }

    private static bool Flag(JToken owner, string name)
    {
        if (owner == null) return false;

        JToken found = owner[name];
        if (found == null || found.Type == JTokenType.Null) return false;

        bool value;
        return bool.TryParse(found.ToString(), out value) && value;
    }

    private static string Word(JToken owner, string name)
    {
        if (owner == null) return "";

        JToken found = owner[name];
        return found == null || found.Type == JTokenType.Null ? "" : found.ToString();
    }
}
