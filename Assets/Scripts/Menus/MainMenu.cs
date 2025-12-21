using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene("MAİN GAME İTCH.İO");
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
