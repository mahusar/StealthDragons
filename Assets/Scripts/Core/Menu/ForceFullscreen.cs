using UnityEngine;

public class ForceFullscreen : MonoBehaviour
{
    public void CloseApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
