using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class ReplayChannel : IBotChannel
{
    private readonly Queue<MatchReplay.Move> pending = new Queue<MatchReplay.Move>();
    private readonly Dictionary<int, string> answers = new Dictionary<int, string>();

    private readonly string name;
    private readonly string key;
    private readonly Func<string, uint> resolve;

    public int Remaining
    {
        get { return pending.Count; }
    }

    public bool Diverged { get; private set; }

    public string Trouble { get; private set; }

    public ReplayChannel(string name, string key, List<MatchReplay.Move> moves, Func<string, uint> resolve)
    {
        this.name = name;
        this.key = key;
        this.resolve = resolve;

        Trouble = "";

        if (moves != null)
            foreach (MatchReplay.Move move in moves) pending.Enqueue(move);
    }

    public string Name
    {
        get { return name; }
    }

    public string Key
    {
        get { return key; }
    }

    public void Request(int token, string state)
    {
        answers[token] = Next();
    }

    public void RequestSignature(int token, string digestHex)
    {
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

    private string Next()
    {
        if (pending.Count == 0) return BotAction.End;

        MatchReplay.Move move = pending.Dequeue();

        if (move.verb == BotAction.End) return BotAction.End;

        if (move.verb == BotAction.Play)
            return BotAction.Play + " " + move.handIndex.ToString(CultureInfo.InvariantCulture);

        if (move.verb == BotAction.Cast)
        {
            string cast = BotAction.Cast + " " + move.handIndex.ToString(CultureInfo.InvariantCulture);

            if (string.IsNullOrEmpty(move.target)) return cast;

            uint aimedAt = Resolve(move.target);

            if (aimedAt == 0)
            {
                Diverge("the replay casts at a creature that is not on this board (" + move.target + ")");
                return BotAction.End;
            }

            return cast + " " + aimedAt.ToString(CultureInfo.InvariantCulture);
        }

        if (move.verb == BotAction.Attack)
        {
            uint attacker = Resolve(move.attacker);
            uint target = Resolve(move.target);

            if (attacker == 0 || target == 0)
            {
                Diverge("the replay names a creature that is not on this board (" +
                        move.attacker + " -> " + move.target + ")");

                return BotAction.End;
            }

            return BotAction.Attack + " " +
                   attacker.ToString(CultureInfo.InvariantCulture) + " " +
                   target.ToString(CultureInfo.InvariantCulture);
        }

        Diverge("the replay holds an action this build cannot play back (" + move.verb + ")");
        return BotAction.End;
    }

    private uint Resolve(string stable)
    {
        if (string.IsNullOrEmpty(stable)) return 0;

        return resolve != null ? resolve(stable) : 0;
    }

    private void Diverge(string reason)
    {
        if (Diverged) return;

        Diverged = true;
        Trouble = reason;

        Debug.LogWarning("ReplayChannel: " + name + " could not follow the replay - " + reason);
    }
}
