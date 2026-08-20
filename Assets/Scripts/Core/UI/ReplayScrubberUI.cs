using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReplayScrubberUI : MonoBehaviour
{
    [SerializeField] private GameObject bar;
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text pauseLabel;
    [SerializeField] private Button slowerButton;
    [SerializeField] private Button fasterButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private TMP_Text statusText;

    private GameManager gameManager;
    private int shownTurn = -1;
    private bool shownPaused;
    private bool shownFinished;
    private float shownSpeed = -1f;

    void Start()
    {
        if (pauseButton != null) pauseButton.onClick.AddListener(ReplayMatch.TogglePause);
        if (slowerButton != null) slowerButton.onClick.AddListener(Slower);
        if (fasterButton != null) fasterButton.onClick.AddListener(Faster);
        if (restartButton != null) restartButton.onClick.AddListener(Restart);

        if (bar != null) bar.SetActive(false);
    }

    void Update()
    {
        bool watching = ReplayMatch.Active || ReplayMatch.Finished;

        if (bar != null && bar.activeSelf != watching) bar.SetActive(watching);

        if (!watching) return;

        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();

        int turn = gameManager != null ? gameManager.turnCount : 0;

        if (turn == shownTurn &&
            ReplayMatch.Paused == shownPaused &&
            ReplayMatch.Finished == shownFinished &&
            ReplayMatch.Speed == shownSpeed) return;

        shownTurn = turn;
        shownPaused = ReplayMatch.Paused;
        shownFinished = ReplayMatch.Finished;
        shownSpeed = ReplayMatch.Speed;

        Render(turn);
    }

    private void Render(int turn)
    {
        if (pauseLabel != null) pauseLabel.text = ReplayMatch.Paused ? "PLAY" : "PAUSE";

        if (statusText == null) return;

        if (ReplayMatch.Finished)
        {
            statusText.text = ReplayMatch.Verdict == "faithful"
                ? "Replay finished - it played back exactly as recorded."
                : "Replay finished - " + Describe();

            return;
        }

        string speed = ReplayMatch.Paused
            ? "paused"
            : ReplayMatch.Speed.ToString("0.##") + "x";

        statusText.text = ReplayMatch.TotalTurns > 0
            ? "Replay - turn " + turn + " of " + ReplayMatch.TotalTurns + " - " + speed
            : "Replay - turn " + turn + " - " + speed;
    }

    private string Describe()
    {
        if (!string.IsNullOrEmpty(ReplayMatch.Trouble)) return ReplayMatch.Trouble + ".";

        return "it did not reproduce the recorded match exactly.";
    }

    private void Slower()
    {
        ReplayMatch.SetSpeed(ReplayMatch.Speed / 2f);
        if (ReplayMatch.Paused) ReplayMatch.Resume();
    }

    private void Faster()
    {
        ReplayMatch.SetSpeed(ReplayMatch.Speed * 2f);
        if (ReplayMatch.Paused) ReplayMatch.Resume();
    }

    private void Restart()
    {
        ReplayMatch watcher = ReplayMatch.Instance;

        if (watcher == null)
        {
            Debug.LogWarning("ReplayScrubberUI: there is no replay to restart.");
            return;
        }

        shownTurn = -1;
        watcher.Restart();
    }
}
