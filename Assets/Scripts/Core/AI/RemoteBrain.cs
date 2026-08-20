using System.Collections;
using Mirror;
using UnityEngine;

public class RemoteBrain : MonoBehaviour
{
    [Header("Limits")]
    [Tooltip("Seconds a bot gets to answer one state document before the turn is abandoned.")]
    public float decisionTimeout = 5f;

    [Tooltip("Hard cap on actions per turn. Guards against a bot that never ends its turn.")]
    public int maxActionsPerTurn = 24;

    [Tooltip("Refused or malformed actions in a row before the turn is abandoned.")]
    public int maxRefusalsPerTurn = 3;

    [Tooltip("Abandoned turns before this seat stops being driven at all.")]
    public int maxFaults = 3;

    [Tooltip("Seconds a bot gets to sign the match receipt before the seat is left unsigned.")]
    public float signatureTimeout = 8f;

    [Header("Pacing")]
    [Tooltip("Seconds between applied actions, so a human can follow the board.")]
    public float actionDelay = 0.6f;

    private Player self;
    private GameManager gameManager;
    private IBotChannel channel;

    private bool takingTurn;
    private bool retired;
    private int faults;
    private int lastTurnTaken = -1;

    private static int nextToken;

    public string BotName
    {
        get { return channel == null ? "" : channel.Name; }
    }

    public string BotKey
    {
        get { return channel == null ? "" : channel.Key; }
    }

    public bool Signing { get; private set; }

    public void ServerInitialize(Player botPlayer, IBotChannel botChannel)
    {
        self = botPlayer;
        channel = botChannel;

        Debug.Log($"RemoteBrain: {BotName} driving {self.username} (netId {self.netId}).");
    }

    void OnDestroy()
    {
        IBotChannel closing = channel;
        channel = null;

        if (closing == null) return;

        try
        {
            closing.Close("");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RemoteBrain: {closing.Name} threw while being released ({e.GetType().Name}).");
        }
    }

    void Update()
    {
        if (retired) return;
        if (!NetworkServer.active || self == null || channel == null) return;

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null) return;
        }

        if (takingTurn) return;
        if (gameManager.currentTurnNetId != self.netId) return;

        if (gameManager.turnCount == lastTurnTaken) return;
        lastTurnTaken = gameManager.turnCount;

        StartCoroutine(TakeTurn());
    }

    private IEnumerator TakeTurn()
    {
        takingTurn = true;

        yield return null;

        int actions = 0;
        int refusals = 0;

        Debug.Log($"RemoteBrain: {self.username} starting turn {gameManager.turnCount}.");

        while (actions < maxActionsPerTurn && StillOurTurn())
        {
            string state = BotView.Build(self, gameManager);
            if (state == null)
            {
                Fault("the board could not be described");
                break;
            }

            int token = System.Threading.Interlocked.Increment(ref nextToken);
            string action = null;

            bool asked = Ask(token, state);
            if (!asked)
            {
                Fault("the bot could not be asked");
                break;
            }

            float deadline = Time.realtimeSinceStartup + decisionTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (!TryPoll(token, out action))
                {
                    action = null;
                    break;
                }

                if (action != null) break;
                yield return null;
            }

            if (action == null)
            {
                Cancel(token);
                Fault($"no answer within {decisionTimeout:0.#}s");
                break;
            }

            string detail;
            BotActionResult result = BotAction.Apply(action, self, gameManager, out detail);

            if (result == BotActionResult.Ended)
            {
                Debug.Log($"RemoteBrain: {self.username} ends its turn after {actions} action(s).");
                break;
            }

            if (result == BotActionResult.Refused || result == BotActionResult.Malformed)
            {
                refusals++;
                Debug.LogWarning($"RemoteBrain: {self.username} sent \"{Shorten(action)}\" - {detail} " +
                                 $"({refusals}/{maxRefusalsPerTurn}).");

                if (refusals >= maxRefusalsPerTurn)
                {
                    Fault("too many refused actions");
                    break;
                }

                continue;
            }

            actions++;
            refusals = 0;

            if (actionDelay > 0f) yield return new WaitForSeconds(actionDelay);
        }

        if (actions >= maxActionsPerTurn)
            Debug.LogWarning($"RemoteBrain: {self.username} hit the {maxActionsPerTurn}-action cap.");

        if (StillOurTurn()) gameManager.ServerEndTurn(self);

        takingTurn = false;
    }

    public void ServerRequestReceiptSignature(string digestHex)
    {
        if (!NetworkServer.active || channel == null || Signing) return;
        if (string.IsNullOrEmpty(digestHex) || string.IsNullOrEmpty(channel.Key)) return;

        StartCoroutine(SignReceipt(digestHex));
    }

    private IEnumerator SignReceipt(string digestHex)
    {
        Signing = true;

        int token = System.Threading.Interlocked.Increment(ref nextToken);
        string key = channel.Key;
        bool asked;

        try
        {
            channel.RequestSignature(token, digestHex);
            asked = true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RemoteBrain: {BotName} threw being asked to sign the receipt - {e.Message}");
            asked = false;
        }

        string signature = null;

        if (asked)
        {
            float deadline = Time.realtimeSinceStartup + signatureTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (!TryPoll(token, out signature))
                {
                    signature = null;
                    break;
                }

                if (signature != null) break;
                yield return null;
            }
        }

        if (string.IsNullOrEmpty(signature))
        {
            Cancel(token);
            Debug.LogWarning($"RemoteBrain: {BotName} did not sign the match receipt within " +
                             $"{signatureTimeout:0.#}s - its seat stays unsigned.");

            Signing = false;
            yield break;
        }

        GameManager manager = gameManager != null ? gameManager : FindFirstObjectByType<GameManager>();

        if (manager != null) manager.ServerAcceptBotSignature(key, signature);

        Signing = false;
    }

    private bool Ask(int token, string state)
    {
        try
        {
            channel.Request(token, state);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RemoteBrain: {BotName} threw asking for a move - {e.Message}");
            return false;
        }
    }

    private bool TryPoll(int token, out string action)
    {
        action = null;

        try
        {
            action = channel.Poll(token);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RemoteBrain: {BotName} threw answering - {e.Message}");
            return false;
        }
    }

    private void Cancel(int token)
    {
        try
        {
            channel.Cancel(token);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RemoteBrain: {BotName} threw cancelling - {e.Message}");
        }
    }

    private void Fault(string reason)
    {
        faults++;

        Debug.LogWarning($"RemoteBrain: {self.username} abandoned turn {gameManager.turnCount} - {reason} " +
                         $"(fault {faults}/{maxFaults}).");

        if (faults < maxFaults) return;

        retired = true;
        Debug.LogError($"RemoteBrain: {self.username} is no longer being driven after {faults} faults. " +
                       "It will pass every remaining turn.");
    }

    private bool StillOurTurn()
    {
        return NetworkServer.active && self != null && self.health > 0 &&
               gameManager != null && gameManager.currentTurnNetId == self.netId;
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 40 ? flat : flat.Substring(0, 37) + "...";
    }
}
