using System.Collections;
using UnityEngine;

public class MinimapCheckpointMarker : MonoBehaviour
{
    public int checkpointIndex;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Renderer colorRenderer;

    private static readonly Color ReadyColor = Color.green;   
    private static readonly Color SelectedColor = Color.red;  

    private Coroutine cooldownRoutine;
    private bool isSelected;

    public bool IsOnCooldown { get; private set; }

    public Transform FeedbackRoot => visualRoot != null ? visualRoot : transform;

    void Awake()
    {
        if (colorRenderer == null) colorRenderer = GetComponent<Renderer>();
        ApplyColor(ReadyColor);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        if (IsOnCooldown) return;
        ApplyColor(selected ? SelectedColor : ReadyColor);
    }

    public void PlayCooldown(float duration)
    {
        if (cooldownRoutine != null) StopCoroutine(cooldownRoutine);
        cooldownRoutine = StartCoroutine(CooldownRoutine(duration));
    }

    private IEnumerator CooldownRoutine(float duration)
    {
        IsOnCooldown = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ApplyColor(Color.Lerp(SelectedColor, ReadyColor, elapsed / duration));
            yield return null;
        }

        IsOnCooldown = false;
        cooldownRoutine = null;
        ApplyColor(ReadyColor);
    }

    private void ApplyColor(Color color)
    {
        if (colorRenderer == null) return;
        if (colorRenderer.material.HasProperty("_BaseColor"))
        {
            Color current = colorRenderer.material.GetColor("_BaseColor");
            colorRenderer.material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, current.a));
        }
        if (colorRenderer.material.HasProperty("_Color"))
        {
            Color current = colorRenderer.material.GetColor("_Color");
            colorRenderer.material.SetColor("_Color", new Color(color.r, color.g, color.b, current.a));
        }
    }
}
