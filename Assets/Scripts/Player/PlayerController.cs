using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float runSpeed = 6.0f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float crouchRunSpeed = 3.0f;


    [Header("Rotation")]
    [SerializeField] private float rotationSmoothTime = 0.12f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -15f;

        [Header("Jump")]
    [SerializeField] private float jumpHeight = 1.2f;

[Header("Crouch")]
    [SerializeField] private float crouchingHeight = 1.0f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int PickUpHash = Animator.StringToHash("PickUp");
    private static readonly int PickUpStateHash = Animator.StringToHash("PickUp");


    private Animator animator;
private CharacterController controller;
    private PlayerInteractor interactor;
    private float standingHeight;
    private float verticalVelocity;
    private float rotationVelocity;
    private bool isCrouching;

    public bool IsCrouching => isCrouching;

private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        interactor = GetComponent<PlayerInteractor>();
        standingHeight = controller.height;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

private void Update()
    {
        if (DialogueManager.IsOpen || IntroSequenceController.IsPlaying || PlayerStun.IsStunned)
        {
            UpdateAnimator(Vector2.zero, false, false, false);
            return;
        }

        if (ThirdPersonCameraController.IsFirstPerson)
        {
            Vector3 verticalMotion = Vector3.up * ApplyGravity(false);
            controller.Move(verticalMotion * Time.deltaTime);
            UpdateAnimator(Vector2.zero, false, false, false);
            return;
        }

        
if (IsPlayingPickUp())
        {
            Vector3 verticalMotion = Vector3.up * ApplyGravity(false);
            controller.Move(verticalMotion * Time.deltaTime);
            UpdateAnimator(Vector2.zero, false, false, false);
            return;
        }

        UpdateCrouchState();

        Vector2 moveInput = ReadMoveInput();
        bool wantsToRun = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        float targetSpeed = isCrouching
            ? (wantsToRun ? crouchRunSpeed : crouchSpeed)
            : (wantsToRun ? runSpeed : walkSpeed);
        bool jumpPressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && !isCrouching;
        bool pickUpPressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame
            && (interactor == null || !interactor.HasNearbyInteractable);

        MoveAndRotate(moveInput, targetSpeed, jumpPressed);
        UpdateAnimator(moveInput, wantsToRun, jumpPressed, pickUpPressed);
    }

    private Vector2 ReadMoveInput()
    {
        if (Keyboard.current == null) return Vector2.zero;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        return Vector2.ClampMagnitude(input, 1f);
    }

private void MoveAndRotate(Vector2 moveInput, float targetSpeed, bool jumpPressed)
    {
        float verticalSpeed = ApplyGravity(jumpPressed);
        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y);

        if (inputDirection.sqrMagnitude >= 0.01f)
        {
            float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cameraYaw;
            float smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref rotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);

            Vector3 moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            controller.Move(moveDirection * targetSpeed * Time.deltaTime);
        }

        controller.Move(Vector3.up * verticalSpeed * Time.deltaTime);
    }

private void UpdateAnimator(Vector2 moveInput, bool wantsToRun, bool jumpPressed, bool pickUpPressed)
    {
        if (animator == null) return;

        float speedMultiplier = isCrouching ? 1f : (wantsToRun ? 1f : 0.5f);
        animator.SetFloat(SpeedHash, moveInput.magnitude * speedMultiplier);
        animator.SetBool(IsCrouchingHash, isCrouching);
        animator.SetBool(IsGroundedHash, controller.isGrounded);

        if (jumpPressed)
            animator.SetTrigger(JumpHash);

        if (pickUpPressed)
            animator.SetTrigger(PickUpHash);
    }

private bool IsPlayingPickUp()
    {
        if (animator == null) return false;
        return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == PickUpStateHash;
    }



private float ApplyGravity(bool jumpPressed)
    {
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (jumpPressed)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        return verticalVelocity;
    }

private void UpdateCrouchState()
    {
        bool crouchTogglePressed = Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
        if (!crouchTogglePressed) return;

        if (isCrouching && HasCeilingAbove()) return;

        isCrouching = !isCrouching;
        float height = isCrouching ? crouchingHeight : standingHeight;
        controller.height = height;
        controller.center = new Vector3(0f, (height - standingHeight) / 2f, 0f);
    }

private bool HasCeilingAbove()
    {
        float skin = 0.05f;
        float currentTop = controller.center.y + controller.height / 2f;
        float standingTop = standingHeight / 2f;
        float checkDistance = standingTop - currentTop - skin;
        Vector3 origin = transform.position + Vector3.up * (currentTop + skin);
        return checkDistance > 0f && Physics.Raycast(origin, Vector3.up, checkDistance);
    }
}
