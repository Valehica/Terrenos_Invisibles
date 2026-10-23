using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Panel de diálogo. Tiene dos usos:
///  - <see cref="StartDialogue"/>: el texto fijo de un <see cref="Interactable"/> con los botones
///    Salir / Aceptar (los NPC de minijuego de Centro y Sur).
///  - <see cref="StartConversation"/>: varias líneas con el nombre de quien habla, que se van
///    escribiendo y avanzan con Enter, Espacio o clic. La usa el flujo de cada zona.
/// </summary>
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

    [Header("Conversaciones")]
    [SerializeField] private float letrasPorSegundo = 40f;
    [SerializeField] private Color colorNombre = new Color(1f, 0.82f, 0.5f);

    private Interactable currentTarget;

    // Conversación en curso.
    private IList<LineaDialogo> lineas;
    private int indice;
    private Action alTerminar;
    private bool escribiendo;
    private Coroutine escritura;
    private int frameApertura;
    private TMP_Text nombreText;
    private TMP_Text continuarText;

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

        // El flujo de la zona puede tomar la conversación de este NPC.
        if (target.AlHablar != null && target.AlHablar()) return;

        IsOpen = true;
        currentTarget = target;
        MostrarModoConversacion(false);

        if (dialogueText != null)
            dialogueText.text = target.DialogueText;

        if (acceptButton != null)
            acceptButton.gameObject.SetActive(target.StartsMinigame);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        target.GetComponent<OutlineHighlight>()?.HideOutline();

        if (cameraController != null)
            cameraController.EnterDialogueView(target.InteractionPoint);

        UIManager.Abrir(CapaUI.Dialogo, this);

        if (EventSystem.current != null && exitButton != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(exitButton.gameObject);
        }
    }

    /// <summary>
    /// Muestra las líneas una tras otra y al final llama a <paramref name="onEnd"/>.
    /// Con <paramref name="interlocutor"/> la cámara encuadra a los dos; sin él (monólogo) no se mueve.
    /// Devuelve false si ya había un diálogo abierto.
    /// </summary>
    public bool StartConversation(IList<LineaDialogo> lines, Transform interlocutor, Action onEnd)
    {
        if (IsOpen || lines == null || lines.Count == 0) return false;

        IsOpen = true;
        currentTarget = null;
        lineas = lines;
        indice = -1;
        alTerminar = onEnd;
        frameApertura = Time.frameCount;

        MostrarModoConversacion(true);
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);
        UIManager.Abrir(CapaUI.Conversacion, this);

        if (cameraController != null)
        {
            if (interlocutor != null)
            {
                interlocutor.GetComponent<OutlineHighlight>()?.HideOutline();
                cameraController.EnterDialogueView(interlocutor);
            }
            else
            {
                cameraController.ExitPhone();
            }
        }

        SiguienteLinea();
        return true;
    }

    private void Update()
    {
        if (lineas == null || Time.frameCount == frameApertura) return;

        bool avanzar = false;
        var kb = Keyboard.current;
        if (kb != null)
            avanzar = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            avanzar = true;
        if (!avanzar) return;

        if (escribiendo)
            CompletarLinea();
        else
            SiguienteLinea();
    }

    private void SiguienteLinea()
    {
        indice++;
        if (indice >= lineas.Count)
        {
            TerminarConversacion();
            return;
        }

        LineaDialogo l = lineas[indice];
        if (nombreText != null)
        {
            nombreText.text = l.pensando ? $"{l.quien} <size=70%><i>(pensando)</i></size>" : l.quien;
            nombreText.gameObject.SetActive(!string.IsNullOrEmpty(l.quien));
        }
        if (dialogueText != null)
            dialogueText.fontStyle = l.pensando ? FontStyles.Italic : FontStyles.Normal;

        if (escritura != null) StopCoroutine(escritura);
        escritura = StartCoroutine(Escribir(l.texto));
    }

    private IEnumerator Escribir(string texto)
    {
        escribiendo = true;
        if (continuarText != null) continuarText.gameObject.SetActive(false);
        if (dialogueText != null)
        {
            // maxVisibleCharacters respeta las etiquetas de texto enriquecido (<b>, <i>...).
            dialogueText.text = texto;
            dialogueText.maxVisibleCharacters = 0;
            dialogueText.ForceMeshUpdate();
            int total = dialogueText.textInfo.characterCount;
            float t = 0f;
            while (dialogueText.maxVisibleCharacters < total)
            {
                t += Time.unscaledDeltaTime * letrasPorSegundo;
                dialogueText.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(t));
                yield return null;
            }
        }
        FinLinea();
    }

    private void CompletarLinea()
    {
        if (escritura != null) StopCoroutine(escritura);
        FinLinea();
    }

    private void FinLinea()
    {
        escribiendo = false;
        if (dialogueText != null) dialogueText.maxVisibleCharacters = int.MaxValue;
        if (continuarText != null) continuarText.gameObject.SetActive(true);
    }

    private void TerminarConversacion()
    {
        lineas = null;
        Action fin = alTerminar;
        alTerminar = null;
        CloseDialogue();
        fin?.Invoke();
    }

    /// <summary>Esconde los botones y muestra el nombre y el aviso de continuar, o al revés.</summary>
    private void MostrarModoConversacion(bool conversacion)
    {
        if (conversacion) CrearEtiquetas();

        if (exitButton != null) exitButton.gameObject.SetActive(!conversacion);
        if (acceptButton != null && conversacion) acceptButton.gameObject.SetActive(false);
        if (nombreText != null) nombreText.gameObject.SetActive(conversacion);
        if (continuarText != null) continuarText.gameObject.SetActive(false);
        if (dialogueText != null)
        {
            dialogueText.fontStyle = FontStyles.Normal;
            dialogueText.maxVisibleCharacters = int.MaxValue;

            // En conversación no hay botones abajo, pero sí el nombre arriba: el texto baja y se estira.
            var r = dialogueText.rectTransform;
            if (!rectGuardado)
            {
                anclaTexto = r.anchorMin;
                bordeTexto = r.offsetMax;
                rectGuardado = true;
            }
            r.anchorMin = conversacion ? new Vector2(anclaTexto.x, 0.18f) : anclaTexto;
            r.offsetMax = conversacion ? bordeTexto + new Vector2(0f, -46f) : bordeTexto;
        }
    }

    private bool rectGuardado;
    private Vector2 anclaTexto, bordeTexto;

    // El panel de la escena solo trae el texto y los botones: el nombre y el aviso se agregan acá.
    private void CrearEtiquetas()
    {
        if (nombreText != null || dialoguePanel == null) return;
        nombreText = Etiqueta("Nombre", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -10f), new Vector2(700f, 44f), 32f, colorNombre);
        nombreText.fontStyle = FontStyles.Bold;
        nombreText.alignment = TextAlignmentOptions.TopLeft;
        continuarText = Etiqueta("Continuar", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 12f), new Vector2(400f, 34f), 22f, new Color(1f, 1f, 1f, 0.6f));
        continuarText.text = "Enter para continuar";
        continuarText.alignment = TextAlignmentOptions.BottomRight;
    }

    private TMP_Text Etiqueta(string nombre, Vector2 ancla, Vector2 pivote, Vector2 pos, Vector2 tam, float tamLetra, Color color)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(dialoguePanel.transform, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (dialogueText != null) t.font = dialogueText.font;
        t.fontSize = tamLetra;
        t.color = color;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = ancla;
        r.pivot = pivote;
        r.anchoredPosition = pos;
        r.sizeDelta = tam;
        return t;
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
        lineas = null;
        if (escritura != null) StopCoroutine(escritura);
        escribiendo = false;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (cameraController != null)
            cameraController.ExitDialogueView();

        UIManager.Cerrar(CapaUI.Dialogo);
        UIManager.Cerrar(CapaUI.Conversacion);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
