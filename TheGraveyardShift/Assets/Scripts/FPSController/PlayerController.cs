﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Manages a first person character
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(AudioSource))]
public class PlayerController : MonoBehaviour
{
    [Header("Arms")]
    [Tooltip("The transform component that holds the gun camera.")]
    public Transform arms;
    [Tooltip("The position of the arms and gun camera relative to the fps controller GameObject."), SerializeField]
    private Vector3 armPosition;

    [Header("Flashlight")]
    [Tooltip("Flashlight game object.")]
    public GameObject flashlight;
    public Light flashlightComponent;
    private bool flashlightToggle;
    private bool flashlightDead;
    public float maxFlashlightLife;
    private float flashlightLife;

    [Header("Audio Clips")]
    [Tooltip("The audio clip that is played while walking."), SerializeField]
    private AudioClip walkingSound;

    [Tooltip("The audio clip that is played while running."), SerializeField]
    private AudioClip runningSound;

    [Header("Movement Settings")]
    [Tooltip("How fast the player moves while walking and strafing."), SerializeField]
    private float walkingSpeed = 5f;

    [Tooltip("How fast the player moves while running."), SerializeField]
    private float runningSpeed = 9f;

    [Tooltip("Approximately the amount of time it will take for the player to reach maximum running or walking speed."), SerializeField]
    private float movementSmoothness = 0.125f;

    [Tooltip("Amount of force applied to the player when jumping."), SerializeField]
    private float jumpForce = 35f;

    [Header("Look Settings")]
    [Tooltip("Rotation speed of the fps controller."), SerializeField]
    private float mouseSensitivity = 7f;

    [Tooltip("Approximately the amount of time it will take for the fps controller to reach maximum rotation speed."), SerializeField]
    private float rotationSmoothness = 0.05f;

    [Tooltip("Minimum rotation of the arms and camera on the x axis."),
        SerializeField]
    private float minVerticalAngle = -90f;

    [Tooltip("Maximum rotation of the arms and camera on the axis."),
        SerializeField]
    private float maxVerticalAngle = 90f;

    [Tooltip("The names of the axes and buttons for Unity's Input Manager."), SerializeField]
    private FpsInput input;
    private Rigidbody _rigidbody;
    private CapsuleCollider _collider;
    private AudioSource _audioSource;
    private SmoothRotation _rotationX;
    private SmoothRotation _rotationY;
    private SmoothVelocity _velocityX;
    private SmoothVelocity _velocityZ;
    private bool _isGrounded;

    [Header("Player Health")]
    public float maxHealth = 150f;
    private float health;

    [Header("HUD Options")]
    public ScreenController gameOverScreen;
    public ScreenController pauseScreen;
    public ScreenController controlsScreen;
    public GameObject hud;
    public GameObject healthBar;
    public GameObject healthBarBackground;
    public Slider batterySlider;
    public GameObject batteryBar;
    public GameObject dialogueBox;

    //Flow of the Game
    private bool introTutorial = false;
    private bool jumpTutorial = false;
    private bool fireTutorial = false;
    private bool keyInTheCity = false;
    private bool lookForKey = false;
    private bool findKey = false;
    private bool forestNotEntered = false;
    private bool cemeteryNotEntered = false;
    private bool inEscapeMenu = false;
    private bool playerIsDead = false;
    private bool playerEnteredChurch = false;

    private bool hasKey = false;

    private bool gaveReloadTip = false;

    private Objectives objectives;
    private EnemyHandler enemyHandler;


    private readonly RaycastHit[] _groundCastResults = new RaycastHit[8];
    private readonly RaycastHit[] _wallCastResults = new RaycastHit[8];

    /// Initializes the FpsController on start.
    private void Start()
    {
        Time.timeScale = 1;
        objectives = GameObject.FindWithTag("Objectives").GetComponent<Objectives>();
        enemyHandler = GameObject.FindWithTag("EnemyManager").GetComponent<EnemyHandler>();
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        _collider = GetComponent<CapsuleCollider>();
        _audioSource = GetComponent<AudioSource>();
        arms = AssignCharactersCamera();
        _audioSource.clip = walkingSound;
        _audioSource.loop = true;
        _rotationX = new SmoothRotation(RotationXRaw);
        _rotationY = new SmoothRotation(RotationYRaw);
        _velocityX = new SmoothVelocity();
        _velocityZ = new SmoothVelocity();
        Cursor.lockState = CursorLockMode.Locked;
        ValidateRotationRestriction();
        health = maxHealth;
        flashlightLife = maxFlashlightLife;
    }

    private Transform AssignCharactersCamera()
    {
        var t = transform;
        arms.SetPositionAndRotation(t.position, t.rotation);
        return arms;
    }

    /// Clamps <see cref="minVerticalAngle"/> and <see cref="maxVerticalAngle"/> to valid values and
    /// ensures that <see cref="minVerticalAngle"/> is less than <see cref="maxVerticalAngle"/>.
    private void ValidateRotationRestriction()
    {
        minVerticalAngle = ClampRotationRestriction(minVerticalAngle, -90, 90);
        maxVerticalAngle = ClampRotationRestriction(maxVerticalAngle, -90, 90);
        if (maxVerticalAngle >= minVerticalAngle) return;
        Debug.LogWarning("maxVerticalAngle should be greater than minVerticalAngle.");
        var min = minVerticalAngle;
        minVerticalAngle = maxVerticalAngle;
        maxVerticalAngle = min;
    }

    private static float ClampRotationRestriction(float rotationRestriction, float min, float max)
    {
        if (rotationRestriction >= min && rotationRestriction <= max) return rotationRestriction;
        var message = string.Format("Rotation restrictions should be between {0} and {1} degrees.", min, max);
        Debug.LogWarning(message);
        return Mathf.Clamp(rotationRestriction, min, max);
    }

    /// Checks if the character is on the ground.
    private void OnCollisionStay()
    {
        var bounds = _collider.bounds;
        var extents = bounds.extents;
        var radius = extents.x - 0.01f;
        Physics.SphereCastNonAlloc(bounds.center, radius, Vector3.down,
            _groundCastResults, extents.y - radius * 0.5f, ~0, QueryTriggerInteraction.Ignore);
        if (!_groundCastResults.Any(hit => hit.collider != null && hit.collider != _collider)) return;
        for (var i = 0; i < _groundCastResults.Length; i++)
        {
            _groundCastResults[i] = new RaycastHit();
        }

        _isGrounded = true;
    }

    /// Processes the character movement and the camera rotation every fixed framerate frame.
    private void FixedUpdate()
    {
        // FixedUpdate is used instead of Update because this code is dealing with physics and smoothing.
        RotateCameraAndCharacter();
        MoveCharacter();
        _isGrounded = false;
    }

    //void ChangeWeapon(int number)
    //{
    //    if (m_CurrentWeapon != -1)
    //    {
    //        m_Weapons[m_CurrentWeapon].PutAway();
    //        m_Weapons[m_CurrentWeapon].gameObject.SetActive(false);
    //    }

    //    m_CurrentWeapon = number;

    //    if (m_CurrentWeapon < 0)
    //        m_CurrentWeapon = m_Weapons.Count - 1;
    //    else if (m_CurrentWeapon >= m_Weapons.Count)
    //        m_CurrentWeapon = 0;

    //    m_Weapons[m_CurrentWeapon].gameObject.SetActive(true);
    //    m_Weapons[m_CurrentWeapon].Selected();
    //}

    /// Moves the camera to the character, processes jumping and plays sounds every frame.
    private void Update()
    {
        arms.position = transform.position + transform.TransformVector(armPosition);
        Jump();
        PlayFootstepSounds();
        ToggleFlashlight();
        FlashlightLife();

        if (health <= 0)
        {
            health = 0.1f;
            Time.timeScale = 0;
            gameOverScreen.Setup();
            hud.SetActive(false);
            playerIsDead = true;
        }

        if (Input.GetKeyDown(KeyCode.M) && !inEscapeMenu && !playerIsDead)
        {
            Time.timeScale = 0;
            pauseScreen.Setup();
            hud.SetActive(false);
            inEscapeMenu = true;
        }
        else if (Input.GetKeyDown(KeyCode.M) && inEscapeMenu && !playerIsDead)
        {
            pauseScreen.ResumeButton();
            controlsScreen.ResumeButton();
            inEscapeMenu = false;
        }
    }

    private void RotateCameraAndCharacter()
    {
        var rotationX = _rotationX.Update(RotationXRaw, rotationSmoothness);
        var rotationY = _rotationY.Update(RotationYRaw, rotationSmoothness);
        var clampedY = RestrictVerticalRotation(rotationY);
        _rotationY.Current = clampedY;
        var worldUp = arms.InverseTransformDirection(Vector3.up);
        var rotation = arms.rotation *
                        Quaternion.AngleAxis(rotationX, worldUp) *
                        Quaternion.AngleAxis(clampedY, Vector3.left);
        transform.eulerAngles = new Vector3(0f, rotation.eulerAngles.y, 0f);
        arms.rotation = rotation;
    }

    /// Returns the target rotation of the camera around the y axis with no smoothing.
    private float RotationXRaw
    {
        get { return input.RotateX * mouseSensitivity; }
    }

    /// Returns the target rotation of the camera around the x axis with no smoothing.
    private float RotationYRaw
    {
        get { return input.RotateY * mouseSensitivity; }
    }

    /// Clamps the rotation of the camera around the x axis
    /// between the <see cref="minVerticalAngle"/> and <see cref="maxVerticalAngle"/> values.
    private float RestrictVerticalRotation(float mouseY)
    {
        var currentAngle = NormalizeAngle(arms.eulerAngles.x);
        var minY = minVerticalAngle + currentAngle;
        var maxY = maxVerticalAngle + currentAngle;
        return Mathf.Clamp(mouseY, minY + 0.01f, maxY - 0.01f);
    }

    /// Normalize an angle between -180 and 180 degrees.
    /// <param name="angleDegrees">angle to normalize</param>
    /// <returns>normalized angle</returns>
    private static float NormalizeAngle(float angleDegrees)
    {
        while (angleDegrees > 180f)
        {
            angleDegrees -= 360f;
        }

        while (angleDegrees <= -180f)
        {
            angleDegrees += 360f;
        }

        return angleDegrees;
    }

    private void MoveCharacter()
    {
        var direction = new Vector3(input.Move, 0f, input.Strafe).normalized;
        var worldDirection = transform.TransformDirection(direction);
        var velocity = worldDirection * (input.Run ? runningSpeed : walkingSpeed);
        //Checks for collisions so that the character does not stuck when jumping against walls.
        var intersectsWall = CheckCollisionsWithWalls(velocity);
        if (intersectsWall)
        {
            _velocityX.Current = _velocityZ.Current = 0f;
            return;
        }

        var smoothX = _velocityX.Update(velocity.x, movementSmoothness);
        var smoothZ = _velocityZ.Update(velocity.z, movementSmoothness);
        var rigidbodyVelocity = _rigidbody.velocity;
        var force = new Vector3(smoothX - rigidbodyVelocity.x, 0f, smoothZ - rigidbodyVelocity.z);
        _rigidbody.AddForce(force, ForceMode.VelocityChange);
    }

    public void TakeDamage(float damage)
    {
        health -= damage;
        float healthPercentage = health / maxHealth;

        RectTransform rt = (RectTransform)healthBar.transform;
        rt.sizeDelta = new Vector2(healthPercentage * 1000, 20);

        RectTransform rt2 = (RectTransform)healthBarBackground.transform;
        rt2.sizeDelta = new Vector2(healthPercentage * 1000 + 2, 22);
    }

    private bool CheckCollisionsWithWalls(Vector3 velocity)
    {
        if (_isGrounded) return false;
        var bounds = _collider.bounds;
        var radius = _collider.radius;
        var halfHeight = _collider.height * 0.5f - radius * 1.0f;
        var point1 = bounds.center;
        point1.y += halfHeight;
        var point2 = bounds.center;
        point2.y -= halfHeight;
        Physics.CapsuleCastNonAlloc(point1, point2, radius, velocity.normalized, _wallCastResults,
            radius * 0.04f, ~0, QueryTriggerInteraction.Ignore);
        var collides = _wallCastResults.Any(hit => hit.collider != null && hit.collider != _collider);
        if (!collides) return false;
        for (var i = 0; i < _wallCastResults.Length; i++)
        {
            _wallCastResults[i] = new RaycastHit();
        }

        return true;
    }

    private void Jump()
    {
        if (!_isGrounded || !input.Jump) return;
        _isGrounded = false;
        _rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    private void ToggleFlashlight()
    {
        if (input.Flashlight && !flashlightDead)
        {
            flashlightToggle = !flashlightToggle;

            if (flashlightToggle)
                flashlight.SetActive(true);
            else if (!flashlightToggle)
                flashlight.SetActive(false);
        }
    }

    private void FlashlightLife()
    {
        //RectTransform rt = (RectTransform)batteryBar.transform;

        if (flashlightLife > 0)
            flashlightDead = false;

        if (flashlightToggle)
        {
            if (flashlightLife >= maxFlashlightLife || flashlightLife > 60f)
            {
                flashlightComponent.intensity = 2f;
                batteryBar.GetComponent<Image>().color = Color.green;
                //rt.SetPositionAndRotation(new Vector3(-115, 115, 0), new Quaternion(0, 0, 0, 0));
            }

            flashlightLife -= Time.deltaTime;
            float batteryPercentage = flashlightLife / maxFlashlightLife;

            if (flashlightLife <= 0)
            {
                flashlightDead = true;
                flashlightToggle = false;
                flashlight.SetActive(false);
            }
            else if (flashlightLife <= maxFlashlightLife / 4)
            {
                flashlightComponent.intensity = 0.5f;
                batteryBar.GetComponent<Image>().color = Color.red;
            }
            else if (flashlightLife <= maxFlashlightLife / 2)
            {
                flashlightComponent.intensity = 1f;
                batteryBar.GetComponent<Image>().color = Color.yellow;
            }

            batterySlider.value = batteryPercentage;


            //rt.sizeDelta = new Vector2(batteryPercentage * 180, 55);
            //Vector2 moveRight = new Vector2(1, 0);
            //rt.Translate(moveRight * Time.deltaTime * 1.62f, Camera.main.transform);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Medkit")
        {
            if (health < maxHealth)
            {
                TakeDamage(-(maxHealth / 3));
                if (health > maxHealth)
                    health = maxHealth;
                Destroy(other.gameObject);
            }
        }
        else if (other.gameObject.tag == "Ammo")
        {
            int index = gameObject.GetComponent<SwitchWeapon>().currentWpnIndex;
            GameObject[] weapons = gameObject.GetComponent<SwitchWeapon>().weapons;
            weapons[index].GetComponent<Gun>().AddAmmo();
            Destroy(other.gameObject);
        }
        else if (other.gameObject.tag == "Battery")
        {
            if (flashlightLife < maxFlashlightLife)
            {
                flashlightLife += (maxFlashlightLife / 2);
                float batteryPercentage = flashlightLife / maxFlashlightLife;
                if (flashlightLife > maxFlashlightLife)
                    flashlightLife = maxFlashlightLife;
                Destroy(other.gameObject);
                batterySlider.value = batteryPercentage;
            }
        }
        else if (other.gameObject.tag == "Secret")
        {
            Destroy(other.gameObject);
        }
        else if (other.gameObject.tag == "JumpLog" && !jumpTutorial)
        {
            string[] newText = { "Common James, remember boot camp...", "You have to press the Space Key to jump!" };

            dialogueBox.GetComponent<Dialogue>().SetText(newText);
            dialogueBox.GetComponent<Dialogue>().Start();

            jumpTutorial = true;
        }
        else if (other.gameObject.tag == "FireTutorial" && !fireTutorial)
        {
            objectives.CompleteObjective();
            string[] newText = { "Oh no, Enemies incoming!", "Remember, left mouse click is to shoot and right mouse click is to aim!" };

            dialogueBox.GetComponent<Dialogue>().SetText(newText);
            dialogueBox.GetComponent<Dialogue>().Start();

            fireTutorial = true;
        }
        else if (other.gameObject.tag == "keyInTheCity" && !keyInTheCity)
        {
            if (enemyHandler.CheckIfDead(3))
            {
                objectives.CompleteObjective();
            }
            string[] newText = { "Looks like there is a city over here... I might want to go check for survivors here..." };

            dialogueBox.GetComponent<Dialogue>().SetText(newText);
            dialogueBox.GetComponent<Dialogue>().Start();

            keyInTheCity = true;
        }
        else if (other.gameObject.tag == "keyInTheCity" && !keyInTheCity)
        {
            if (enemyHandler.CheckIfDead(3))
            {
                objectives.CompleteObjective();
            }
            string[] newText = { "Looks like there is a city over here... I might want to go check for survivors here..." };

            dialogueBox.GetComponent<Dialogue>().SetText(newText);
            dialogueBox.GetComponent<Dialogue>().Start();

            keyInTheCity = true;
        }
        else if (other.gameObject.tag == "Key")
        {
            hasKey = true;
            Destroy(other.gameObject);
            objectives.CompleteObjective();
        }
        else if (other.gameObject.tag == "EnterChurch" && !playerEnteredChurch)
        {
            playerEnteredChurch = true;
            objectives.CompleteObjective();
        }
        else if (other.gameObject.tag == "FindKey")
        {
            if (hasKey)
            {
                objectives.CompleteObjective();
                TransitionManagerClass.Transition("BossMap");
            }
            if (!findKey && !hasKey)
            {
                string[] newText = { "The gate is locked... There may be a key in a near by cemetery..." };

                dialogueBox.GetComponent<Dialogue>().SetText(newText);
                dialogueBox.GetComponent<Dialogue>().Start();

                findKey = true;
            }
        }
        else if (other.gameObject.tag == "EnterForest" && !forestNotEntered)
        {
            forestNotEntered = true;
            objectives.CompleteObjective();
        }
        else if (other.gameObject.tag == "EnterCemetery" && !cemeteryNotEntered)
        {
            cemeteryNotEntered = true;
            objectives.CompleteObjective();
        }
        else if (other.gameObject.tag == "Gate")
        {
            //if (!lookForKey) 
            //{
            //    string[] newText = { "This gate is locked! Maybe I could find the key for it in the city..." };

            //    dialogueBox.GetComponent<Dialogue>().SetText(newText);
            //    dialogueBox.GetComponent<Dialogue>().Start();

            //    lookForKey = true;
            //}
            //else
            //{
            if (enemyHandler.CheckIfDead(enemyHandler.initialCount))
            {
                objectives.CompleteObjective();
                TransitionManagerClass.Transition("MainMap");
            }
            
        }
       
    }

    public void ReloadTip()
    {
        if (!gaveReloadTip && SceneManager.GetActiveScene().name == "TutorialMap")
        {
            gaveReloadTip = true;
            string[] newText = { "I am out of ammo! That gun is not going to reload itself. I must press 'r' to reload it." };

            dialogueBox.GetComponent<Dialogue>().SetText(newText);
            dialogueBox.GetComponent<Dialogue>().Start();
        }
        
    }

    private void PlayFootstepSounds()
    {
        if (_isGrounded && _rigidbody.velocity.sqrMagnitude > 0.1f)
        {
            _audioSource.clip = input.Run ? runningSound : walkingSound;
            if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }
        else
        {
            if (_audioSource.isPlaying)
            {
                _audioSource.Pause();
            }
        }
    }

    /// A helper for assistance with smoothing the camera rotation.
    private class SmoothRotation
    {
        private float _current;
        private float _currentVelocity;

        public SmoothRotation(float startAngle)
        {
            _current = startAngle;
        }

        /// Returns the smoothed rotation.
        public float Update(float target, float smoothTime)
        {
            return _current = Mathf.SmoothDampAngle(_current, target, ref _currentVelocity, smoothTime);
        }

        public float Current
        {
            set { _current = value; }
        }
    }

    /// A helper for assistance with smoothing the movement.
    private class SmoothVelocity
    {
        private float _current;
        private float _currentVelocity;

        /// Returns the smoothed velocity.
        public float Update(float target, float smoothTime)
        {
            return _current = Mathf.SmoothDamp(_current, target, ref _currentVelocity, smoothTime);
        }

        public float Current
        {
            set { _current = value; }
        }
    }

    /// Input mappings
    [Serializable]
    private class FpsInput
    {
        [Tooltip("The name of the virtual axis mapped to rotate the camera around the y axis."),
            SerializeField]
        private string rotateX = "Mouse X";

        [Tooltip("The name of the virtual axis mapped to rotate the camera around the x axis."),
            SerializeField]
        private string rotateY = "Mouse Y";

        [Tooltip("The name of the virtual axis mapped to move the character back and forth."),
            SerializeField]
        private string move = "Horizontal";

        [Tooltip("The name of the virtual axis mapped to move the character left and right."),
            SerializeField]
        private string strafe = "Vertical";

        [Tooltip("The name of the virtual button mapped to run."),
            SerializeField]
        private string run = "Fire3";

        [Tooltip("The name of the virtual button mapped to jump."),
            SerializeField]
        private string jump = "Jump";

        [Tooltip("The name of the virtual button mapped to toggle the flashlight."),
            SerializeField]
        private string flashlight = "Flashlight";

        /// Returns the value of the virtual axis mapped to rotate the camera around the y axis.
        public float RotateX
        {
            get { return Input.GetAxisRaw(rotateX); }
        }

        /// Returns the value of the virtual axis mapped to rotate the camera around the x axis.        
        public float RotateY
        {
            get { return Input.GetAxisRaw(rotateY); }
        }

        /// Returns the value of the virtual axis mapped to move the character back and forth.        
        public float Move
        {
            get { return Input.GetAxisRaw(move); }
        }

        /// Returns the value of the virtual axis mapped to move the character left and right.         
        public float Strafe
        {
            get { return Input.GetAxisRaw(strafe); }
        }

        /// Returns true while the virtual button mapped to run is held down.          
        public bool Run
        {
            get { return Input.GetButton(run); }
        }

        /// Returns true during the frame the user pressed down the virtual button mapped to jump.          
        public bool Jump
        {
            get { return Input.GetButtonDown(jump); }
        }

        /// Returns true during the frame the user pressed down the virtual button mapped to toggle the flashlight.          
        public bool Flashlight
        {
            get { return Input.GetButtonDown(flashlight); }
        }
    }
}