using UnityEngine;
public class TriggerButton : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;

    public Transform FeedbackRoot // animasyonun hangi objeye uygulanacağını belirleyen bir özellik 
    {
        get // bu özellik sadece okunabilir dışardan değiştirilemez
        {
            if (visualRoot != null) // eğer visualRoot atanmışsa onu döndür
            {
                return visualRoot;
            }
            else // eğer visualRoot atanmamışsa butonun kendi transform'unu döndür 
            {
                return transform;
            }
        }
    }
}