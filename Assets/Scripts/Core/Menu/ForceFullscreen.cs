using UnityEngine;

public class ForceFullscreen : MonoBehaviour
{
    void Start()
    {
        // Set the application to fullscreen mode.
        Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, true);
    }   
    // Method to close the application
    public void CloseApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
