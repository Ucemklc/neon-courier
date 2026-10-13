using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private static bool startRunAfterSceneReload;

    [Header("References")]
    [SerializeField]
    private Transform player;

    [SerializeField]
    private RunnerController runnerController;

    [Header("Start Countdown")]
    [SerializeField, Min(1)]
    private int countdownSeconds = 3;

    [SerializeField, Min(0f)]
    private float goDisplayDuration = 0.65f;

    [Header("Speed Progression")]
    [SerializeField, Min(1f)]
    private float startingSpeed = 9.5f;

    [SerializeField, Min(1f)]
    private float maximumSpeed = 15.5f;

    [SerializeField, Min(0f)]
    private float speedGainPerSecond = 0.08f;

    [Header("Lives")]
    [SerializeField, Min(1)]
    private int maximumLives = 3;

    [SerializeField, Range(0f, 1f)]
    private float packageLossFraction = 0.5f;

    [Header("Revive")]
    [SerializeField, Min(1)]
    private int reviveLives = 1;

    [SerializeField, Min(0f)]
    private float reviveInvulnerabilityDuration = 2.5f;

    [Header("Score")]
    [SerializeField]
    private int scorePerMeter = 2;

    [SerializeField]
    private int packagePickupPoints = 10;

    [SerializeField]
    private int deliveryPointsPerPackage = 100;

    [Header("Combo")]
    [SerializeField, Min(1)]
    private int maximumCombo = 5;

    [Header("Coins")]
    [SerializeField, Min(1)]
    private int coinPerDeliveredPackage = 5;

    [Header("Audio")]
    [SerializeField]
    private bool soundEnabledByDefault = true;

    public event Action StatsChanged;
    public event Action GameOverTriggered;
    public event Action ReviveTriggered;
    public event Action<string> CountdownChanged;
    public event Action<int> DeliveryCompleted;
    public event Action PlayerHit;
    public event Action<bool> PauseChanged;
    public event Action<bool> SoundStateChanged;
    public event Action<bool> MainMenuChanged;
    public event Action<bool> RunStateChanged;

    public bool IsGameOver { get; private set; }

    public bool IsRunActive { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsMainMenuActive { get; private set; }

    public bool IsSoundEnabled { get; private set; }

    public bool ReviveUsedThisRun { get; private set; }

    public bool IsReviveInvulnerabilityActive =>
        Time.unscaledTime <
        reviveInvulnerableUntil;

    public bool IsHoverboardProtectionActive
    {
        get
        {
            PowerUpController powerUpController =
                PowerUpController.Instance;

            return
                powerUpController != null &&
                powerUpController
                    .IsHoverboardActive;
        }
    }

    public int CurrentLives { get; private set; }

    public int DistanceMeters { get; private set; }

    public int CarriedPackages { get; private set; }

    public int DeliveredPackages { get; private set; }

    public int ComboMultiplier { get; private set; } = 1;

    public int HighScore { get; private set; }

    public int TotalCoins { get; private set; }

    public int EarnedCoinsThisRun { get; private set; }

    public float CurrentSpeed { get; private set; }

    public int DifficultyLevel { get; private set; }

    public int CurrentCoinMultiplier
    {
        get
        {
            PowerUpController powerUpController =
                PowerUpController.Instance;

            return powerUpController != null
                ? powerUpController.CoinMultiplier
                : 1;
        }
    }

    public int Score =>
        DistanceMeters * scorePerMeter +
        bonusScore;

    private const string HighScoreKey =
        "NeonCourier.HighScore";

    private const string TotalCoinsKey =
        "NeonCourier.TotalCoins";

    private const string SoundEnabledKey =
        "NeonCourier.SoundEnabled";

    private int bonusScore;
    private float startingPlayerZ;

    private bool isCountdownRunning;
    private Coroutine countdownCoroutine;

    private float reviveInvulnerableUntil = -1f;

    private void Awake()
    {
        if (
            Instance != null &&
            Instance != this
        )
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        HighScore = PlayerPrefs.GetInt(
            HighScoreKey,
            0
        );

        TotalCoins = PlayerPrefs.GetInt(
            TotalCoinsKey,
            0
        );

        int defaultSoundValue =
            soundEnabledByDefault ? 1 : 0;

        IsSoundEnabled =
            PlayerPrefs.GetInt(
                SoundEnabledKey,
                defaultSoundValue
            ) == 1;

        CurrentLives = maximumLives;
        CurrentSpeed = startingSpeed;
        EarnedCoinsThisRun = 0;

        ReviveUsedThisRun = false;
        reviveInvulnerableUntil = -1f;

        ApplySoundState();
    }

    private IEnumerator Start()
    {
        ResolveReferences();

        if (
            player == null ||
            runnerController == null
        )
        {
            Debug.LogError(
                "GameManager: Player veya RunnerController bulunamadı.",
                this
            );

            enabled = false;
            yield break;
        }

        startingPlayerZ = player.position.z;

        CurrentSpeed = startingSpeed;

        runnerController.SetForwardSpeed(
            startingSpeed
        );

        runnerController.enabled = false;

        StatsChanged?.Invoke();

        SoundStateChanged?.Invoke(
            IsSoundEnabled
        );

        yield return null;

        if (startRunAfterSceneReload)
        {
            startRunAfterSceneReload = false;
            StartGameFromMenu();
        }
        else
        {
            OpenMainMenu();
        }
    }

    private void Update()
    {
        if (IsGameOver)
        {
            ReadRestartInput();
            return;
        }

        if (IsMainMenuActive)
        {
            return;
        }

        ReadPauseInput();

        if (
            IsPaused ||
            !IsRunActive
        )
        {
            return;
        }

        UpdateDistance();
        UpdateSpeed();
        UpdateDifficulty();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnApplicationPause(
        bool pauseStatus
    )
    {
        if (!pauseStatus)
        {
            return;
        }

        if (
            IsRunActive &&
            !IsGameOver &&
            !IsPaused &&
            !IsMainMenuActive
        )
        {
            PauseGame();
        }
    }

    private void ResolveReferences()
    {
        if (runnerController == null)
        {
            runnerController =
                FindAnyObjectByType
                    <RunnerController>();
        }

        if (
            player == null &&
            runnerController != null
        )
        {
            player =
                runnerController.transform;
        }
    }

    public void OpenMainMenu()
    {
        if (IsGameOver)
        {
            return;
        }

        StopCountdown();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        IsMainMenuActive = true;
        IsRunActive = false;
        IsPaused = false;
        isCountdownRunning = false;

        ReviveUsedThisRun = false;
        reviveInvulnerableUntil = -1f;

        if (runnerController != null)
        {
            runnerController.enabled = false;
        }

        CountdownChanged?.Invoke(
            string.Empty
        );

        PauseChanged?.Invoke(false);
        RunStateChanged?.Invoke(false);
        MainMenuChanged?.Invoke(true);
        StatsChanged?.Invoke();
    }

    public void StartGameFromMenu()
    {
        if (
            IsGameOver ||
            IsRunActive ||
            isCountdownRunning
        )
        {
            return;
        }

        ReviveUsedThisRun = false;
        reviveInvulnerableUntil = -1f;

        IsMainMenuActive = false;
        IsPaused = false;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        MainMenuChanged?.Invoke(false);
        PauseChanged?.Invoke(false);

        countdownCoroutine =
            StartCoroutine(
                CountdownRoutine()
            );
    }

    private IEnumerator CountdownRoutine()
    {
        isCountdownRunning = true;
        IsRunActive = false;

        RunStateChanged?.Invoke(false);

        if (runnerController != null)
        {
            runnerController.enabled = false;
        }

        for (
            int number = countdownSeconds;
            number > 0;
            number--
        )
        {
            CountdownChanged?.Invoke(
                number.ToString()
            );

            yield return
                new WaitForSecondsRealtime(1f);
        }

        if (runnerController != null)
        {
            runnerController.enabled = true;
        }

        IsRunActive = true;

        RunStateChanged?.Invoke(true);
        CountdownChanged?.Invoke("GO!");

        yield return
            new WaitForSecondsRealtime(
                goDisplayDuration
            );

        CountdownChanged?.Invoke(
            string.Empty
        );

        isCountdownRunning = false;
        countdownCoroutine = null;
    }

    private void StopCountdown()
    {
        if (countdownCoroutine == null)
        {
            return;
        }

        StopCoroutine(countdownCoroutine);

        countdownCoroutine = null;
        isCountdownRunning = false;
    }

    private void UpdateDistance()
    {
        if (player == null)
        {
            return;
        }

        float travelledDistance =
            Mathf.Max(
                0f,
                player.position.z -
                startingPlayerZ
            );

        int newDistance =
            Mathf.FloorToInt(
                travelledDistance
            );

        if (newDistance == DistanceMeters)
        {
            return;
        }

        DistanceMeters = newDistance;

        StatsChanged?.Invoke();
    }

    private void UpdateSpeed()
    {
        if (runnerController == null)
        {
            return;
        }

        CurrentSpeed =
            Mathf.MoveTowards(
                CurrentSpeed,
                maximumSpeed,
                speedGainPerSecond *
                Time.deltaTime
            );

        runnerController.SetForwardSpeed(
            CurrentSpeed
        );
    }

    private void UpdateDifficulty()
    {
        int newDifficultyLevel =
            CalculateDifficultyLevel();

        if (
            newDifficultyLevel ==
            DifficultyLevel
        )
        {
            return;
        }

        DifficultyLevel =
            newDifficultyLevel;

        Debug.Log(
            $"Zorluk seviyesi: {DifficultyLevel}"
        );
    }

    private int CalculateDifficultyLevel()
    {
        if (DistanceMeters < 150)
        {
            return 0;
        }

        if (DistanceMeters < 400)
        {
            return 1;
        }

        if (DistanceMeters < 800)
        {
            return 2;
        }

        return 3;
    }

    private void ReadPauseInput()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        bool pausePressed =
            Keyboard.current
                .escapeKey
                .wasPressedThisFrame ||
            Keyboard.current
                .pKey
                .wasPressedThisFrame;

        if (!pausePressed)
        {
            return;
        }

        if (IsPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    private void ReadRestartInput()
    {
        if (
            Keyboard.current != null &&
            Keyboard.current
                .rKey
                .wasPressedThisFrame
        )
        {
            RestartGame();
        }
    }

    public void CollectPackage()
    {
        CollectPackage(CourierCargoKind.Standard);
    }

    public void CollectPackage(CourierCargoKind cargoKind)
    {
        if (
            IsGameOver ||
            IsPaused ||
            IsMainMenuActive ||
            !IsRunActive
        )
        {
            return;
        }

        GetComponent<CourierCargoManifest>()?.RecordPickup(cargoKind);
        CarriedPackages++;
        bonusScore += packagePickupPoints;

        StatsChanged?.Invoke();
    }

    public int DeliverCarriedPackages()
    {
        if (
            IsGameOver ||
            IsPaused ||
            IsMainMenuActive ||
            !IsRunActive
        )
        {
            return 0;
        }

        if (CarriedPackages <= 0)
        {
            if (ComboMultiplier != 1)
            {
                ComboMultiplier = 1;
                StatsChanged?.Invoke();
            }

            return 0;
        }

        int deliveredNow =
            CarriedPackages;
        int specialCargoBonus = GetComponent<CourierCargoManifest>()?.CompleteDelivery(deliveredNow, CurrentCoinMultiplier) ?? 0;

        int comboUsed =
            ComboMultiplier;

        int coinMultiplier =
            CurrentCoinMultiplier;

        int deliveryScore =
            deliveredNow *
            deliveryPointsPerPackage *
            comboUsed;

        int earnedCoins =
            deliveredNow *
            coinPerDeliveredPackage *
            comboUsed *
            coinMultiplier;

        earnedCoins += specialCargoBonus * coinMultiplier;
        bonusScore += deliveryScore;

        DeliveredPackages +=
            deliveredNow;

        EarnedCoinsThisRun +=
            earnedCoins;

        TotalCoins +=
            earnedCoins;

        CarriedPackages = 0;

        ComboMultiplier =
            Mathf.Min(
                ComboMultiplier + 1,
                maximumCombo
            );

        SaveTotalCoins();

        StatsChanged?.Invoke();

        DeliveryCompleted?.Invoke(
            deliveredNow
        );

        return deliveredNow;
    }

    public int HandlePlayerHit()
    {
        if (
            IsGameOver ||
            IsPaused ||
            IsMainMenuActive ||
            !IsRunActive
        )
        {
            return 0;
        }

        if (IsReviveInvulnerabilityActive)
        {
            return 0;
        }

        PowerUpController powerUpController =
            PowerUpController.Instance;

        /*
         * Hoverboard aktifken oyuncu engel hasarı almaz.
         * Shield varsa burada harcanmaz.
         */
        if (
            powerUpController != null &&
            powerUpController
                .IsHoverboardActive
        )
        {
            return 0;
        }

        if (
            powerUpController != null &&
            powerUpController
                .TryConsumeShield()
        )
        {
            return 0;
        }

        CurrentLives =
            Mathf.Max(
                0,
                CurrentLives - 1
            );

        GetComponent<CourierCargoManifest>()?.DamageFragileCargo();
        int lostPackages = 0;

        if (CarriedPackages > 0)
        {
            lostPackages =
                Mathf.CeilToInt(
                    CarriedPackages *
                    packageLossFraction
                );

            lostPackages =
                Mathf.Clamp(
                    lostPackages,
                    1,
                    CarriedPackages
                );

            CarriedPackages -=
                lostPackages;
        }

        ComboMultiplier = 1;

        StatsChanged?.Invoke();
        PlayerHit?.Invoke();

        if (CurrentLives <= 0)
        {
            TriggerGameOver();
        }

        return lostPackages;
    }

    public bool CanRevive(
        int coinCost
    )
    {
        if (coinCost < 0)
        {
            return false;
        }

        return
            IsGameOver &&
            !ReviveUsedThisRun &&
            TotalCoins >= coinCost;
    }

    public bool TryRevive(
        int coinCost
    )
    {
        if (!CanRevive(coinCost))
        {
            return false;
        }

        ResolveReferences();

        if (
            player == null ||
            runnerController == null
        )
        {
            Debug.LogError(
                "GameManager: Devam etme için Player veya RunnerController bulunamadı.",
                this
            );

            return false;
        }

        StopCountdown();

        TotalCoins -= coinCost;
        SaveTotalCoins();

        ReviveUsedThisRun = true;

        IsGameOver = false;
        IsPaused = false;
        IsMainMenuActive = false;
        IsRunActive = false;
        isCountdownRunning = false;

        CurrentLives =
            Mathf.Clamp(
                reviveLives,
                1,
                maximumLives
            );

        ComboMultiplier = 1;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        runnerController.enabled = false;

        reviveInvulnerableUntil =
            Time.unscaledTime +
            countdownSeconds +
            goDisplayDuration +
            reviveInvulnerabilityDuration;

        CountdownChanged?.Invoke(
            string.Empty
        );

        MainMenuChanged?.Invoke(false);
        PauseChanged?.Invoke(false);
        RunStateChanged?.Invoke(false);
        StatsChanged?.Invoke();
        ReviveTriggered?.Invoke();

        countdownCoroutine =
            StartCoroutine(
                CountdownRoutine()
            );

        return true;
    }

    public void GrantRunBonus(int amount)
    {
        if (amount <= 0 || !IsRunActive || IsPaused || IsGameOver || IsMainMenuActive) return;
        EarnedCoinsThisRun += amount;
        AddCoins(amount);
    }

    public void AddCoins(
        int amount
    )
    {
        if (amount <= 0)
        {
            return;
        }

        TotalCoins += amount;

        SaveTotalCoins();
        StatsChanged?.Invoke();
    }

    public bool TrySpendCoins(
        int amount
    )
    {
        if (
            amount <= 0 ||
            TotalCoins < amount
        )
        {
            return false;
        }

        TotalCoins -= amount;

        SaveTotalCoins();
        StatsChanged?.Invoke();

        return true;
    }

    public void PauseGame()
    {
        if (
            IsGameOver ||
            IsPaused ||
            IsMainMenuActive ||
            !IsRunActive
        )
        {
            return;
        }

        IsPaused = true;

        if (runnerController != null)
        {
            runnerController.enabled = false;
        }

        AudioListener.pause = true;
        Time.timeScale = 0f;

        PauseChanged?.Invoke(true);
    }

    public void ResumeGame()
    {
        if (
            IsGameOver ||
            !IsPaused
        )
        {
            return;
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;

        IsPaused = false;

        if (runnerController != null)
        {
            runnerController.enabled = true;
        }

        PauseChanged?.Invoke(false);
    }

    public void ToggleSound()
    {
        IsSoundEnabled =
            !IsSoundEnabled;

        PlayerPrefs.SetInt(
            SoundEnabledKey,
            IsSoundEnabled ? 1 : 0
        );

        PlayerPrefs.Save();

        ApplySoundState();

        SoundStateChanged?.Invoke(
            IsSoundEnabled
        );
    }

    private void ApplySoundState()
    {
        AudioListener.volume =
            IsSoundEnabled ? 1f : 0f;
    }

    private void SaveTotalCoins()
    {
        PlayerPrefs.SetInt(
            TotalCoinsKey,
            TotalCoins
        );

        PlayerPrefs.Save();
    }

    public void TriggerGameOver()
    {
        if (IsGameOver)
        {
            return;
        }

        StopCountdown();

        IsGameOver = true;
        IsRunActive = false;
        IsPaused = false;
        IsMainMenuActive = false;
        isCountdownRunning = false;

        if (runnerController != null)
        {
            runnerController.enabled = false;
        }

        AudioListener.pause = false;

        if (Score > HighScore)
        {
            HighScore = Score;

            PlayerPrefs.SetInt(
                HighScoreKey,
                HighScore
            );
        }

        SaveTotalCoins();

        CountdownChanged?.Invoke(
            string.Empty
        );

        MainMenuChanged?.Invoke(false);
        PauseChanged?.Invoke(false);
        RunStateChanged?.Invoke(false);
        StatsChanged?.Invoke();
        GameOverTriggered?.Invoke();

        Time.timeScale = 0f;
    }

    public void RestartGame()
    {
        startRunAfterSceneReload = true;
        LoadCurrentScene();
    }

    public void ReturnToMainMenu()
    {
        startRunAfterSceneReload = false;
        LoadCurrentScene();
    }

    private void LoadCurrentScene()
    {
        IsPaused = false;

        AudioListener.pause = false;
        Time.timeScale = 1f;

        Scene currentScene =
            SceneManager.GetActiveScene();

        SceneManager.LoadScene(
            currentScene.buildIndex
        );
    }
}
