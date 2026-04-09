using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Core;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;

public class DragonatorWallet : NetworkBehaviour
{
    public static DragonatorWallet Instance;

    [SyncVar(hook = nameof(OnPlayer1StatusChanged))]
    private string player1Status = "";
    [SyncVar(hook = nameof(OnPlayer2StatusChanged))]
    private string player2Status = "";

    [Header("Match Settings")]
    public float betAmount = 0.01f; // fixed bet in XST

    // Tracks each player's validation state
    private Dictionary<NetworkConnectionToClient, PlayerBetInfo> betInfos
        = new Dictionary<NetworkConnectionToClient, PlayerBetInfo>();

    private string matchDepositAddress = "";

    private string usedTxidsPath => Application.persistentDataPath + "/usedtxids.txt";

    private class PlayerBetInfo
    {
        public string txid;
        public string payoutAddress; // extracted from TX
        public bool validated;
        public bool sceneReady;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnPlayer1StatusChanged(string old, string newVal) =>
        BetUI.Instance?.UpdatePlayerStatus(1, newVal);

    void OnPlayer2StatusChanged(string old, string newVal) =>
        BetUI.Instance?.UpdatePlayerStatus(2, newVal);

    private HashSet<NetworkConnectionToClient> earlyReadyConnections
        = new HashSet<NetworkConnectionToClient>();

    [Command(requiresAuthority = false)]
    public void CmdClientReady(NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;

        if (!betInfos.ContainsKey(sender))
        {
            Debug.LogWarning($"[DragonatorWallet] CmdClientReady from unknown connection {sender.connectionId} — storing for later.");
            earlyReadyConnections.Add(sender);
            return;
        }

        betInfos[sender].sceneReady = true;
        Debug.Log($"[DragonatorWallet] Client {sender.connectionId} is scene-ready.");
        TrySendMatchInfoToAll();
    }
    public bool BothPlayersValidated()
    {
        if (betInfos.Count < 2) return false;
        foreach (var info in betInfos.Values)
            if (!info.validated) return false;
        return true;
    }
    [Server]
    private void TrySendMatchInfoToAll()
    {
        if (string.IsNullOrEmpty(matchDepositAddress)) return;
        foreach (var info in betInfos.Values)
            if (!info.sceneReady) return;

        foreach (var conn in betInfos.Keys)
            TargetReceiveMatchInfo(conn, matchDepositAddress, betAmount);
    }

    [Server]
    public void InitializeMatch(List<NetworkConnectionToClient> players)
    {
        betInfos.Clear();
        int index = 1;
        foreach (var conn in players)
        {
            betInfos[conn] = new PlayerBetInfo();
            string name = conn.identity?.GetComponent<Player>()?.username ?? $"Player {index}";
            if (index == 1) player1Status = $"{name}: Waiting...";
            else player2Status = $"{name}: Waiting...";
            index++;
        }

        foreach (var conn in earlyReadyConnections)
        {
            if (betInfos.ContainsKey(conn))
            {
                betInfos[conn].sceneReady = true;
                Debug.Log($"[DragonatorWallet] Late-applied early ready for connection {conn.connectionId}");
            }
        }
        earlyReadyConnections.Clear();

        StartCoroutine(GenerateMatchAddress(players));
    }

    [Server]
    private IEnumerator GenerateMatchAddress(List<NetworkConnectionToClient> players)
    {
        // Generate a fresh deposit address for this match
        string address = null;
        bool done = false;

        Task.Run(async () =>
        {
            try
            {
                var rpc = RpcHandler.GetInstance();
                string response = await rpc.SendRpcRequest("getnewaddress");
                var result = JsonConvert.DeserializeObject<RpcResponse>(response);
                address = result?.result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DragonatorWallet] getnewaddress failed: {e.Message}");
            }
            finally { done = true; }
        });

        while (!done) yield return null;

        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("[DragonatorWallet] Failed to generate match address.");
            yield break;
        }

        matchDepositAddress = address;
        Debug.Log($"[DragonatorWallet] Match address: {matchDepositAddress}");

        TrySendMatchInfoToAll();
    }

    [TargetRpc]
    private void TargetReceiveMatchInfo(NetworkConnectionToClient conn,
        string depositAddress, float amount)
    {
        Debug.Log($"[DragonatorWallet] TargetReceiveMatchInfo received on client. Address: {depositAddress}");
        if (BetUI.Instance == null)
        {
            Debug.LogError("[DragonatorWallet] BetUI.Instance is null!");
            return;
        }
        BetUI.Instance.ShowBetUI(depositAddress, amount);
    }
    private bool IsTxidUsed(string txid)
    {
        if (!File.Exists(usedTxidsPath)) return false;
        string[] lines = File.ReadAllLines(usedTxidsPath);
        foreach (var line in lines)
            if (line.Trim() == txid) return true;
        return false;
    }

    private void StoreTxid(string txid)
    {
        File.AppendAllText(usedTxidsPath, txid + "\n");
        Debug.Log($"[DragonatorWallet] TXID stored: {txid}");
    }

    [Command(requiresAuthority = false)]
    public void CmdSubmitTxid(string txid, string payoutAddress,
        NetworkConnectionToClient sender = null)
    {
        if (!betInfos.ContainsKey(sender))
        {
            Debug.LogWarning($"[DragonatorWallet] Unknown connection submitted TXID.");
            return;
        }

        // Check if TXID already used
        if (IsTxidUsed(txid))
        {
            Debug.LogWarning($"[DragonatorWallet] TXID already used: {txid}");
            TargetValidationResult(sender, false, "TXID already used.");
            return;
        }

        StoreTxid(txid);
        betInfos[sender].txid = txid;
        betInfos[sender].payoutAddress = payoutAddress;
        Debug.Log($"[DragonatorWallet] TXID received from {sender.connectionId}: {txid}");
        Debug.Log($"[DragonatorWallet] Payout address: {payoutAddress}");
        StartCoroutine(ValidateTxid(sender, txid));
    }

    [Server]
    private IEnumerator ValidateTxid(NetworkConnectionToClient conn, string txid)
    {
        // On validation start
        if (conn == betInfos.Keys.First())
            player1Status = player1Status.Split(':')[0] + ": Validating...";
        else
            player2Status = player2Status.Split(':')[0] + ": Validating...";

        string response = null;
        bool done = false;

        Task.Run(async () =>
        {
            try
            {
                var rpc = RpcHandler.GetInstance();
                response = await rpc.SendRpcRequest("gettransaction", new object[] { txid });
            }
            catch (Exception e)
            {
                Debug.LogError($"[DragonatorWallet] gettransaction failed: {e.Message}");
            }
            finally { done = true; }
        });

        while (!done) yield return null;

        if (string.IsNullOrEmpty(response))
        {
            TargetValidationResult(conn, false, "Transaction not found.");
            yield break;
        }

        var tx = JsonConvert.DeserializeObject<TransactionResponse>(response);
        bool correctAddress = false;
        bool correctAmount = false;

        if (tx?.result != null)
        {
            foreach (var detail in tx.result.details)
            {
                if (detail.address == matchDepositAddress && detail.category == "receive")
                {
                    correctAddress = true;
                    correctAmount = detail.amount >= betAmount;
                    break;
                }
            }
        }

        if (!correctAddress || !correctAmount)
        {
            Debug.LogWarning($"[DragonatorWallet] TX invalid.");
            TargetValidationResult(conn, false, "Invalid transaction.");
            yield break;
        }

        betInfos[conn].validated = true;

        // On success
        if (conn == betInfos.Keys.First())
            player1Status = player1Status.Split(':')[0] + ": <color=#00FF00>Validated</color>";
        else
            player2Status = player2Status.Split(':')[0] + ": <color=#00FF00>Validated</color>";

        Debug.Log($"[DragonatorWallet] TX validated for {conn.connectionId}.");
        TargetValidationResult(conn, true, "Transaction validated");
        CheckBothValidated();
    }

    [Server]
    private void CheckBothValidated()
    {
        foreach (var info in betInfos.Values)
        {
            if (!info.validated) return;
        }

        Debug.Log("[DragonatorWallet] Both players validated — starting match.");

        RpcHideStatusDisplay();

        NetworkConnectionToClient firstConn = null;
        foreach (var conn in betInfos.Keys)
        {
            firstConn = conn;
            break;
        }

        if (firstConn == null)
        {
            Debug.LogError("[DragonatorWallet] No connections found!");
            return;
        }

        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
            gameManager.StartGameForPlayer(firstConn.identity);
        else
            Debug.LogError("[DragonatorWallet] GameManager not found!");
    }

    [ClientRpc]
    private void RpcHideStatusDisplay()
    {
        BetUI.Instance?.HideStatusDisplay();
    }

    [Server]
    public void PayWinner(NetworkConnectionToClient winnerConn)
    {
        if (!betInfos.ContainsKey(winnerConn)) return;

        string winnerAddress = betInfos[winnerConn].payoutAddress;
        float payout = betAmount * 2f;

        StartCoroutine(SendPayout(winnerAddress, payout, winnerConn));
    }

    [Server]
    private IEnumerator SendPayout(string address, float amount, NetworkConnectionToClient winnerConn)
    {
        bool done = false;
        string txid = null;

        Task.Run(async () =>
        {
            try
            {
                SendToAddress sender = FindFirstObjectByType<SendToAddress>();
                txid = await sender.SendTransaction(address, (decimal)amount);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DragonatorWallet] Payout failed: {e.Message}");
            }
            finally { done = true; }
        });

        while (!done) yield return null;

        if (!string.IsNullOrEmpty(txid) && !txid.StartsWith("Error"))
        {
            Debug.Log($"[DragonatorWallet] Payout sent! TXID: {txid} to {address}");
            TargetShowWinnerTxid(winnerConn, txid); // call it here
        }
        else
            Debug.LogError($"[DragonatorWallet] Payout FAILED to {address}: {txid}");
    }

    [TargetRpc]
    private void TargetValidationResult(NetworkConnectionToClient conn,
        bool success, string message)
    {
        Debug.Log($"[DragonatorWallet] Validation: {success} — {message}");
        BetUI.Instance?.ShowValidationResult(success, message);
    }

    [TargetRpc]
    private void TargetShowWinnerTxid(NetworkConnectionToClient conn, string txid)
    {
        FindFirstObjectByType<OutcomeUI>()?.ShowWinnerTxid(txid);
    }
}

public class RpcResponse
{
    public string result;
    public object error;
}

public class TransactionResponse
{
    public TxResult result;
}

public class TxResult
{
    public string txid;
    public float amount;
    public List<TxDetail> details = new List<TxDetail>();
}

public class TxDetail
{
    public string address;
    public string category; // "send" or "receive"
    public float amount;
}