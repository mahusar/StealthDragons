using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public class BetLedger
{
    public const string KindPayout = "payout";
    public const string KindRefund = "refund";

    public const string MatchPlaying = "playing";
    public const string MatchSettled = "settled";

    public class Entry
    {
        public string type;
        public string recordId;
        public string matchId;
        public string kind;
        public string state;
        public int connectionId;
        public string depositAddress;
        public string payoutAddress;
        public decimal amount;
        public string txid;
        public string error;
        public string utc;
    }

    private readonly string path;
    private readonly object gate = new object();

    private BetLedger(string path) { this.path = path; }

    public static BetLedger ForPort(int port)
    {
        string file = Path.Combine(Application.persistentDataPath, $"bets-{port}.jsonl");
        Debug.Log($"[BetLedger] Using {file}");
        return new BetLedger(file);
    }

    private void Append(Entry e)
    {
        e.utc = DateTime.UtcNow.ToString("o");
        string line = JsonConvert.SerializeObject(e);
        lock (gate)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    public IEnumerable<Entry> ReadAll()
    {
        if (!File.Exists(path)) yield break;

        string[] lines;
        lock (gate) { lines = File.ReadAllLines(path); }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Entry e = null;
            try { e = JsonConvert.DeserializeObject<Entry>(line); }
            catch (Exception ex) { Debug.LogWarning($"[BetLedger] Skipping corrupt line: {ex.Message}"); }
            if (e != null) yield return e;
        }
    }

    public void RecordIssue(string matchId, int connectionId, string depositAddress, string payoutAddress)
    {
        Append(new Entry
        {
            type = "issue",
            matchId = matchId,
            connectionId = connectionId,
            depositAddress = depositAddress,
            payoutAddress = payoutAddress
        });
    }

    public string LookupPayoutAddress(string depositAddress)
    {
        foreach (var e in ReadAll())
            if (e.type == "issue" && e.depositAddress == depositAddress)
                return e.payoutAddress;
        return null;
    }

    public void RecordFunded(string matchId, int connectionId, string depositAddress,
                             string payoutAddress, decimal amount)
    {
        Append(new Entry
        {
            type = "funded",
            matchId = matchId,
            connectionId = connectionId,
            depositAddress = depositAddress,
            payoutAddress = payoutAddress,
            amount = amount
        });
    }

    public void RecordMatchState(string matchId, string state)
    {
        Append(new Entry { type = "matchstate", matchId = matchId, state = state });
    }

    public bool HasSettledKind(string matchId, string kind)
    {
        var ids = new HashSet<string>();
        foreach (var e in ReadAll())
            if (e.type == "send" && e.matchId == matchId && e.kind == kind
                && !string.IsNullOrEmpty(e.recordId))
                ids.Add(e.recordId);

        if (ids.Count == 0) return false;

        foreach (var e in ReadAll())
            if (e.type == "send" && e.state == "sent" && ids.Contains(e.recordId))
                return true;
        return false;
    }

    public List<Entry> GetOrphanedFundings()
    {
        var funded = new Dictionary<string, Entry>();
        var matchState = new Dictionary<string, string>();
        var settledRecordIds = new HashSet<string>();
        var sendsByRecord = new Dictionary<string, Entry>();

        foreach (var e in ReadAll())
        {
            switch (e.type)
            {
                case "funded":
                    funded[e.matchId + "|" + e.connectionId] = e;
                    break;
                case "matchstate":
                    matchState[e.matchId] = e.state;
                    break;
                case "send":
                    if (string.IsNullOrEmpty(e.recordId)) break;
                    if (e.state == "sent") settledRecordIds.Add(e.recordId);
                    else if (e.state == "pending") sendsByRecord[e.recordId] = e;
                    break;
            }
        }

        var paidOutMatches = new HashSet<string>();
        var refundedPlayers = new HashSet<string>();
        foreach (var kv in sendsByRecord)
        {
            if (!settledRecordIds.Contains(kv.Key)) continue;
            Entry send = kv.Value;
            if (send.kind == KindPayout) paidOutMatches.Add(send.matchId);
            else if (send.kind == KindRefund) refundedPlayers.Add(send.matchId + "|" + send.connectionId);
        }

        var orphaned = new List<Entry>();
        foreach (var kv in funded)
        {
            Entry f = kv.Value;
            if (matchState.TryGetValue(f.matchId, out string state) && state == MatchSettled) continue;
            if (paidOutMatches.Contains(f.matchId)) continue;
            if (refundedPlayers.Contains(kv.Key)) continue;
            orphaned.Add(f);
        }
        return orphaned;
    }

    public string BeginSend(string matchId, string kind, int connectionId, string address, decimal amount)
    {
        string recordId = Guid.NewGuid().ToString("N");
        Append(new Entry
        {
            type = "send",
            recordId = recordId,
            matchId = matchId,
            kind = kind,
            state = "pending",
            connectionId = connectionId,
            payoutAddress = address,
            amount = amount
        });
        return recordId;
    }

    public void CompleteSend(string recordId, string txid)
    {
        Append(new Entry { type = "send", recordId = recordId, state = "sent", txid = txid });
    }

    public void FailSend(string recordId, string error)
    {
        Append(new Entry { type = "send", recordId = recordId, state = "failed", error = error });
    }

    public List<Entry> GetUnresolvedSends()
    {
        var pending = new Dictionary<string, Entry>();
        var resolved = new HashSet<string>();

        foreach (var e in ReadAll())
        {
            if (e.type != "send" || string.IsNullOrEmpty(e.recordId)) continue;
            if (e.state == "pending") pending[e.recordId] = e;
            else resolved.Add(e.recordId);
        }

        var unresolved = new List<Entry>();
        foreach (var kv in pending)
            if (!resolved.Contains(kv.Key)) unresolved.Add(kv.Value);
        return unresolved;
    }

    public bool HasSettled(string matchId, string kind, int connectionId)
    {
        var ids = new HashSet<string>();
        foreach (var e in ReadAll())
            if (e.type == "send" && e.matchId == matchId && e.kind == kind
                && e.connectionId == connectionId && !string.IsNullOrEmpty(e.recordId))
                ids.Add(e.recordId);

        if (ids.Count == 0) return false;

        foreach (var e in ReadAll())
            if (e.type == "send" && e.state == "sent" && ids.Contains(e.recordId))
                return true;
        return false;
    }
}
