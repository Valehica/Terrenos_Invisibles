using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private Button exitButton;
    [SerializeField] private Button acceptButton;

    [Header("Camera")]
    [SerializeField] private ThirdPersonCameraController cameraController;

    private Interactable currentTarget;

    private void Awake()
    {
        Instance = this;
        IsOpen = false;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (exitButton != null)
            exitButton.onClick.AddListener(CloseDialogue);

        if (acceptButton != null)
            acceptButton.onClick.AddListener(AcceptMinigame);
    }

    public void StartDialogue(Interactable target)
    {
        if (target == null || IsOpen) return;

        IsOpen = true;
        currentTarget = target;

        if (dialogueText != null)
            dialogueText.text = target.DialogueText;

        if (acceptButton != null)
            acceptButton.gameObject.SetActive(target.StartsMinigame);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        target.GetComponent<OutlineHighlight>()?.HideOutline();

        if (cameraController != null)
            cameraController.EnterDialogueView(target.InteractionPoint);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (EventSystem.current != null && exitButton != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(exitButton.gameObject);
        }
    }

    private void AcceptMinigame()
    {
        // Cada NPC apunta a su propio minijuego; si no lo tiene asignado se
        // mantiene el comportamiento antiguo (el chaku de la zona norte).
        MinigameBase minigame = currentTarget != null ? currentTarget.Minigame : null;

        if (minigame != null)
            minigame.StartMinigame();
        else
            ChakuMinigameManager.Instance?.StartMinigame();

        CloseDialogue();
    }

    public void CloseDialogue()
    {
        if (!IsOpen) return;

        IsOpen = false;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (cameraController != null)
            cameraController.ExitDialogueView();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
