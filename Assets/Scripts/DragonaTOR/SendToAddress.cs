using Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class SendToAddress : MonoBehaviour
{
    private RpcHandler rpcHandler;

    private void Start()
    {
        rpcHandler = RpcHandler.GetInstance();
    }
    public async Task<string> SendTransaction(string address, decimal amount)
    {
        string response = await rpcHandler.SendRpcRequest("sendtoaddress", new object[] { address, amount, "", "", true });

        if (!string.IsNullOrEmpty(response))
        {
            try
            {
                JObject jsonResponse = JObject.Parse(response);
                if (jsonResponse["error"] != null && jsonResponse["error"].Type != JTokenType.Null)
                {
                    return $"Error: {jsonResponse["error"].ToString()}";
                }

                if (jsonResponse["result"] != null && jsonResponse["result"].Type == JTokenType.String)
                {
                    return jsonResponse["result"].ToString(); 
                }

                return "Unexpected response format!";
            }
            catch (JsonException ex)
            {
                return $"Parsing error: {ex.Message}";
            }
        }

        return "Transaction failed!";
    }
}
