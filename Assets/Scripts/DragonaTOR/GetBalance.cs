using Core;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class GetBalance : MonoBehaviour
{
    private RpcHandler rpcHandler;

    private void Start()
    {
        rpcHandler = RpcHandler.GetInstance();
    }

    public async Task<float> FetchBalance(string account = "*", int minConf = 1)
    {
        if (rpcHandler == null)
        {
            Debug.LogError("RPC Handler not initialized!");
            return 0.0f;
        }

        object[] parameters = new object[] { account, minConf };
        string response = await rpcHandler.SendRpcRequest("getbalance", parameters);

        if (string.IsNullOrEmpty(response))
        {
            Debug.LogError("Error: No response from RPC!");
            return 0.0f;
        }

        try
        {
            JObject jsonResponse = JObject.Parse(response);
            if (jsonResponse["result"] != null)
            {
                return jsonResponse["result"].ToObject<float>();
            }
        }
        catch
        {
            Debug.LogError("Error: Failed to parse response!");
        }

        return 0.0f;
    }
}
