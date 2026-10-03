using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class IntroSequenceController : MonoBehaviour
{
    [System.Serializable]
    public class Animacion
    {
        public Sprite[] frames;
    }

    [System.Serializable]
    public class AnimacionesBloque
    {
        [Tooltip("Se muestran en orden, cambiando cada 'segundosPorAnimacion', y vuelven a empezar.")]
        public Animacion[] animaciones;
    }

    public static bool IsPlaying { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject introPanel;
    [SerializeField] private TMP_Text introText;
    [SerializeField] private GameObject continuePrompt;
    [SerializeField] private Image animacionImage;

    [Header("Narrativa")]
    [TextArea(3, 10)]
    [SerializeField]
    private string[] blocks =
    {
        "Maite sufre la pérdida de sus padres y se encuentra en un estado profundo de desconsuelo y estancamiento emocional.",
        "Al revisar las pertenencias familiares, Maite encuentra una caja que contiene postales de lugares remotos que sus padres recorrieron en un antiguo viaje. Movida por la necesidad de conectar con ellos, decide replicar exactamente esta ruta en su memoria.",
        "Maite se prepara para la travesía y se dirige al terminal, dispuesta a comenzar su recorrido en el primer poblado ubicado en la zona norte del país.",
        "¿Podrá Maite realizar este viaje, capturar la esencia de sus padres y encontrar el consuelo necesario para recuperarse de su pérdida?",
    };

    [Header("Ritmo")]
    [SerializeField] private float charactersPerSecond = 35f;

    [Header("Animaciones (una entrada por bloque; vacía = imagen en negro)")]
    [SerializeField] private AnimacionesBloque[] animacionesPorBloque;
    [SerializeField] private float segundosPorAnimacion = 2f;
    [SerializeField] private float framesPorSegundo = 8f;

    private float animTime;

    private int blockIndex = -1;
    private bool isTyping;
    private Coroutine typingRoutine;

    private void Awake()
    {
        IsPlaying = true;
        if (introPanel != null) introPanel.SetActive(true);
        if (continuePrompt != null) continuePrompt.SetActive(false);
    }

    private void Start()
    {
        ShowNextBlock();
    }

    private void Update()
    {
        UpdateAnimation();

        if (Keyboard.current == null) return;
        bool enterPressed = Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
        if (!enterPressed) return;

        if (isTyping)
            CompleteTyping();
        else
            ShowNextBlock();
    }

    private void ShowNextBlock()
    {
        blockIndex++;
        if (blockIndex >= blocks.Length)
        {
            EndIntro();
            return;
        }

        if (continuePrompt != null) continuePrompt.SetActive(false);
        animTime = 0f;
        UpdateAnimation();
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeBlock(blocks[blockIndex]));
    }

    private void UpdateAnimation()
    {
        if (animacionImage == null || blockIndex < 0) return;
        animTime += Time.unscaledDeltaTime;

        Animacion[] anims = blockIndex < (animacionesPorBloque?.Length ?? 0) ? animacionesPorBloque[blockIndex].animaciones : null;
        if (anims == null || anims.Length == 0)
        {
            animacionImage.sprite = null;
            animacionImage.color = Color.black;
            return;
        }

        // Cada animación dura 'segundosPorAnimacion' y dentro de ese tiempo repite sus frames en loop.
        float duracion = Mathf.Max(0.01f, segundosPorAnimacion);
        Animacion actual = anims[(int)(animTime / duracion) % anims.Length];
        if (actual.frames == null || actual.frames.Length == 0) return;

        float tLocal = animTime % duracion;
        animacionImage.sprite = actual.frames[(int)(tLocal * framesPorSegundo) % actual.frames.Length];
        animacionImage.color = Color.white;
    }

    private IEnumerator TypeBlock(string text)
    {
        isTyping = true;
        if (introText != null) introText.text = "";

        float delay = 1f / Mathf.Max(1f, charactersPerSecond);
        var wait = new WaitForSeconds(delay);

        for (int i = 0; i < text.Length; i++)
        {
            if (introText != null) introText.text += text[i];
            yield return wait;
        }

        FinishTyping();
    }

    private void CompleteTyping()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        if (introText != null) introText.text = blocks[blockIndex];
        FinishTyping();
    }

    private void FinishTyping()
    {
        isTyping = false;
        if (continuePrompt != null) continuePrompt.SetActive(true);
    }

    private void EndIntro()
    {
        IsPlaying = false;
        if (introPanel != null) introPanel.SetActive(false);
    }
}
