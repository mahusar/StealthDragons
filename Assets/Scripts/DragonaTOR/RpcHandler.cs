using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Core
{
    public class RpcHandler : MonoBehaviour
    {
        private static RpcHandler instance;

        private static readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public string rpcUser;
        public string rpcPassword;
        public string rpcUrl;

        public bool Configured { get; private set; }

        public static RpcHandler GetInstance()
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("RpcHandler");
                instance = obj.AddComponent<RpcHandler>();
                DontDestroyOnLoad(obj);
            }
            return instance;
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                LoadRpcSettings();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void LoadRpcSettings()
        {
            string configPath = Application.persistentDataPath + "/rpc.conf";
            if (System.IO.File.Exists(configPath))
            {
                string[] lines = System.IO.File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (key == "rpcuser") rpcUser = value;
                    else if (key == "rpcpassword") rpcPassword = value;
                    else if (key == "rpcurl") rpcUrl = value;
                }
                Configured = !string.IsNullOrEmpty(rpcUser)
                          && !string.IsNullOrEmpty(rpcPassword)
                          && !string.IsNullOrEmpty(rpcUrl);

                if (Configured)
                    Debug.Log($"[RpcHandler] Loaded RPC settings from {configPath} (url {rpcUrl})");
                else
                    Debug.LogError($"[RpcHandler] {configPath} is missing rpcuser/rpcpassword/rpcurl.");
            }
            else if (Mirror.Utils.IsHeadless())
            {
                Configured = false;
                Debug.LogError($"[RpcHandler] FATAL: no rpc.conf at {configPath}. " +
                               "Server cannot validate bets or pay out — shutting down.");
                Application.Quit(1);
            }
            else
            {
                rpcUser = PlayerPrefs.GetString("RPC_User", "defaultuser");
                rpcPassword = PlayerPrefs.GetString("RPC_Password", "defaultpassword");
                rpcUrl = PlayerPrefs.GetString("RPC_Url", "http://127.0.0.1:46502/");
                Configured = true;
                Debug.LogWarning($"[RpcHandler] No rpc.conf found, using PlayerPrefs ({rpcUrl}).");
            }
        }

        public void SaveRpcSettings(string user, string password, string url)
        {
            rpcUser = user;
            rpcPassword = password;
            rpcUrl = url;

            PlayerPrefs.SetString("RPC_User", rpcUser);
            PlayerPrefs.SetString("RPC_Password", rpcPassword);
            PlayerPrefs.SetString("RPC_Url", rpcUrl);
            PlayerPrefs.Save();
        }

        public async Task<string> SendRpcRequest(string method, object[] parameters = null)
        {
            if (!Configured)
            {
                Debug.LogError($"RPC Error ({method}): handler has no credentials.");
                return null;
            }

            RpcRequest request = new RpcRequest(method, parameters ?? new object[] { });
            string requestJson = JsonConvert.SerializeObject(request);

            var message = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{rpcUser}:{rpcPassword}"));
            message.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            try
            {
                HttpResponseMessage response = await http.SendAsync(message);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"RPC Error ({method}): " + ex.Message);
                return null;
            }
        }
    }

    [System.Serializable]
    public class RpcRequest
    {
        public string jsonrpc = "1.0";
        public string id = "1";
        public string method;
        public object[] @params;

        public RpcRequest(string method, object[] parameters = null)
        {
            this.method = method;
            this.@params = parameters ?? new object[] { };
        }
    }
}
