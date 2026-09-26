using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeOfDuty.Character;
using CodeOfDuty.Input;

public class CharacterMove : MonoBehaviour
{
    [Header("Components")]
    public CharacterController characterController;

    /// <summary>
    /// THE authority for what this character is doing. The legacy move-state classes still
    /// drive physics, but every transition is now reported here, so weapon/aim/traversal code
    /// can read one source of truth instead of private booleans.
    /// </summary>
    public readonly CharacterState characterState = new CharacterState();

    CharacterInput characterInput;

    /// <summary>
    /// What this character wants to do this frame, whoever is driving it.
    ///
    /// Movement states read intent from here instead of from the keyboard, which is the whole
    /// reason a bot can share their code: a bot writes into a <see cref="BotInputSource"/> and
    /// travels the identical path, with identical acceleration, identical state transitions and
    /// identical animation, rather than teleporting along a NavMesh.
    ///
    /// Resolved lazily and created on demand, so characters authored before the input seam
    /// existed keep working without every prefab needing a manual component add.
    /// </summary>
    public IInputSource InputSource
    {
        get
        {
            if (characterInput == null) characterInput = CharacterInput.For(this);
            return characterInput.Source;
        }
    }
    public BodyTurnHandler bodyTurnHandler;
    public Animator animator;
    public Transform directionOrienter;

    [Header("Colider values")]
    public float crouchColliderHeight = 1f;
    public float normalColliderHeight { get; private set; }
    public float grounCheckDistance;

    private bool _isGrounded;
    public bool isGrounded
    {
        get => _isGrounded;
        set
        {
            if (value == _isGrounded) return;

            _isGrounded = value;

            if (!_isGrounded && currentState != inAirState)
                SetState(inAirState);

            // landing thud for the local player, scaled by fall speed
            if (_isGrounded && NetOwnership.IsLocal(this))
                CameraShake.Land(velocity.y);

            characterState.IsGrounded = value;

            animator.SetBool("isGrounded", value);

            OnGroundedValueChange.Invoke(value);
        }
    }
    public LayerMask groundCheckMask;
    public delegate void isGroundedChange(bool changed);
    public event isGroundedChange OnGroundedValueChange;
    public float edgeFallMoveForce = 1f;
    public float noSlipDistance = .1f;


    [Header("Move values")]
    public float gravity = -9.81f;
    public float walkSpeed = 2;
    public float runSpeed = 3;
    public float sprintSpeed = 5;
    public float crouchSpeed = 1;

    public float jumpHeight = 1f;

    /// <summary>Runtime multiplier on sprintSpeed (driven by tac-sprint). 1 = normal sprint.</summary>
    [HideInInspector] public float sprintSpeedMultiplier = 1f;

    [Header("Velocity values")]
    public Vector3 moveVelocity;
    public Vector3 velocity;
    public Vector3 rollVelocity;
    public Vector3 edgeSlipVelocity;

    public MoveStateBase previousState;
    public MoveStateBase currentState;
    public StandState standState { get; private set; }
    public CrouchState crouchState { get; private set; }
    public RollState rollState { get; private set; }
    public JumpState jumpState { get; private set; }
    public InAirState inAirState { get; private set; }

    // animator ids
    public int horizontalInputID { get; private set; }
    public int verticalInputID { get; private set; }
    public int walkID { get; private set; }
    public int crouchID { get; private set; }
    public int isGroundID { get; private set; }
    public int sprintID { get; private set; }
    public int rollID { get; private set; }
    public int locomotionSpeedID { get; private set; }

    [Tooltip("Ground speed the standing locomotion clips were authored for (anim sync baseline).")]
    public float standAnimReferenceSpeed = 3f;
    [Tooltip("Ground speed the crouch locomotion clips were authored for (anim sync baseline).")]
    public float crouchAnimReferenceSpeed = 1.1f;

    IEnumerator colliderSizeChangeCor;

    private void Awake()
    {
        AssighAnimatorIDs();
        colliderSizeChangeCor = ColliderSizeChangeSmooth(false);
        characterController = GetComponent<CharacterController>();
        normalColliderHeight = characterController.height;


        standState = new StandState(this);
        crouchState = new CrouchState(this);
        rollState = new RollState(this);
        jumpState = new JumpState(this);
        inAirState = new InAirState(this);
    }

    private void Start()
    {
        currentState = standState;
        SetState(inAirState);
    }

    /// <summary>Maps a legacy move-state object onto the shared locomotion channel.</summary>
    LocomotionState ToLocomotion(MoveStateBase state)
    {
        if (state == crouchState) return LocomotionState.CrouchIdle;
        if (state == rollState) return LocomotionState.Slide;
        if (state == jumpState) return LocomotionState.Jump;
        if (state == inAirState) return LocomotionState.Air;
        if (state == standState) return LocomotionState.Idle;
        return LocomotionState.Idle;
    }

    public void SetState(MoveStateBase state)
    {
        if (currentState != null)
            currentState.OnStateExit();

        previousState = currentState;
        currentState = state;

        // report the transition to the single authority (forced: the kit owns the physics)
        characterState.RequestLocomotion(ToLocomotion(state), "CharacterMove.SetState", true);

        bool coliderReduce = currentState == crouchState | currentState == rollState;
        if (colliderSizeChangeCor != null) StopCoroutine(colliderSizeChangeCor);
        colliderSizeChangeCor = ColliderSizeChangeSmooth(coliderReduce);
        StartCoroutine(colliderSizeChangeCor);

        if (currentState != null)
            currentState.OnStateEnter();
    }

    private void Update()
    {
        if (currentState == null) return;

        GroundCheck();

        currentState.Tick();
    }

    /// <summary>
    /// True when there is room above to stand up from crouch.
    /// Ignores the player's own colliders (hitboxes, controller) and triggers,
    /// which is what made the old check always fail with "Can't get up".
    /// </summary>
    public bool CanStandUp()
    {
        float radius = Mathf.Max(0.05f, characterController.radius * 0.9f);
        Vector3 origin = transform.position + Vector3.up * (radius + characterController.skinWidth + 0.02f);
        float distance = Mathf.Max(0.01f, normalColliderHeight - radius * 2f - characterController.skinWidth);

        foreach (RaycastHit hit in Physics.SphereCastAll(origin, radius, Vector3.up, distance, groundCheckMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.root == transform.root) continue; // our own body
            return false;
        }

        return true;
    }

    void GroundCheck()
    {
        RaycastHit hitInfo;

        if (velocity.y <= 0 && Physics.SphereCast(transform.position + characterController.center, characterController.radius + characterController.skinWidth, Vector3.down, out hitInfo, grounCheckDistance, groundCheckMask, QueryTriggerInteraction.Ignore))
        {
            isGrounded = true;
            Vector3 relativeHitPoint = hitInfo.point - (transform.position + Vector3.right * characterController.center.x + Vector3.forward * characterController.center.z);

            Debug.DrawLine(transform.position + Vector3.up * 0.1f, transform.position + Vector3.up * 0.1f + Vector3.down * 0.3f, Color.red);

            if (characterController.velocity.y < 0 && relativeHitPoint.magnitude > noSlipDistance && !Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.3f, groundCheckMask))
            {
                Vector3 edgeFallMovement = transform.position - hitInfo.point;
                edgeFallMovement.y = 0;
                edgeSlipVelocity += (edgeFallMovement * Time.deltaTime * edgeFallMoveForce);
            }
            else
            {
                edgeSlipVelocity = Vector3.zero;
            }
        }
        else
        {
            isGrounded = false;
            edgeSlipVelocity = Vector3.zero;
        }

    }

    IEnumerator ColliderSizeChangeSmooth(bool reduce)
    {
        var startSize = characterController.height;
        var finalSize = reduce ? crouchColliderHeight : normalColliderHeight;
        var startCenter = characterController.center.y;
        var finalCener = finalSize / 2f;
        float t = 0;

        while (t < 0.3f)
        {
            characterController.height = Mathf.Lerp(startSize, finalSize, t / 0.3f);
            characterController.center = new Vector3(characterController.center.x, Mathf.Lerp(startCenter, finalCener, t / 0.3f), characterController.center.z);
            t += Time.deltaTime;
            yield return null;
        }
        characterController.height = finalSize;
        yield break;
    }

    private void AssighAnimatorIDs()
    {
        horizontalInputID = Animator.StringToHash("x");
        verticalInputID = Animator.StringToHash("y");
        isGroundID = Animator.StringToHash("isGround");
        sprintID = Animator.StringToHash("sprint");
        rollID = Animator.StringToHash("roll");
        walkID = Animator.StringToHash("walk");
        crouchID = Animator.StringToHash("crouch");
        locomotionSpeedID = Animator.StringToHash("locomotionSpeed");
    }

    float smoothedLocomotionSync = 1f;

    /// <summary>
    /// Keeps foot animation speed matched to actual ground speed so higher
    /// move speeds never cause foot sliding. referenceSpeed = the ground
    /// speed the locomotion clips were authored for.
    /// </summary>
    public void SyncLocomotionAnimation(float groundSpeed, float referenceSpeed)
    {
        float target = groundSpeed > 0.25f ? Mathf.Clamp(groundSpeed / referenceSpeed, 0.5f, 2.4f) : 1f;
        smoothedLocomotionSync = Mathf.Lerp(smoothedLocomotionSync, target, Time.deltaTime * 8f);
        animator.SetFloat(locomotionSpeedID, smoothedLocomotionSync);
    }

}
