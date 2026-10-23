using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ChakuMinigameManager : MinigameBase
{
    public static ChakuMinigameManager Instance { get; private set; }

    [Header("Exploración (se desactiva durante el minijuego)")]
    [SerializeField] private GameObject explorationPlayer;
    [SerializeField] private Camera explorationCamera;

    [Header("Minijuego")]
    [SerializeField] private GameObject minigameRoot;
    [SerializeField] private Camera topDownCamera;
    [SerializeField] private ChainController chainController;
    [SerializeField] private VicunaHerdGroup[] herdGroups;

    [Header("Zonas")]
    [SerializeField] private BoxCollider corralZone;
    [SerializeField] private BoxCollider outerBounds;

    public BoxCollider CorralZone => corralZone;
    public BoxCollider OuterBounds => outerBounds;

    [Header("UI")]
    [SerializeField] private GameObject instructionsPanel;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button returnButton;
    [SerializeField] private TMP_Text collectedCounterText;

    private bool isRunning;
    private bool won;
    private int collectedCount;
    private readonly List<VicunaHerdMember> allMembers = new List<VicunaHerdMember>();
    private readonly List<Vector3> memberSpawnPositions = new List<Vector3>();

    private void Awake()
    {
        Instance = this;

        if (minigameRoot != null) minigameRoot.SetActive(false);
        if (topDownCamera != null) topDownCamera.gameObject.SetActive(false);
        if (returnButton != null) returnButton.onClick.AddListener(ReturnToExploration);

        // No es hijo de minigameRoot (vive en un Canvas aparte), así que hay que
        // ocultarlo a mano al arrancar el juego.
        if (collectedCounterText != null) collectedCounterText.gameObject.SetActive(false);
    }

    public override void StartMinigame()
    {
        if (isRunning) return;
        isRunning = true;
        won = false;
        UIManager.Abrir(CapaUI.Minijuego, this);

        if (explorationPlayer != null) explorationPlayer.SetActive(false);
        if (explorationCamera != null) explorationCamera.gameObject.SetActive(false);

        if (minigameRoot != null) minigameRoot.SetActive(true);
        if (topDownCamera != null) topDownCamera.gameObject.SetActive(true);

        if (chainController != null) chainController.SetInputEnabled(true);

        CacheSpawnPositions();
        ResetRound();

        if (instructionsPanel != null) instructionsPanel.SetActive(true);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (collectedCounterText != null) collectedCounterText.gameObject.SetActive(true);
    }

    private void CacheSpawnPositions()
    {
        if (allMembers.Count > 0) return;

        foreach (var group in herdGroups)
        {
            if (group == null) continue;
            foreach (var member in group.Members)
                allMembers.Add(member);
        }

        foreach (var member in allMembers)
            memberSpawnPositions.Add(member.transform.position);
    }

    private void ResetRound()
    {
        collectedCount = 0;
        UpdateCollectedUI();

        if (chainController != null) chainController.ResetChain();

        if (topDownCamera != null)
            topDownCamera.GetComponent<MinigameCamera>()?.SnapToTarget();

        for (int i = 0; i < allMembers.Count; i++)
        {
            if (allMembers[i] == null) continue;

            allMembers[i].ResetCollected();

            var agent = allMembers[i].GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.Warp(memberSpawnPositions[i]);
            else
                allMembers[i].transform.position = memberSpawnPositions[i];
        }
    }

    private void Update()
    {
        if (!isRunning)
        {
            // El minijuego ya terminó (panel de resultado visible): Enter equivale a
            // apretar el botón de volver, sin obligar a usar el mouse.
            if (resultPanel != null && resultPanel.activeSelf && Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                ReturnToExploration();
            }

            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ReturnToExploration();
            return;
        }

        CheckEscapeAndWin();
    }

    private void CheckEscapeAndWin()
    {
        if (outerBounds == null || corralZone == null) return;

        foreach (var member in allMembers)
        {
            if (member == null || member.IsCollected) continue;

            Vector3 pos = member.transform.position;

            if (!outerBounds.bounds.Contains(pos))
            {
                ResetRound();
                return;
            }

            if (corralZone.bounds.Contains(pos))
                CollectMember(member);
        }
    }

    private void CollectMember(VicunaHerdMember member)
    {
        member.SetCollected();
        collectedCount++;
        UpdateCollectedUI();

        if (collectedCount >= allMembers.Count)
            EndMinigame(true);
    }

    private void UpdateCollectedUI()
    {
        if (collectedCounterText != null)
            collectedCounterText.text = $"Vicuñas recolectadas: {collectedCount} / {allMembers.Count}";
    }

    private void EndMinigame(bool won)
    {
        isRunning = false;
        this.won = won;
        if (chainController != null) chainController.SetInputEnabled(false);

        if (instructionsPanel != null) instructionsPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText != null)
            resultText.text = won ? "¡Recolectaste todas las vicuñas!" : "Minijuego terminado.";
        UIManager.Abrir(CapaUI.ResultadoMinijuego, this);
    }

    public void ReturnToExploration()
    {
        bool estabaJugando = minigameRoot == null || minigameRoot.activeSelf;
        isRunning = false;

        // Los paneles viven en Canvas/ChakuUI, fuera de minigameRoot: si no se esconden acá el
        // resultado queda en pantalla, y su Enter seguiría llamando a este método.
        if (instructionsPanel != null) instructionsPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);

        if (minigameRoot != null) minigameRoot.SetActive(false);
        if (topDownCamera != null) topDownCamera.gameObject.SetActive(false);
        if (collectedCounterText != null) collectedCounterText.gameObject.SetActive(false);

        if (explorationPlayer != null) explorationPlayer.SetActive(true);
        if (explorationCamera != null) explorationCamera.gameObject.SetActive(true);

        UIManager.Cerrar(CapaUI.ResultadoMinijuego);
        UIManager.Cerrar(CapaUI.Minijuego);

        // Esc a mitad de ronda cuenta como abandonado; volver desde el resultado, como completado.
        bool completado = won;
        won = false;
        if (estabaJugando) AvisarTerminado(completado);
    }
}
