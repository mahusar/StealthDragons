using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DisplaySettings : MonoBehaviour
{
    private const string ModeKey = "display_mode";
    private const string WidthKey = "display_width";
    private const string HeightKey = "display_height";

    private const int MinWidth = 1280;
    private const int MinHeight = 720;

    [SerializeField] private TMP_Dropdown modeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    private static readonly FullScreenMode[] Modes =
    {
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.FullScreenWindow,
        FullScreenMode.Windowed
    };

    private static readonly string[] ModeLabels = { "Fullscreen", "Borderless", "Windowed" };

    private readonly List<Vector2Int> options = new List<Vector2Int>();
    private static bool appliedThisSession;

    void Awake()
    {
        if (Mirror.Utils.IsHeadless()) return;

        BuildOptions();

        if (appliedThisSession) return;
        appliedThisSession = true;
        Apply(LoadModeIndex(), LoadResolution(), false);
    }

    void Start()
    {
        if (Mirror.Utils.IsHeadless()) return;

        Populate();
    }

    private void BuildOptions()
    {
        options.Clear();
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();

        foreach (Resolution r in Screen.resolutions)
        {
            Vector2Int size = new Vector2Int(r.width, r.height);
            if (size.x < MinWidth || size.y < MinHeight) continue;
            if (seen.Add(size)) options.Add(size);
        }

        options.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        if (options.Count == 0)
        {
            options.Add(new Vector2Int(
                Mathf.Max(MinWidth, Screen.currentResolution.width),
                Mathf.Max(MinHeight, Screen.currentResolution.height)));
            Debug.LogWarning("DisplaySettings: no supported resolutions at or above " +
                             $"{MinWidth}x{MinHeight}; falling back to the current one.");
        }
    }

    private int LoadModeIndex()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, Modes.Length - 1);
    }

    private Vector2Int LoadResolution()
    {
        int w = PlayerPrefs.GetInt(WidthKey, 0);
        int h = PlayerPrefs.GetInt(HeightKey, 0);

        if (w >= MinWidth && h >= MinHeight) return new Vector2Int(w, h);

        return new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
    }

    private void Apply(int modeIndex, Vector2Int size, bool save)
    {
        modeIndex = Mathf.Clamp(modeIndex, 0, Modes.Length - 1);

        Screen.SetResolution(size.x, size.y, Modes[modeIndex]);
        Debug.Log($"DisplaySettings: {size.x}x{size.y} {ModeLabels[modeIndex]}.");

        if (!save) return;

        PlayerPrefs.SetInt(ModeKey, modeIndex);
        PlayerPrefs.SetInt(WidthKey, size.x);
        PlayerPrefs.SetInt(HeightKey, size.y);
        PlayerPrefs.Save();
    }

    private void Populate()
    {
        int modeIndex = LoadModeIndex();
        Vector2Int current = LoadResolution();

        if (modeDropdown != null)
        {
            modeDropdown.ClearOptions();
            modeDropdown.AddOptions(new List<string>(ModeLabels));
            modeDropdown.SetValueWithoutNotify(modeIndex);
            modeDropdown.onValueChanged.RemoveListener(OnModeChanged);
            modeDropdown.onValueChanged.AddListener(OnModeChanged);
        }

        if (resolutionDropdown == null) return;

        List<string> labels = new List<string>();
        int selected = 0;

        for (int i = 0; i < options.Count; i++)
        {
            labels.Add($"{options[i].x} x {options[i].y}");
            if (options[i] == current) selected = i;
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
        resolutionDropdown.SetValueWithoutNotify(selected);
        resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    private Vector2Int SelectedResolution()
    {
        if (resolutionDropdown == null || options.Count == 0) return LoadResolution();
        return options[Mathf.Clamp(resolutionDropdown.value, 0, options.Count - 1)];
    }

    public void OnModeChanged(int index)
    {
        Apply(index, SelectedResolution(), true);
    }

    public void OnResolutionChanged(int index)
    {
        int modeIndex = modeDropdown != null ? modeDropdown.value : LoadModeIndex();
        Apply(modeIndex, options[Mathf.Clamp(index, 0, options.Count - 1)], true);
    }
}
