using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Socks5;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReplayUI : MonoBehaviour
{
    [SerializeField] private Button replayButton;
    [SerializeField] private GameObject replayPanel;
    [SerializeField] private TMP_InputField replayInput;
    [SerializeField] private Button replaySubmitButton;
    [SerializeField] private Button replayCloseButton;
    [SerializeField] private TMP_Text replayResultText;

    [Tooltip("Hidden while the replay panel is open, the same way the swap panel behaves.")]
    [SerializeField] private GameObject[] hideWhileOpen;

    private bool busy;

    void Start()
    {
        if (replayButton != null) replayButton.onClick.AddListener(OnReplayClicked);
        if (replaySubmitButton != null) replaySubmitButton.onClick.AddListener(OnSubmitClicked);
        if (replayCloseButton != null) replayCloseButton.onClick.AddListener(OnCloseClicked);

        if (replayPanel != null) replayPanel.SetActive(false);
    }

    private void OnReplayClicked()
    {
        if (replayPanel == null) return;

        replayPanel.SetActive(true);
        Show(Hint());

        Connect list = GetComponent<Connect>();
        if (list != null) list.ShowReplayList();

        foreach (GameObject hidden in Hidden()) hidden.SetActive(false);
    }

    private void OnCloseClicked()
    {
        if (replayPanel != null) replayPanel.SetActive(false);

        Connect list = GetComponent<Connect>();
        if (list != null) list.HideReplayList();

        foreach (GameObject hidden in Hidden()) hidden.SetActive(true);
    }

    private void OnSubmitClicked()
    {
        if (busy)
        {
            Show("Still fetching the last one.");
            return;
        }

        string typed = replayInput != null ? replayInput.text.Trim() : "";

        if (typed.Length == 0)
        {
            Show("Paste a match digest first.");
            return;
        }

        int picked;
        if (typed.Length <= 3 && int.TryParse(typed, out picked))
        {
            string fromList = ReplayList.DigestFor(picked);

            if (fromList.Length == 0)
            {
                Show("There is no match " + picked + " in the list on the left.");
                return;
            }

            StartCoroutine(Fetch("", fromList));
            return;
        }

        string onion = "";
        string digest = typed;

        int gap = typed.IndexOfAny(new[] { ' ', '\t' });
        if (gap > 0)
        {
            onion = typed.Substring(0, gap).Trim();
            digest = typed.Substring(gap + 1).Trim();
        }

        digest = digest.ToLowerInvariant();

        if (digest.Length != 64)
        {
            Show("A match digest is 64 characters. That one is " + digest.Length + ".");
            return;
        }

        StartCoroutine(Fetch(onion, digest));
    }

    private IEnumerator Fetch(string onion, string digest)
    {
        busy = true;

        string canonical = MatchReplayStore.Lookup(digest);

        if (!string.IsNullOrEmpty(canonical))
        {
            Show("Found that match on this machine.");
        }
        else if (onion.Length == 0)
        {
            Show("No match with that digest here. Add the server's onion address to fetch it instead." +
                 ((char)10) + PathLine(MatchReplayStore.FolderPath()));
            busy = false;
            yield break;
        }
        else
        {
            Show("Asking " + Shorten(onion) + " for the match...");

            string wire = "";
            string trouble = "";
            bool done = false;

            System.Threading.Thread worker = new System.Threading.Thread(() =>
            {
                try
                {
                    TcpClient tcp = new TcpClient();
                    tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort,
                                                 onion, TorConfig.MatchmakerPort)
                        .GetAwaiter().GetResult();

                    using (tcp)
                    using (NetworkStream stream = tcp.GetStream())
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        writer.WriteLine("GET_REPLAY|" + digest);
                        wire = reader.ReadLine() ?? "";
                    }
                }
                catch (Exception e)
                {
                    trouble = e.Message;
                }
                finally
                {
                    done = true;
                }
            });

            worker.IsBackground = true;
            worker.Start();

            while (!done) yield return null;

            if (trouble.Length > 0)
            {
                Show("That server could not be reached - " + trouble);
                busy = false;
                yield break;
            }

            if (wire.Trim().Length == 0)
            {
                Show("That server does not have this match, or does not keep replays.");
                busy = false;
                yield break;
            }

            canonical = wire.Replace(MatchReplayStore.Separator, "\n");
        }

        MatchReplay replay = MatchReplay.Parse(canonical);

        if (replay == null)
        {
            Show("That match could not be read.");
            busy = false;
            yield break;
        }

        if (replay.DigestHex() != digest)
        {
            Show("REFUSING to play that match - it does not match the digest you asked for.");
            busy = false;
            yield break;
        }

        Show("Playing back " + replay.playback.Count + " moves...");

        ReplayMatch watcher = ReplayMatch.Instance;

        if (watcher == null)
        {
            Show("This build cannot play replays.");
            busy = false;
            yield break;
        }

        if (!watcher.Watch(replay))
        {
            Show("That match could not be played - " + ReplayMatch.Trouble);
        }
        else if (string.IsNullOrEmpty(replay.cards))
        {
            Show("Playing back " + replay.playback.Count + " moves." + ((char)10) +
                 "This one was recorded before card sets were stamped, so it may not reproduce - " +
                 "watch the verdict at the end.");
        }

        busy = false;
    }

    public void PickFromList(int number)
    {
        string digest = ReplayList.DigestFor(number);

        if (digest.Length == 0)
        {
            Show("There is no match " + number + " in the list.");
            return;
        }

        if (replayInput != null) replayInput.text = digest;

        Show("Match " + number + " loaded. Press WATCH to play it.");
    }

    private static string Hint()
    {
        string folder = MatchReplayStore.FolderPath();
        string line = ((char)10).ToString();
        int found = 0;

        try
        {
            if (Directory.Exists(folder)) found = Directory.GetFiles(folder, "*.txt").Length;
        }
        catch (Exception)
        {
        }

        string count = found == 0
            ? "No matches saved yet."
            : found + " match" + (found == 1 ? "" : "es") + " saved.";

        return "<size=13>" + count + line +
               "Pick a number from the list, or paste a digest.</size>" + line + PathLine(folder);
    }

    private static string PathLine(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return "";

        string shown = folder.Replace('\\', '/');
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');

        if (home.Length > 0 && shown.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            shown = "~" + shown.Substring(home.Length);

        return "<size=13>" + shown + "</size>";
    }

    private GameObject[] Hidden()
    {
        return hideWhileOpen ?? new GameObject[0];
    }

    private void Show(string message)
    {
        if (replayResultText != null) replayResultText.text = message;

        Debug.Log("[Replay] " + message);
    }

    private static string Shorten(string onion)
    {
        if (string.IsNullOrEmpty(onion)) return "the server";

        return onion.Length <= 20 ? onion : onion.Substring(0, 16) + "...";
    }
}
