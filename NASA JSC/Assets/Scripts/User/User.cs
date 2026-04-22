using UnityEngine;

/// <summary>
/// Handles first-person player movement, camera look, jumping, and grounded checks.
/// </summary>
public class User : MonoBehaviour
{
    [Header ("Camera Settings")]
    // Camera Rotation
    public float mouseSensitivity = 2f;
    private float verticalRotation = 0f;
    private Transform cameraTransform;
    
    [Header("Movement Settings")]
    // Ground Movement
    private Rigidbody rb;
    public float MoveSpeed = 5f;
    private float moveHorizontal;
    private float moveForward;

    [Header("Jumping Settings")]
    // Jumping
    public float jumpForce = 10f;
    public float fallMultiplier = 2.5f; // Multiplies gravity when falling down
    public float ascendMultiplier = 2f; // Multiplies gravity for ascending to peak of jump
    private bool isGrounded = true;
    public LayerMask groundLayer;
    private float groundCheckTimer = 0f;
    private float groundCheckDelay = 0.3f;
    private float playerHeight;
    private float raycastDistance;

    /// <summary>
    /// Locks and hides the cursor before gameplay begins.
    /// </summary>
    private void Awake()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Caches required components and prepares the ground-check ray distance.
    /// </summary>
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        cameraTransform = Camera.main.transform;

        // Set the raycast to be slightly beneath the player's feet.
        playerHeight = GetComponent<CapsuleCollider>().height * transform.localScale.y;
        raycastDistance = (playerHeight / 2) + 0.2f;

        // Keep the mouse hidden and locked for first-person camera control.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Reads player input, rotates the camera, and checks whether the player has landed.
    /// </summary>
    void Update()
    {
        // Capture movement input each frame so FixedUpdate can apply it to physics.
        moveHorizontal = Input.GetAxisRaw("Horizontal");
        moveForward = Input.GetAxisRaw("Vertical");

        RotateCamera();

        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            Jump();
        }

        // Delay the ground check right after jumping so the player is not instantly considered grounded again.
        if (!isGrounded && groundCheckTimer <= 0f)
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            isGrounded = Physics.Raycast(rayOrigin, Vector3.down, raycastDistance, groundLayer);
        }
        else
        {
            groundCheckTimer -= Time.deltaTime;
        }

    }
    /// <summary>
    /// Applies movement and jump-related physics during the fixed physics step.
    /// </summary>
    void FixedUpdate()
    {
        MovePlayer();
        ApplyJumpPhysics();
    }
    /// <summary>
    /// Moves the player horizontally based on input while preserving vertical velocity.
    /// </summary>
    void MovePlayer()
    {

        // Build movement relative to the player's facing direction.
        Vector3 movement = (transform.right * moveHorizontal + transform.forward * moveForward).normalized;
        Vector3 targetVelocity = movement * MoveSpeed;

        // Apply horizontal movement directly to the Rigidbody while keeping current Y velocity.
        Vector3 velocity = rb.velocity;
        velocity.x = targetVelocity.x;
        velocity.z = targetVelocity.z;
        rb.velocity = velocity;

        // If we aren't moving and are on the ground, stop velocity so we don't slide
        if (isGrounded && moveHorizontal == 0 && moveForward == 0)
        {
            rb.velocity = new Vector3(0, rb.velocity.y, 0);
        }
    }
    /// <summary>
    /// Rotates the player horizontally and tilts the camera vertically for mouse look.
    /// </summary>
    void RotateCamera()
    {
        // Turn the player body left and right.
        float horizontalRotation = Input.GetAxis("Mouse X") * mouseSensitivity;
        transform.Rotate(0, horizontalRotation, 0);

        // Clamp vertical look so the camera cannot rotate past straight up or down.
        verticalRotation -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);

        cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
    }
    /// <summary>
    /// Starts a jump and temporarily disables grounded checks.
    /// </summary>
    void Jump()
    {
        isGrounded = false;
        groundCheckTimer = groundCheckDelay;
        rb.velocity = new Vector3(rb.velocity.x, jumpForce, rb.velocity.z); // Initial burst for the jump
    }
    /// <summary>
    /// Adjusts upward and downward velocity so the jump feels snappier.
    /// </summary>
    void ApplyJumpPhysics()
    {
        if (rb.velocity.y < 0) 
        {
            // Falling: Apply fall multiplier to make descent faster
            rb.velocity += Vector3.up * Physics.gravity.y * fallMultiplier * Time.fixedDeltaTime;
        } // Rising
        else if (rb.velocity.y > 0)
        {
            // Rising: Change multiplier to make player reach peak of jump faster
            rb.velocity += Vector3.up * Physics.gravity.y * ascendMultiplier  * Time.fixedDeltaTime;
        }
    }
}
