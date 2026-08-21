using UnityEngine;
using UnityEngine.UI;

public class PracticeButtonUI : MonoBehaviour
{
    public Button practiceButton;

    void Start()
    {
        if (practiceButton == null) practiceButton = GetComponent<Button>();

        if (practiceButton == null)
        {
            Debug.LogWarning("Practice Button not assigned in PracticeButtonUI.");
            return;
        }

        practiceButton.onClick.AddListener(OnPracticeClicked);
    }

    private void OnPracticeClicked()
    {
        PracticeMode practice = PracticeMode.Instance;
        if (practice == null)
        {
            Debug.LogError("PracticeButtonUI: PracticeMode.Instance is null - cannot start practice.");
            return;
        }

        if (ReplayMatch.Active || ReplayMatch.Finished)
        {
            Debug.Log("PracticeButtonUI: clearing leftover replay state before starting practice.");
            ReplayMatch.Clear();
        }

        practice.StartPractice();
    }
}
