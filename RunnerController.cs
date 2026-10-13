using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

using Touch =
    UnityEngine.InputSystem.EnhancedTouch.Touch;

using TouchPhase =
    UnityEngine.InputSystem.TouchPhase;

[RequireComponent(typeof(CharacterController))]
public class RunnerController : MonoBehaviour
{
    [Header("Forward Movement")]
    [SerializeField]
    private float forwardSpeed = 8f;

    [Header("Lane Movement")]
    [SerializeField]
    private float laneDistance = 2.5f;

    [SerializeField]
    private float laneChangeDuration = 0.12f;

    [Header("Jump")]
    [SerializeField]
    private float jumpHeight = 2.2f;

    [SerializeField]
    private float gravity = -25f;

    [SerializeField, Range(0f, 0.1f)]
    private float coyoteTime = 0.1f;

    [SerializeField, Range(0f, 0.12f)]
    private float jumpInputBuffer = 0.12f;

    [Header("Slide")]
    [SerializeField]
    private float slideDuration = 0.8f;

    [SerializeField]
    private float slidingHeight = 1f;

    [Header("Hoverboard")]
    [Tooltip(
        "Player üzerindeki PowerUpController bağlanabilir. " +
        "Boş bırakılırsa otomatik bulunur."
    )]
    [SerializeField]
    private PowerUpController powerUpController;

    [Tooltip(
        "Hoverboard aktifken karakterin yerden yüksekliği."
    )]
    [SerializeField, Min(0.1f)]
    private float hoverboardHeight = 1.55f;

    [Tooltip(
        "Karakterin hoverboard yüksekliğine çıkma süresi."
    )]
    [SerializeField, Min(0.05f)]
    private float hoverboardRiseDuration = 0.40f;

    [Tooltip(
        "Hoverboard sona erdiğinde yere inme süresi."
    )]
    [SerializeField, Min(0.05f)]
    private float hoverboardLandingDuration = 0.55f;

    [Header("Swipe")]
    [SerializeField, Range(0.03f, 0.25f)]
    private float minimumSwipeFraction = 0.07f;

    [SerializeField]
    private float maximumSwipeDuration = 0.8f;

    [Header("Animation")]
    [SerializeField]
    private Animator animator;

    [SerializeField, Min(0.1f)]
    private float animationReferenceSpeed = 8f;

    [Header("Lane Change Visuals")]
    [SerializeField]
    private Transform laneVisualRoot;

    [SerializeField, Range(0f, 25f)]
    private float laneLeanAngle = 13f;

    [SerializeField, Min(0.01f)]
    private float laneLeanSmoothTime = 0.075f;

    [SerializeField, Min(0f)]
    private float laneLeanHoldDuration = 0.12f;

    [SerializeField, Min(0.1f)]
    private float minimumRunAnimationSpeed = 0.85f;

    [SerializeField, Min(0.1f)]
    private float maximumRunAnimationSpeed = 1.45f;

    public float CurrentForwardSpeed =>
        forwardSpeed;

    public Animator CurrentAnimator =>
        animator;

    public int CurrentLane => currentLane;
    public bool IsGrounded => isGrounded;
    public bool IsSliding => isSliding;
    public float LaneDistance => laneDistance;
    public float JumpHeight => powerUpController != null && powerUpController.IsJumpShoesActive ? Mathf.Max(jumpHeight, 6.2f) : jumpHeight;

    public bool IsHoverboardMovementActive =>
        IsHoverboardPowerActive ||
        hoverboardHeightOffset > 0.01f;

    private bool IsHoverboardPowerActive =>
        powerUpController != null &&
        powerUpController.IsHoverboardActive;

    private static readonly int
        SpeedParameter =
            Animator.StringToHash("Speed");

    private static readonly int
        SprintParameter =
            Animator.StringToHash("Sprint");

    private static readonly int
        GroundedParameter =
            Animator.StringToHash("Grounded");

    private static readonly int
        VerticalSpeedParameter =
            Animator.StringToHash("VerticalSp");

    private static readonly int
        SlideParameter =
            Animator.StringToHash("Slide");

    private static readonly int
        HoverboardParameter =
            Animator.StringToHash("Hoverboard");

    private CharacterController
        characterController;

    // 0 = sol, 1 = orta, 2 = sağ
    private int currentLane = 1;

    private float verticalVelocity;
    private float horizontalVelocity;
    private bool isGrounded;
    private float coyoteTimeRemaining;
    private float jumpBufferRemaining;
    private readonly RaycastHit[] hoverboardSurfaceHits = new RaycastHit[16];

    private Vector2 pointerStartPosition;
    private float pointerStartTime;
    private bool pointerIsDown;

    private bool isSliding;
    private Coroutine slideCoroutine;

    private float standingHeight;
    private Vector3 standingCenter;

    private float hoverboardHeightOffset;
    private bool previousHoverboardPowerState;

    private bool hasSpeedParameter;
    private bool hasSprintParameter;
    private bool hasGroundedParameter;
    private bool hasVerticalSpeedParameter;
    private bool hasSlideParameter;
    private bool hasHoverboardParameter;

    private Quaternion laneVisualInitialRotation;
    private Vector3 laneVisualInitialScale;
    private float laneLean;
    private float laneLeanVelocity;
    private float laneLeanTarget;
    private float laneLeanTimer;

    public event Action<int> LaneChanged;

    private void Awake()
    {
        characterController =
            GetComponent<CharacterController>();

        standingHeight =
            characterController.height;

        standingCenter =
            characterController.center;

        if (animator == null)
        {
            animator =
                GetComponentInChildren
                    <Animator>(true);
        }

        ResolvePowerUpController();

        if (laneVisualRoot == null && animator != null)
        {
            laneVisualRoot = animator.transform;
        }

        if (laneVisualRoot != null)
        {
            laneVisualInitialRotation = laneVisualRoot.localRotation;
            laneVisualInitialScale = laneVisualRoot.localScale;
        }

        ConfigureAnimator();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();

        pointerIsDown = false;
        verticalVelocity = 0f;
        isGrounded = characterController != null && characterController.isGrounded;
        coyoteTimeRemaining = isGrounded ? Mathf.Clamp(coyoteTime, 0f, 0.1f) : 0f;
        jumpBufferRemaining = 0f;

        ResolvePowerUpController();

        SetRunningAnimationState(true);
    }

    private void OnDisable()
    {
        if (EnhancedTouchSupport.enabled)
        {
            EnhancedTouchSupport.Disable();
        }

        pointerIsDown = false;
        jumpBufferRemaining = 0f;
        coyoteTimeRemaining = 0f;

        // Pause disables input, but must preserve the board's height and current pose.
        if (GameManager.Instance && GameManager.Instance.IsPaused) return;

        if (slideCoroutine != null)
        {
            StopCoroutine(slideCoroutine);
            slideCoroutine = null;
        }

        RestoreStandingState();
        ResetHoverboardMovement(true);

        if (
            animator != null &&
            hasSlideParameter
        )
        {
            animator.ResetTrigger(
                SlideParameter
            );
        }

        SetHoverboardAnimatorValue(false);
        ResetLaneVisuals();
        SetRunningAnimationState(false);

        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    private void Update()
    {
        if (Time.deltaTime <= 0f)
        {
            return;
        }

        ResolvePowerUpController();

        ReadKeyboardInput();
        ReadTouchInput();
        ReadMouseInput();

        float hoverboardVerticalMovement =
            UpdateHoverboardMovement();

        bool useHoverboardVerticalMovement =
            IsHoverboardMovementActive ||
            Mathf.Abs(
                hoverboardVerticalMovement
            ) > 0.00001f;

        if (useHoverboardVerticalMovement)
        {
            verticalVelocity = 0f;
        }
        MovePlayer(
            hoverboardVerticalMovement,
            useHoverboardVerticalMovement
        );

        UpdateLaneVisuals();
        UpdateAnimatorParameters();
    }

    private void ResolvePowerUpController()
    {
        if (powerUpController != null)
        {
            return;
        }

        powerUpController =
            GetComponent<PowerUpController>();

        if (powerUpController != null)
        {
            return;
        }

        powerUpController =
            PowerUpController.Instance;

        if (powerUpController != null)
        {
            return;
        }

        powerUpController =
            FindAnyObjectByType
                <PowerUpController>();
    }

    private void ReadKeyboardInput()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        bool moveLeft =
            Keyboard.current.leftArrowKey
                .wasPressedThisFrame ||
            Keyboard.current.aKey
                .wasPressedThisFrame;

        bool moveRight =
            Keyboard.current.rightArrowKey
                .wasPressedThisFrame ||
            Keyboard.current.dKey
                .wasPressedThisFrame;

        bool jumpPressed =
            Keyboard.current.spaceKey
                .wasPressedThisFrame ||
            Keyboard.current.upArrowKey
                .wasPressedThisFrame ||
            Keyboard.current.wKey
                .wasPressedThisFrame;

        bool slidePressed =
            Keyboard.current.downArrowKey
                .wasPressedThisFrame ||
            Keyboard.current.sKey
                .wasPressedThisFrame;

        if (moveLeft)
        {
            MoveLeft();
        }

        if (moveRight)
        {
            MoveRight();
        }

        if (jumpPressed)
        {
            TryJump();
        }

        if (slidePressed)
        {
            TrySlide();
        }
    }

    private void ReadTouchInput()
    {
        if (Touch.activeTouches.Count == 0)
        {
            return;
        }

        Touch touch =
            Touch.activeTouches[0];

        if (touch.phase == TouchPhase.Began)
        {
            BeginPointer(
                touch.screenPosition
            );

            return;
        }

        if (touch.phase == TouchPhase.Ended)
        {
            EndPointer(
                touch.screenPosition,
                true
            );

            return;
        }

        if (touch.phase == TouchPhase.Canceled)
        {
            EndPointer(
                touch.screenPosition,
                false
            );
        }
    }

    private void ReadMouseInput()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Mouse.current == null)
        {
            return;
        }

        if (
            Mouse.current.leftButton
                .wasPressedThisFrame
        )
        {
            BeginPointer(
                Mouse.current.position
                    .ReadValue()
            );
        }

        if (
            Mouse.current.leftButton
                .wasReleasedThisFrame
        )
        {
            EndPointer(
                Mouse.current.position
                    .ReadValue(),
                true
            );
        }
#endif
    }

    private void BeginPointer(
        Vector2 screenPosition
    )
    {
        var journey=GameManager.Instance?GameManager.Instance.GetComponent<CourierJourneyHUD>():null;
        if(journey&&journey.IsControlAt(screenPosition)){pointerIsDown=false;return;}
        pointerStartPosition =
            screenPosition;

        pointerStartTime =
            Time.unscaledTime;

        pointerIsDown = true;
    }

    private void EndPointer(
        Vector2 screenPosition,
        bool shouldProcessSwipe
    )
    {
        if (!pointerIsDown)
        {
            return;
        }

        float swipeDuration =
            Time.unscaledTime -
            pointerStartTime;

        Vector2 swipeDelta =
            screenPosition -
            pointerStartPosition;

        pointerIsDown = false;

        if (!shouldProcessSwipe)
        {
            return;
        }

        if (
            swipeDuration >
            maximumSwipeDuration
        )
        {
            return;
        }

        float minimumSwipeDistance =
            Mathf.Min(
                Screen.width,
                Screen.height
            ) * minimumSwipeFraction;

        if (
            swipeDelta.magnitude <
            minimumSwipeDistance
        )
        {
            return;
        }

        ProcessSwipe(swipeDelta);
    }

    private void ProcessSwipe(
        Vector2 swipeDelta
    )
    {
        bool isHorizontal =
            Mathf.Abs(swipeDelta.x) >
            Mathf.Abs(swipeDelta.y);

        if (isHorizontal)
        {
            if (swipeDelta.x > 0f)
            {
                MoveRight();
            }
            else
            {
                MoveLeft();
            }

            return;
        }

        if (swipeDelta.y > 0f)
        {
            TryJump();
        }
        else
        {
            TrySlide();
        }
    }

    private void MoveLeft()
    {
        int nextLane = Mathf.Max(0, currentLane - 1);
        if (nextLane == currentLane) return;

        currentLane = nextLane;
        BeginLaneChange(-1);
    }

    private void MoveRight()
    {
        int nextLane = Mathf.Min(2, currentLane + 1);
        if (nextLane == currentLane) return;

        currentLane = nextLane;
        BeginLaneChange(1);
    }

    private void BeginLaneChange(int direction)
    {
        laneLeanTarget = direction * laneLeanAngle;
        laneLeanTimer = laneLeanHoldDuration;
        LaneChanged?.Invoke(currentLane);
    }

    private void UpdateLaneVisuals()
    {
        if (laneVisualRoot == null) return;

        if (laneLeanTimer > 0f)
        {
            laneLeanTimer -= Time.deltaTime;
        }
        else
        {
            laneLeanTarget = 0f;
        }

        laneLean = Mathf.SmoothDampAngle(
            laneLean,
            laneLeanTarget,
            ref laneLeanVelocity,
            laneLeanSmoothTime
        );

        laneVisualRoot.localRotation = laneVisualInitialRotation *
            Quaternion.Euler(0f, 0f, laneLean);

        float leanStrength = laneLeanAngle <= 0.01f
            ? 0f
            : Mathf.Clamp01(Mathf.Abs(laneLean) / laneLeanAngle);
        laneVisualRoot.localScale = laneVisualInitialScale *
            (1f + leanStrength * 0.018f);
    }

    private void ResetLaneVisuals()
    {
        laneLean = 0f;
        laneLeanVelocity = 0f;
        laneLeanTarget = 0f;
        laneLeanTimer = 0f;

        if (laneVisualRoot != null)
        {
            laneVisualRoot.localRotation = laneVisualInitialRotation;
            laneVisualRoot.localScale = laneVisualInitialScale;
        }
    }
    private void TryJump()
    {
        if (IsHoverboardMovementActive || characterController == null || isSliding)
        {
            return;
        }

        jumpBufferRemaining = Mathf.Clamp(jumpInputBuffer, 0f, 0.12f);
        TryConsumeBufferedJump(true);
    }

    private void TryConsumeBufferedJump(bool immediateInput = false)
    {
        if (
            IsHoverboardMovementActive || isSliding ||
            (!immediateInput && jumpBufferRemaining <= 0f) ||
            (!isGrounded && coyoteTimeRemaining <= 0f)
        )
        {
            return;
        }

        jumpBufferRemaining = 0f;
        coyoteTimeRemaining = 0f;
        isGrounded = false;

        verticalVelocity =
            Mathf.Sqrt(
                JumpHeight *
                -2f *
                gravity
            );

        SetGroundedAnimatorValue(false);

        SetVerticalSpeedAnimatorValue(
            verticalVelocity
        );
    }

    private void TrySlide()
    {
        if (IsHoverboardMovementActive)
        {
            return;
        }

        if (
            characterController == null ||
            !isGrounded
        )
        {
            return;
        }

        if (isSliding)
        {
            return;
        }

        if (
            animator != null &&
            hasSlideParameter
        )
        {
            animator.ResetTrigger(
                SlideParameter
            );

            animator.SetTrigger(
                SlideParameter
            );
        }

        slideCoroutine =
            StartCoroutine(
                SlideRoutine()
            );
    }

    private IEnumerator SlideRoutine()
    {
        ApplySlidingState();

        yield return new WaitForSeconds(
            slideDuration
        );

        RestoreStandingState();

        slideCoroutine = null;
    }

    private void ApplySlidingState()
    {
        isSliding = true;

        characterController.height =
            slidingHeight;

        float centerDifference =
            (
                standingHeight -
                slidingHeight
            ) * 0.5f;

        characterController.center =
            new Vector3(
                standingCenter.x,
                standingCenter.y -
                centerDifference,
                standingCenter.z
            );
    }

    private void RestoreStandingState()
    {
        isSliding = false;

        if (characterController == null)
        {
            return;
        }

        characterController.height =
            standingHeight;

        characterController.center =
            standingCenter;
    }

    private float UpdateHoverboardMovement()
    {
        bool hoverboardPowerActive =
            IsHoverboardPowerActive;

        if (
            hoverboardPowerActive &&
            !previousHoverboardPowerState
        )
        {
            BeginHoverboardMovement();
        }

        previousHoverboardPowerState =
            hoverboardPowerActive;

        float targetHeight =
            hoverboardPowerActive
                ? hoverboardHeight
                : 0f;

        float movementDuration =
            hoverboardPowerActive
                ? hoverboardRiseDuration
                : hoverboardLandingDuration;

        float movementSpeed =
            hoverboardHeight /
            Mathf.Max(
                0.05f,
                movementDuration
            );

        float previousHeightOffset =
            hoverboardHeightOffset;

        hoverboardHeightOffset =
            Mathf.MoveTowards(
                hoverboardHeightOffset,
                targetHeight,
                movementSpeed *
                Time.deltaTime
            );

        if (
            !hoverboardPowerActive &&
            hoverboardHeightOffset <=
                0.0001f
        )
        {
            hoverboardHeightOffset = 0f;
        }

        return
            hoverboardHeightOffset -
            previousHeightOffset;
    }

    private void BeginHoverboardMovement()
    {
        verticalVelocity = 0f;
        jumpBufferRemaining = 0f;
        coyoteTimeRemaining = 0f;
        isGrounded = false;

        if (slideCoroutine != null)
        {
            StopCoroutine(slideCoroutine);
            slideCoroutine = null;
        }

        RestoreStandingState();

        if (
            animator != null &&
            hasSlideParameter
        )
        {
            animator.ResetTrigger(
                SlideParameter
            );
        }
    }

    private void ResetHoverboardMovement(
        bool restoreTransformHeight
    )
    {
        if (
            restoreTransformHeight &&
            hoverboardHeightOffset > 0f
        )
        {
            // Removing the flight offset must not teleport the capsule into
            // a roof if a collision prevented it reaching its full hover height.
            float heightToRemove = hoverboardHeightOffset;
            if (TryGetHoverboardSurfaceHeight(transform.position, out float surfaceHeight))
            {
                float standingY = surfaceHeight + RootHeightAboveFeet;
                heightToRemove = Mathf.Min(heightToRemove,
                    Mathf.Max(0f, transform.position.y - standingY));
            }
            transform.position -= Vector3.up * heightToRemove;
        }

        hoverboardHeightOffset = 0f;
        previousHoverboardPowerState = false;
        verticalVelocity = 0f;
    }

    private float RootHeightAboveFeet => characterController == null ? 1f :
        (characterController.height * 0.5f - characterController.center.y) *
        transform.lossyScale.y;

    private bool TryGetHoverboardSurfaceHeight(Vector3 nextPosition, out float surfaceHeight)
    {
        surfaceHeight = float.NegativeInfinity;
        if (characterController == null)
        {
            return false;
        }

        // A capsule-sized probe bridges narrow gaps between adjacent roofs.
        // Start close to the feet so a tall train beside a ground-level board
        // cannot be mistaken for a platform above the player.
        float radius = Mathf.Max(0.05f, characterController.radius *
            Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.95f);
        Vector3 origin = nextPosition;
        origin.y = transform.position.y - RootHeightAboveFeet + radius +
            Mathf.Max(0.12f, characterController.skinWidth + 0.05f);
        float probeDistance = Mathf.Max(16f, hoverboardHeight + characterController.height + 4f);
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, Vector3.down,
            hoverboardSurfaceHits, probeDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hoverboardSurfaceHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform) ||
                hit.normal.y < 0.45f)
            {
                continue;
            }

            RailRideSurface railSurface = hit.collider.GetComponentInParent<RailRideSurface>();
            if (railSurface == null && hit.collider.CompareTag("Obstacle"))
            {
                continue;
            }

            surfaceHeight = Mathf.Max(surfaceHeight, hit.point.y);
        }

        return !float.IsNegativeInfinity(surfaceHeight);
    }

    private void MovePlayer(
        float hoverboardVerticalMovement,
        bool useHoverboardVerticalMovement
    )
    {
        if (characterController == null || !characterController.enabled)
        {
            return;
        }

        if (movingTrain != null && movingTrain.isActiveAndEnabled && isGrounded && !useHoverboardVerticalMovement)
            characterController.Move(Vector3.forward * movingTrain.FrameDeltaZ);
        float runSpeed = forwardSpeed * (CourierCargoManifest.Instance ? CourierCargoManifest.Instance.SpeedMultiplier : 1f);
        float frameTime = Time.deltaTime;
        float estimatedTravel = Mathf.Max(runSpeed,
            Mathf.Max(Mathf.Abs(verticalVelocity), Mathf.Abs(horizontalVelocity))) * frameTime;
        estimatedTravel = Mathf.Max(estimatedTravel, Mathf.Abs(hoverboardVerticalMovement));
        int stepCount = Mathf.Clamp(Mathf.CeilToInt(
            Mathf.Max(frameTime * 90f, estimatedTravel / 0.35f)), 1, 64);
        float stepTime = frameTime / stepCount;

        for (int step = 0; step < stepCount; step++)
        {
            // A collision callback can stop the run in the middle of a move.
            if (!isActiveAndEnabled || !characterController.enabled) break;

            if (!useHoverboardVerticalMovement && isGrounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
                coyoteTimeRemaining = Mathf.Clamp(coyoteTime, 0f, 0.1f);
            }
            if (!useHoverboardVerticalMovement) TryConsumeBufferedJump();

            float nextX = Mathf.SmoothDamp(transform.position.x,
                (currentLane - 1) * laneDistance, ref horizontalVelocity,
                laneChangeDuration, Mathf.Infinity, stepTime);
            float forwardMovement = runSpeed * stepTime;
            float verticalMovement;

            if (useHoverboardVerticalMovement)
            {
                verticalVelocity = 0f;
                coyoteTimeRemaining = 0f;
                jumpBufferRemaining = 0f;
                verticalMovement = hoverboardVerticalMovement / stepCount;
                Vector3 nextPosition = new Vector3(nextX, transform.position.y,
                    transform.position.z + forwardMovement);

                if (TryGetHoverboardSurfaceHeight(nextPosition, out float surfaceHeight))
                {
                    float stepHeightOffset = hoverboardHeightOffset -
                        hoverboardVerticalMovement * (1f - (step + 1f) / stepCount);
                    float targetY = surfaceHeight + RootHeightAboveFeet + stepHeightOffset;
                    float followSpeed = Mathf.Max(runSpeed,
                        hoverboardHeight / Mathf.Max(0.05f,
                            IsHoverboardPowerActive ? hoverboardRiseDuration : hoverboardLandingDuration));
                    verticalMovement = Mathf.MoveTowards(transform.position.y, targetY,
                        followSpeed * stepTime) - transform.position.y;
                }
            }
            else
            {
                verticalMovement = verticalVelocity * stepTime +
                    0.5f * gravity * stepTime * stepTime;
                verticalVelocity += gravity * stepTime;
            }

            CollisionFlags collisions = characterController.Move(new Vector3(
                nextX - transform.position.x, verticalMovement, forwardMovement));

            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;

            isGrounded = !useHoverboardVerticalMovement &&
                (collisions & CollisionFlags.Below) != 0 && verticalVelocity <= 0f;
            if (isGrounded)
            {
                verticalVelocity = -2f;
                coyoteTimeRemaining = Mathf.Clamp(coyoteTime, 0f, 0.1f);
            }
            else
            {
                coyoteTimeRemaining = Mathf.Max(0f, coyoteTimeRemaining - stepTime);
            }

            jumpBufferRemaining = Mathf.Max(0f, jumpBufferRemaining - stepTime);
            if (!useHoverboardVerticalMovement) TryConsumeBufferedJump();
        }
    }

    private CourierMovingTrain movingTrain;
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y >= .45f) movingTrain = hit.collider.GetComponentInParent<CourierMovingTrain>();
    }

    private void ConfigureAnimator()
    {
        hasSpeedParameter = false;
        hasSprintParameter = false;
        hasGroundedParameter = false;
        hasVerticalSpeedParameter = false;
        hasSlideParameter = false;
        hasHoverboardParameter = false;

        if (animator == null)
        {
            Debug.LogWarning(
                "RunnerController: Animator bulunamadı.",
                this
            );

            return;
        }

        animator.applyRootMotion = false;
        animator.speed = 1f;

        foreach (
            AnimatorControllerParameter parameter
            in animator.parameters
        )
        {
            if (
                parameter.nameHash ==
                SpeedParameter
            )
            {
                hasSpeedParameter = true;
            }

            if (
                parameter.nameHash ==
                SprintParameter
            )
            {
                hasSprintParameter = true;
            }

            if (
                parameter.nameHash ==
                GroundedParameter
            )
            {
                hasGroundedParameter = true;
            }

            if (
                parameter.nameHash ==
                VerticalSpeedParameter
            )
            {
                hasVerticalSpeedParameter = true;
            }

            if (
                parameter.nameHash ==
                SlideParameter
            )
            {
                hasSlideParameter = true;
            }

            if (
                parameter.nameHash ==
                HoverboardParameter
            )
            {
                hasHoverboardParameter = true;
            }
        }

        if (!hasSlideParameter)
        {
            Debug.LogWarning(
                "RunnerController: Animator içinde Slide Trigger parametresi yok.",
                animator
            );
        }

        if (!hasHoverboardParameter)
        {
            Debug.LogWarning(
                "RunnerController: Animator içinde Hoverboard Bool parametresi yok.",
                animator
            );
        }
    }

    public void SetAnimator(
        Animator newAnimator
    )
    {
        if (newAnimator == null)
        {
            Debug.LogError(
                "RunnerController: Yeni Animator boş.",
                this
            );

            return;
        }

        animator = newAnimator;

        animator.applyRootMotion = false;
        animator.speed = 1f;

        animator.Rebind();
        animator.Update(0f);

        ConfigureAnimator();

        if (enabled)
        {
            SetRunningAnimationState(true);
        }
        else
        {
            SetRunningAnimationState(false);
        }
    }

    private void UpdateAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        bool hoverboardPowerActive =
            IsHoverboardPowerActive;

        SetHoverboardAnimatorValue(
            IsHoverboardMovementActive
        );

        SetSpeedAnimatorValue(1f);
        SetSprintAnimatorValue(true);

        SetGroundedAnimatorValue(
            hoverboardPowerActive ||
            isGrounded
        );

        SetVerticalSpeedAnimatorValue(
            IsHoverboardMovementActive
                ? 0f
                : verticalVelocity
        );

        UpdateAnimationPlaybackSpeed();
    }

    private void SetRunningAnimationState(
        bool isRunning
    )
    {
        if (animator == null)
        {
            return;
        }

        SetSpeedAnimatorValue(
            isRunning ? 1f : 0f
        );

        SetSprintAnimatorValue(
            isRunning
        );

        if (!isRunning)
        {
            SetHoverboardAnimatorValue(false);

            SetGroundedAnimatorValue(true);

            SetVerticalSpeedAnimatorValue(
                0f
            );

            if (hasSlideParameter)
            {
                animator.ResetTrigger(
                    SlideParameter
                );
            }
        }
    }

    private void SetSpeedAnimatorValue(
        float value
    )
    {
        if (
            animator == null ||
            !hasSpeedParameter
        )
        {
            return;
        }

        animator.SetFloat(
            SpeedParameter,
            value
        );
    }

    private void SetSprintAnimatorValue(
        bool value
    )
    {
        if (
            animator == null ||
            !hasSprintParameter
        )
        {
            return;
        }

        animator.SetBool(
            SprintParameter,
            value
        );
    }

    private void SetGroundedAnimatorValue(
        bool grounded
    )
    {
        if (
            animator == null ||
            !hasGroundedParameter
        )
        {
            return;
        }

        animator.SetBool(
            GroundedParameter,
            grounded
        );
    }

    private void SetVerticalSpeedAnimatorValue(
        float speed
    )
    {
        if (
            animator == null ||
            !hasVerticalSpeedParameter
        )
        {
            return;
        }

        animator.SetFloat(
            VerticalSpeedParameter,
            speed
        );
    }

    private void SetHoverboardAnimatorValue(
        bool active
    )
    {
        if (
            animator == null ||
            !hasHoverboardParameter
        )
        {
            return;
        }

        animator.SetBool(
            HoverboardParameter,
            active
        );
    }

    private void UpdateAnimationPlaybackSpeed()
    {
        if (
            animator == null ||
            characterController == null
        )
        {
            return;
        }

        /*
         * Hoverboard animasyonunu oyun hızına göre
         * hızlandırmıyoruz. Board stance doğal hızında
         * oynasın.
         */
        if (IsHoverboardPowerActive)
        {
            animator.speed = 1f;
            return;
        }

        if (
            isSliding ||
            !isGrounded
        )
        {
            animator.speed = 1f;
            return;
        }

        float speedMultiplier =
            forwardSpeed /
            animationReferenceSpeed;

        animator.speed =
            Mathf.Clamp(
                speedMultiplier,
                minimumRunAnimationSpeed,
                maximumRunAnimationSpeed
            );
    }

    public void SetForwardSpeed(
        float newSpeed
    )
    {
        forwardSpeed =
            Mathf.Max(
                0f,
                newSpeed
            );
    }

    private void OnValidate()
    {
        laneDistance =
            Mathf.Max(
                0.1f,
                laneDistance
            );

        laneChangeDuration =
            Mathf.Max(
                0.01f,
                laneChangeDuration
            );

        jumpHeight =
            Mathf.Max(
                0.1f,
                jumpHeight
            );

        slideDuration =
            Mathf.Max(
                0.05f,
                slideDuration
            );

        slidingHeight =
            Mathf.Max(
                0.1f,
                slidingHeight
            );

        hoverboardHeight =
            Mathf.Max(
                0.1f,
                hoverboardHeight
            );

        hoverboardRiseDuration =
            Mathf.Max(
                0.05f,
                hoverboardRiseDuration
            );

        hoverboardLandingDuration =
            Mathf.Max(
                0.05f,
                hoverboardLandingDuration
            );

        maximumSwipeDuration =
            Mathf.Max(
                0.05f,
                maximumSwipeDuration
            );

        animationReferenceSpeed =
            Mathf.Max(
                0.1f,
                animationReferenceSpeed
            );

        minimumRunAnimationSpeed =
            Mathf.Max(
                0.1f,
                minimumRunAnimationSpeed
            );

        maximumRunAnimationSpeed =
            Mathf.Max(
                minimumRunAnimationSpeed,
                maximumRunAnimationSpeed
            );
    }
}
