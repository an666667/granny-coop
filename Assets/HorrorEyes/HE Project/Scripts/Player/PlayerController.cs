using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using UnityEngine.UI;


public class PlayerController : MonoBehaviour {

    [Header("GeneralSettings")]
    [Tooltip("Object with GameControll.cs script")]
    public GameControll gameControll;
    [HideInInspector]
    public Inventory inventory;

    [Header("PlayerSettings")]
    [Tooltip("Player walk speed")]
    public float walkSpeed;
    [Tooltip("Player run speed")]
    public float runSpeed;
    [Tooltip("Player crouch speed")]
    public float crouchSpeed;
    [HideInInspector]
    public bool locked;
    [HideInInspector]
    public bool lockedByDying;
    private CharacterController characterController;
    private float moveSpeed;
    [HideInInspector]
    public bool playerMoving;
    [HideInInspector]
    public bool m_enemySeePlayer;

    [Header("Stamina Settings")]
    public Image m_staminaBar;
    public GameObject m_runArrownImage;
    public float m_stamina = 100;
    public float m_maxStamina = 100;
    [HideInInspector]
    public bool m_unlimitedStamina = false;
    public float m_staminaConsumptionSpeed;
    public float m_staminaRecoverySpeed;
    bool m_staminaRecovery;
    [HideInInspector]
    public bool m_running;

    [Header("CameraSettings")]
    [Tooltip("Mouse Sensetivity value")]
    public float mouseSensetivity;
    [Tooltip("Main camera transform")]
    public Transform cameraTransform;
    private float clampX;
    private float clampY;

    [Header("Camera Animations")]
    [Tooltip("Camera animation gameobject")]
    public Animation cameraAnimation;
    [Tooltip("Camera hit animation name")]
    public string cameraHitAnimName;
    [Tooltip("Camera idle animation name")]
    public string cameraIdleAnimName;
    [Tooltip("Camera move animation name")]
    public string cameraMoveAnimName;


    [Header("CrouchSettings")]  
    private float lerpSpeed  = 10f;
    [Tooltip("Player character controller normal height")]
    public float normalHeight;
    [Tooltip("Player character controller crouch height")]
    public float crouchHeight;
    [Tooltip("Player camera normal offset")]
    public float cameraNormalOffset;
    [Tooltip("Player camera crouch offset")]
    public float cameraCrouchOffset;
    [Tooltip("Player obstacle layers")]
    public LayerMask obstacleLayers;
    [Tooltip("Clamp camera axis")]
    public Vector2 clampXaxis;
    public Vector2 clampYaxis;
    [HideInInspector]
    public bool crouch = false;


    [Header("Sounds Settings")]
    public AudioSource m_breathAS;
    [Tooltip("Foot steps sounds")]
    public AudioClip[] footSteps;
    public AudioClip dyingSound;
    [HideInInspector]
    public AudioSource AS;

    [Header("Third Person Model Settings")]
    public Animator m_TPS_Player;

    [Header("Prone Settings")]
    [Tooltip("Capsule height while lying flat - low enough to crawl under a bed")]
    public float proneHeight = 0.55f;
    [Tooltip("Camera height while prone")]
    public float proneCameraOffset = 0.45f;
    [Tooltip("Movement speed while prone")]
    public float proneSpeed = 0.8f;
    [Tooltip("How far the model tips over while prone")]
    public float proneTilt = 78f;
    [HideInInspector] public bool prone;
    private Quaternion m_modelBaseRot = Quaternion.identity;

    [Header("Jump Settings")]
    [Tooltip("Upward speed applied when jumping")]
    public float jumpSpeed = 4.5f;
    [Tooltip("Downward acceleration while airborne")]
    public float gravity = 18f;
    private float m_verticalVelocity;
    private bool m_jumpRequested;


    private void Awake()
    {
        m_runArrownImage.SetActive(m_running);
        AS = GetComponent<AudioSource>();
        inventory = GetComponent<Inventory>();
        characterController = GetComponent<CharacterController>();
        clampX = 0f;
        moveSpeed = walkSpeed;
        if (m_TPS_Player != null) m_modelBaseRot = m_TPS_Player.transform.localRotation;


    }

    private void Update()
    {
        PlayerStates();
        if (!locked && !lockedByDying)
        {
            CameraRotation();       
            Movement();
            Controll();
            Stamina();
   
        }else
        {
            m_TPS_Player.SetFloat("DirectionY",0, 0.2f, Time.deltaTime);
            m_TPS_Player.SetFloat("DirectionX", 0, 0.2f, Time.deltaTime);
            m_TPS_Player.SetBool("Crouch", crouch);
            m_TPS_Player.SetBool("Run", m_running);
        }
    }

    /// <summary>Queued by the on-screen jump button; consumed in Movement().</summary>
    public void RequestJump()
    {
        if (prone) SetProne(false);      // stand up first
        m_jumpRequested = true;
    }

    /// <summary>Lying flat: crawl speed, low capsule, low camera.</summary>
    public void RequestProne() { SetProne(!prone); }

    public void SetProne(bool on)
    {
        prone = on;
        if (on)
        {
            crouch = false;
            m_running = false;
            if (m_runArrownImage != null) m_runArrownImage.SetActive(false);
            moveSpeed = proneSpeed;
        }
        else
        {
            moveSpeed = crouch ? crouchSpeed : (m_running ? runSpeed : walkSpeed);
        }
    }

    public void SetRun()
    {
     
        if (!m_running && !m_staminaRecovery)
        {
            m_running = true;
            m_runArrownImage.SetActive(m_running);

            if (!crouch)
            {
                moveSpeed = runSpeed;
            }
        }else
        {
            m_running = false;
            m_runArrownImage.SetActive(m_running);

            if (!crouch)
            {
                moveSpeed = walkSpeed;
            }else
            {
                moveSpeed = crouchSpeed;
            }
        }
    }

    private void Stamina()
    {

        m_staminaBar.fillAmount = m_stamina / 100f;

        if(m_running && !crouch)
        {
            if (characterController.velocity.magnitude > 3.5f && !m_unlimitedStamina)
            {
                m_stamina -= m_staminaConsumptionSpeed * Time.deltaTime;
                if (m_stamina <= 0 )
                {
                    m_running = false;
                    m_runArrownImage.SetActive(m_running);
                    m_staminaRecovery = true;
                    m_staminaBar.color = Color.red;
                    if(!crouch)
                    {
                        moveSpeed = walkSpeed;
                    }else
                    {
                        moveSpeed = crouchSpeed;
                    }
                }
            }else
            {
                if (m_stamina < m_maxStamina)
                {
                    m_stamina += m_staminaRecoverySpeed * Time.deltaTime;
                    if(m_stamina >= m_maxStamina)
                    {
                        m_staminaRecovery = false;
                        m_staminaBar.color = Color.green;
                    }
                }
            }
        }else
        {
            if(m_stamina < m_maxStamina)
            {
                m_stamina += m_staminaRecoverySpeed * Time.deltaTime;
                if (m_stamina >= m_maxStamina)
                {
                    m_staminaBar.color = Color.green;
                    m_staminaRecovery = false;
                }
            }
        }
    }

    private void PlayerStates()
    {
        if(m_enemySeePlayer)
        {
            if(m_breathAS.volume < 1)
            {
                m_breathAS.volume += 0.5f * Time.deltaTime;
            }
        }else
        {
            if (m_breathAS.volume > 0)
            {
                m_breathAS.volume -= 0.15f * Time.deltaTime;
            }
        }
    }

    private void Controll()
    {
           
        float newHeight = prone ? proneHeight : (crouch ? crouchHeight : normalHeight);
        characterController.height = Mathf.Lerp(characterController.height, newHeight, Time.deltaTime * lerpSpeed);

        characterController.center = Vector3.down * (normalHeight - characterController.height) / 2.0f;

        float newCamPos = prone ? proneCameraOffset : (crouch ? cameraCrouchOffset : cameraNormalOffset);
        Vector3 newPos = new Vector3(cameraTransform.localPosition.x, newCamPos, cameraTransform.localPosition.z);
        cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, newPos, Time.deltaTime *  lerpSpeed);

        if(CrossPlatformInputManager.GetButtonDown("Run"))
        {
            SetRun();
        }

        if (CrossPlatformInputManager.GetButtonDown("Crouch"))
        {
            SetCrouch();
        }

    }

    public void CatchPlayer(int state)
    {
        if (state == 1)
        {
            gameControll.ResetPlayerStates();
            m_breathAS.gameObject.SetActive(false);
            StopAllCoroutines();
            locked = true;
            lockedByDying = true;
            characterController.height = normalHeight;
            cameraTransform.localPosition = new Vector3(0f, cameraNormalOffset, 0f);
            crouch = false;
            moveSpeed = walkSpeed;
        }

        if(state == 2)
        {
            if (dyingSound)
            {
                AS.PlayOneShot(dyingSound);
            }
            gameControll.ScreenFade(2);
        }


    }

    private void Movement()
    {
        float inputX = CrossPlatformInputManager.GetAxis("Horizontal") * moveSpeed;
        float inputY = CrossPlatformInputManager.GetAxis("Vertical") * moveSpeed;

        Vector3 forvardMove = transform.forward * inputY;
        Vector3 sideMove = transform.right * inputX;

        // SimpleMove applies gravity itself but ignores any Y velocity, so it
        // cannot jump. Drive the controller manually instead.
        if (characterController.isGrounded)
        {
            m_verticalVelocity = -1f;                 // keep it pinned to the floor
            if (m_jumpRequested) m_verticalVelocity = jumpSpeed;
        }
        else
        {
            m_verticalVelocity -= gravity * Time.deltaTime;
        }
        m_jumpRequested = false;

        Vector3 motion = (forvardMove + sideMove) * Time.deltaTime
                         + Vector3.up * m_verticalVelocity * Time.deltaTime;
        characterController.Move(motion);



        // lying flat: tip the model over so the body is level with the ground
        if (m_TPS_Player != null)
        {
            Quaternion want = prone ? (m_modelBaseRot * Quaternion.Euler(proneTilt, 0f, 0f))
                                    : m_modelBaseRot;
            m_TPS_Player.transform.localRotation = Quaternion.Slerp(
                m_TPS_Player.transform.localRotation, want, Time.deltaTime * 8f);
        }

        m_TPS_Player.SetFloat("DirectionY", inputX, 0.2f, Time.deltaTime);
        m_TPS_Player.SetFloat("DirectionX", inputY, 0.2f, Time.deltaTime);
        m_TPS_Player.SetBool("Crouch", crouch);
        m_TPS_Player.SetBool("Run", m_running);

        if (characterController.velocity.magnitude > 0.5f)
        {
            playerMoving = true;
            cameraAnimation.Play(cameraMoveAnimName);
            cameraAnimation[cameraMoveAnimName].speed = characterController.velocity.magnitude / 3;       
        }
        else
        {
            playerMoving = false;
            cameraAnimation.Play(cameraIdleAnimName);         
        }


    }

    private void CameraRotation()
    {
        float mouseX = CrossPlatformInputManager.GetAxis("Mouse X") * (mouseSensetivity * 2) * Time.deltaTime;
        float mouseY = CrossPlatformInputManager.GetAxis("Mouse Y") * (mouseSensetivity * 2) * Time.deltaTime;

        // The CrossPlatform "Mouse X/Y" path is mouse-only on device, so on a phone
        // the view never turned. Fold in real touch swipes (already in degrees for
        // this frame) and let the clamping below keep handling the pitch limits.
        GrannyCoop.TouchLookInput.Tick();
        mouseX += GrannyCoop.TouchLookInput.YawDelta;
        mouseY += GrannyCoop.TouchLookInput.PitchDelta;

        clampX += mouseY;
        clampY += mouseX;

        if (clampX > clampXaxis.y)
        {
            clampX = clampXaxis.y;
            mouseY = 0.0f;
            ClampXAxis(clampXaxis.x);
        }
        else if (clampX < clampXaxis.x)
        {
            clampX = clampXaxis.x;
            mouseY = 0.0f;
            ClampXAxis(clampXaxis.y);
        }

        m_TPS_Player.SetFloat("SpineAngle", clampX * 1.3f, 0.2f, Time.deltaTime);
        cameraTransform.Rotate(Vector3.left * mouseY);
        transform.Rotate(Vector3.up * mouseX);
    }

    private void ClampXAxis(float value)
    {
        Vector3 camEuler = cameraTransform.eulerAngles;
        camEuler.x = value;
        cameraTransform.eulerAngles = camEuler;
    }

    private void ClampYAxis(float value)
    {
        Vector3 camEuler = transform.eulerAngles;
        camEuler.y = value;
        transform.eulerAngles = camEuler;
    }

    public void SetCrouch()
    {
        if (prone) { SetProne(false); return; }   // stand up instead of crouching
        
            if (!crouch)
            {
                crouch = true;
                moveSpeed = crouchSpeed;
            }
            else
            {
                if (CheckDistance() > normalHeight)
                {
                    crouch = false;
                    if (!m_running)
                    {
                        moveSpeed = walkSpeed;
                    } else
                    {
                        moveSpeed = runSpeed;
                    }
                }
            }
        
    }

    private float CheckDistance()
    {
        Vector3 pos = transform.position + characterController.center - new Vector3(0, characterController.height / 2, 0);
        RaycastHit hit;
        if (Physics.SphereCast(pos, characterController.radius, transform.up, out hit, 10, obstacleLayers))
        {
            return hit.distance;
        }
        else
            return 3;
    }

    public void FootStepPlay()
    {
        int r = Random.Range(1, footSteps.Length);
        AS.volume = moveSpeed / 6;
        AS.PlayOneShot(footSteps[r]);
    }


}



