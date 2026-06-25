using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class BoneQuizController : MonoBehaviour
{
    private enum QuizState
    {
        NotStarted,
        ShowingRules,
        PresentingBone,
        WaitingForAnswer,
        FlashingCorrect,
        GameOver
    }

    private QuizState currentState = QuizState.NotStarted;

    private int score = 0;
    private int hearts = 3;
    private float timerDuration = 60f;
    private float currentTimer = 0f;
    private int lastDisplayedScore = -1;
    private int lastDisplayedTimerTenths = -1;
    private int lastDisplayedHearts = -1;
    private int loops = 0;
    private float totalTimeTaken = 0f;
    private int totalBonesAsked = 0;

    private BoneInfo targetBone;
    private List<BoneInfo> availableBones = new List<BoneInfo>();
    private BoneInfoDatabase database;

    // Root UI
    private GameObject quizUIRoot;
    
    // Panels
    private GameObject rulesPanel;
    private GameObject hudPanel;
    private GameObject presentationPanel;
    private GameObject gameOverPanel;

    // HUD Elements
    private TMP_Text scoreText;
    private TMP_Text timerText;
    private TMP_Text hudTargetText;
    private List<Image> heartImages = new List<Image>();

    // Presentation Elements
    private TMP_Text bigTargetText;

    // Game Over Elements
    private TMP_Text gameOverScoreText;
    private TMP_Text gameOverAvgTimeText;

    private Color redColor = new Color(0.9f, 0.2f, 0.2f);
    private Color greenColor = new Color(0.2f, 0.9f, 0.2f);

    private AudioSource audioSource;
    private AudioClip correctSfx;
    private AudioClip wrongSfx;

    public void BeginQuiz()
    {
        if (database == null) database = BoneInfoDatabase.Load();
        
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            correctSfx = Resources.Load<AudioClip>("Correct SFX");
            wrongSfx = Resources.Load<AudioClip>("Wrong SFX");
        }
        
        // Build the entire UI dynamically if it doesn't exist
        if (quizUIRoot == null) BuildQuizUI();

        // Reset variables
        score = 0;
        hearts = 3;
        timerDuration = 60f;
        loops = 0;
        totalTimeTaken = 0f;
        totalBonesAsked = 0;
        
        BuildAvailableBoneList();

        currentState = QuizState.ShowingRules;
        ShowPanel(rulesPanel);
        UpdateHUD();
    }

    public void ClearQuiz()
    {
        currentState = QuizState.NotStarted;
        StopAllCoroutines();
        if (BoneInteractionUI.Instance != null) BoneInteractionUI.Instance.ClearHighlights();
        if (quizUIRoot != null) quizUIRoot.SetActive(false);
    }

    private void StartGameplay()
    {
        if (availableBones.Count == 0)
        {
            EndGame();
            return;
        }
        NextBone();
    }

    private void NextBone()
    {
        if (availableBones.Count == 0 || hearts <= 0)
        {
            EndGame();
            return;
        }

        // Adjust timer
        loops++;
        timerDuration = Mathf.Max(10f, 60f - ((loops - 1) * 5f)); // starts at 60, drops by 5
        
        targetBone = availableBones[UnityEngine.Random.Range(0, availableBones.Count)];
        availableBones.Remove(targetBone); // Don't ask the same bone twice
        
        StartCoroutine(PresentationSequence());
    }

    private IEnumerator PresentationSequence()
    {
        currentState = QuizState.PresentingBone;
        
        // Show HUD in background, put presentation overlay on top
        rulesPanel.SetActive(false);
        gameOverPanel.SetActive(false);
        hudPanel.SetActive(true);
        presentationPanel.SetActive(true);
        
        string displayName = GetDisplayName(targetBone);
        bigTargetText.text = displayName;
        hudTargetText.text = displayName;

        Image dimImg = presentationPanel.GetComponent<Image>();
        RectTransform textRect = bigTargetText.GetComponent<RectTransform>();
        
        float transitionTime = 0.25f; // faster
        float elapsed = 0f;
        
        // Initial state
        dimImg.color = new Color(0f, 0f, 0f, 0f);
        textRect.anchoredPosition = new Vector2(0f, -80f);
        bigTargetText.color = new Color(bigTargetText.color.r, bigTargetText.color.g, bigTargetText.color.b, 0f);
        
        // Fade in dim and slide up — cubic ease out
        while (elapsed < transitionTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionTime);
            float easeT = 1f - (1f - t) * (1f - t) * (1f - t); // cubic ease out

            dimImg.color = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.96f, easeT));
            textRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-80f, 0f, easeT));
            bigTargetText.color = new Color(bigTargetText.color.r, bigTargetText.color.g, bigTargetText.color.b, easeT);
            yield return null;
        }
        
        yield return new WaitForSeconds(0.8f);

        // Fade out — linear
        elapsed = 0f;
        while (elapsed < transitionTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionTime);

            dimImg.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.90f, 0f, t));
            bigTargetText.color = new Color(bigTargetText.color.r, bigTargetText.color.g, bigTargetText.color.b, 1f - t);
            yield return null;
        }

        presentationPanel.SetActive(false);
        currentState = QuizState.WaitingForAnswer;
        currentTimer = timerDuration;
        totalBonesAsked++;
    }


    private void Update()
    {
        if (currentState == QuizState.WaitingForAnswer)
        {
            currentTimer -= Time.deltaTime;
            totalTimeTaken += Time.deltaTime;
            
            if (currentTimer <= 0f)
            {
                currentTimer = 0f;
                // Time up! Count as wrong.
                HandleWrongAnswer(null);
            }
            UpdateHUD();
        }
    }

    private float lastTapTime = 0f;

    public void SubmitAnswer(BoneTapResult tapResult)
    {
        if (currentState != QuizState.WaitingForAnswer) return;

        if (Time.time - lastTapTime < 0.5f) return;
        lastTapTime = Time.time;

        bool isCorrect = string.Equals(tapResult.Bone.objectBlender, targetBone.objectBlender, StringComparison.OrdinalIgnoreCase);

        if (isCorrect)
        {
            if (audioSource != null && correctSfx != null) audioSource.PlayOneShot(correctSfx);
            score += 1; // 1 point per correct answer
            StartCoroutine(CorrectAnswerSequence(tapResult.BoneTransform));
        }
        else
        {
            HandleWrongAnswer(tapResult.BoneTransform);
        }
    }

    private void HandleWrongAnswer(Transform wrongBoneTransform)
    {
        if (audioSource != null && wrongSfx != null) audioSource.PlayOneShot(wrongSfx);
        
        // Vibrate the phone on every wrong answer
        Handheld.Vibrate();

        if (hearts > 0)
        {
            Image lostHeart = heartImages[hearts - 1];
            StartCoroutine(AnimateHeartLoss(lostHeart));
        }

        hearts--;
        
        if (hearts <= 0)
        {
            UpdateHUD();
            EndGame();
        }
        else
        {
            if (currentTimer <= 0)
            {
                currentTimer = timerDuration; // Give time back if they failed due to timeout
            }
            UpdateHUD();
            // Do NOT move to the next bone, let them try again!
        }
    }



    private IEnumerator AnimateHeartLoss(Image heartImg)
    {
        GameObject clone = Instantiate(heartImg.gameObject, quizUIRoot.transform);
        RectTransform cloneRt = clone.GetComponent<RectTransform>();
        cloneRt.position = heartImg.rectTransform.position;
        Image cloneImg = clone.GetComponent<Image>();
        cloneImg.color = Color.white;
        
        float duration = 1.0f;
        float elapsed = 0f;
        Vector3 startPos = cloneRt.position;
        
        float xVelocity = UnityEngine.Random.Range(-250f, -100f);
        float rotateSpeed = UnityEngine.Random.Range(-400f, -100f);
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            float yOffset = Mathf.Sin(t * Mathf.PI) * 80f - (t * t * 350f); 
            float xOffset = xVelocity * t;
            
            cloneRt.position = startPos + new Vector3(xOffset, yOffset, 0);
            cloneRt.Rotate(0, 0, rotateSpeed * Time.deltaTime);
            
            float alpha = t > 0.4f ? 1f - ((t - 0.4f) / 0.6f) : 1f;
            cloneImg.color = new Color(1f, 1f, 1f, alpha);
            
            yield return null;
        }
        
        Destroy(clone);
    }

    private IEnumerator CorrectAnswerSequence(Transform boneTransform)
    {
        currentState = QuizState.FlashingCorrect;
        UpdateHUD(); // To freeze the timer visually
        
        // Flash green 5 times in 2 seconds
        int flashes = 5;
        float delay = 2f / (flashes * 2f); // half on, half off
        
        for (int i = 0; i < flashes; i++)
        {
            if (BoneInteractionUI.Instance != null) BoneInteractionUI.Instance.HighlightBoneForFeedback(boneTransform, greenColor);
            yield return new WaitForSeconds(delay);
            if (BoneInteractionUI.Instance != null) BoneInteractionUI.Instance.ClearHighlights();
            yield return new WaitForSeconds(delay);
        }

        NextBone();
    }

    private void EndGame()
    {
        currentState = QuizState.GameOver;
        ShowPanel(gameOverPanel);
        
        float avgTime = totalBonesAsked > 0 ? totalTimeTaken / totalBonesAsked : 0f;
        gameOverScoreText.text = $"Skor Akhir: {score}";
        gameOverAvgTimeText.text = $"Rata-rata Waktu: {avgTime:F1}s";
    }

    public void ExitMinigameClicked() // Called by bottom exit button
    {
        if (score > 0 && currentState != QuizState.GameOver)
        {
            EndGame();
        }
        else
        {
            ClearQuiz();
            if (ARAppModeController.Instance != null) ARAppModeController.Instance.SetMode(ARAppMode.Skeleton);
        }
    }

    // TMP's Shadow component doesn't work with TextMeshProUGUI.
    // Instead we enable TMP's built-in Underlay pass on the font material.
    private static void AddTMPShadow(TMP_Text text)
    {
        // fontMaterial creates a per-instance copy so we don't pollute the shared asset
        Material mat = text.fontMaterial;
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.85f));
        mat.SetFloat("_UnderlayOffsetX",  0.6f);
        mat.SetFloat("_UnderlayOffsetY", -0.6f);
        mat.SetFloat("_UnderlaySoftness", 0f);
        text.UpdateMeshPadding();
    }

    private void BuildQuizUI()
    {
        if (ARAppModeController.Instance == null) return;
        Transform overlayRoot = ARAppModeController.Instance.overlayRoot;
        
        quizUIRoot = new GameObject("Quiz UI", typeof(RectTransform));
        quizUIRoot.transform.SetParent(overlayRoot, false);
        RectTransform rootRect = quizUIRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;

        // Rules Panel Overlay
        rulesPanel = new GameObject("Rules Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
        rulesPanel.transform.SetParent(quizUIRoot.transform, false);
        RectTransform rulesOverlayRect = rulesPanel.GetComponent<RectTransform>();
        rulesOverlayRect.anchorMin = Vector2.zero; rulesOverlayRect.anchorMax = Vector2.one;
        rulesOverlayRect.offsetMin = Vector2.zero; rulesOverlayRect.offsetMax = Vector2.zero;
        
        Image rulesBg = rulesPanel.GetComponent<Image>();
        rulesBg.color = new Color(0f, 0f, 0f, 0f); // Fully transparent background
        
        Button rulesBtn = rulesPanel.GetComponent<Button>();
        rulesBtn.transition = Selectable.Transition.None;
        rulesBtn.onClick.AddListener(ExitMinigameClicked);

        // The rulesBox blocks clicks from reaching the overlay dismiss button behind it
        GameObject rulesBox = new GameObject("Rules Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(Button), typeof(ModalAnimator));
        rulesBox.transform.SetParent(rulesPanel.transform, false);
        Image rulesBoxImg = rulesBox.GetComponent<Image>();
        rulesBoxImg.color = new Color(0.05f, 0.05f, 0.05f, 0.98f);
        rulesBoxImg.sprite = ARAppModeController.GetRoundedRectSprite();
        rulesBoxImg.type = Image.Type.Sliced;
        
        UnityEngine.UI.Shadow rulesShadow1 = rulesBox.AddComponent<UnityEngine.UI.Shadow>();
        rulesShadow1.effectColor = new Color(0f, 0f, 0f, 0.4f);
        rulesShadow1.effectDistance = new Vector2(3f, -3f);
        
        UnityEngine.UI.Shadow rulesShadow2 = rulesBox.AddComponent<UnityEngine.UI.Shadow>();
        rulesShadow2.effectColor = new Color(0f, 0f, 0f, 0.15f);
        rulesShadow2.effectDistance = new Vector2(8f, -8f);
        Button rulesBoxBlocker = rulesBox.GetComponent<Button>();
        rulesBoxBlocker.targetGraphic = rulesBoxImg;
        rulesBoxBlocker.transition = Selectable.Transition.None; // absorb clicks, do nothing
        RectTransform rulesRect = rulesBox.GetComponent<RectTransform>();
        // Exact same anchor pattern as the working Cara Menggunakan modal:
        // left+right anchors define width, vertical anchor at 0.5 + ContentSizeFitter defines height
        rulesRect.anchorMin = new Vector2(0.1f, 0.5f);
        rulesRect.anchorMax = new Vector2(0.9f, 0.5f);
        rulesRect.pivot = new Vector2(0.5f, 0.5f);
        rulesRect.anchoredPosition = Vector2.zero;
        rulesRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup rulesVbox = rulesBox.GetComponent<VerticalLayoutGroup>();
        rulesVbox.padding = new RectOffset(40, 40, 40, 40);
        rulesVbox.childAlignment = TextAnchor.MiddleCenter; rulesVbox.spacing = 20;
        rulesVbox.childControlWidth = true; rulesVbox.childControlHeight = true;
        rulesVbox.childForceExpandWidth = true; rulesVbox.childForceExpandHeight = false;
        ContentSizeFitter rulesFitter = rulesBox.GetComponent<ContentSizeFitter>();
        rulesFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ARAppModeController.CreateLayoutText("Title", rulesBox.transform, "Aturan Kuis", 48f, FontStyles.Bold, TextAlignmentOptions.Center);
        ARAppModeController.CreateLayoutText("Desc", rulesBox.transform, "Temukan tulang yang diminta sebelum waktu habis!\n\nSalah pilih atau kehabisan waktu = hilang 1 nyawa.", 28f, FontStyles.Normal, TextAlignmentOptions.Center);

        GameObject rulesSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        rulesSpacer.transform.SetParent(rulesBox.transform, false);
        rulesSpacer.GetComponent<LayoutElement>().minHeight = 24f;
        
        GameObject startBtnObj = ARAppModeController.Create3DButtonObject("Start Button", rulesBox.transform, "MULAI KUIS", out Button startButton, out TMP_Text startLbl);
        startButton.onClick.AddListener(StartGameplay);
        ARAppModeController.SetLayoutSize(startBtnObj, 0f, 60f, 0f, 0f);

        // HUD Panel
        hudPanel = new GameObject("HUD Panel", typeof(RectTransform));
        hudPanel.transform.SetParent(quizUIRoot.transform, false);
        RectTransform hudRect = hudPanel.GetComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero; hudRect.offsetMax = Vector2.zero;

        // Score (Top Left) - same height/position as hearts on the right
        scoreText = ARAppModeController.CreateText("Score Text", hudPanel.transform, "Skor: 0", 36f, FontStyles.Bold, TextAlignmentOptions.Left);
        RectTransform scoreRect = scoreText.GetComponent<RectTransform>();
        scoreRect.anchorMin = new Vector2(0, 1); scoreRect.anchorMax = new Vector2(0, 1);
        scoreRect.pivot = new Vector2(0, 1);
        scoreRect.offsetMin = Vector2.zero; scoreRect.offsetMax = Vector2.zero;
        scoreRect.anchoredPosition = new Vector2(40f, -50f);
        scoreRect.sizeDelta = new Vector2(300f, 60f);
        AddTMPShadow(scoreText);

        // Hearts (Top Right)
        GameObject heartsObj = new GameObject("Hearts Container", typeof(RectTransform));
        heartsObj.transform.SetParent(hudPanel.transform, false);
        RectTransform heartsRect = heartsObj.GetComponent<RectTransform>();
        heartsRect.anchorMin = new Vector2(1, 1); heartsRect.anchorMax = new Vector2(1, 1);
        heartsRect.pivot = new Vector2(1, 1);
        heartsRect.anchoredPosition = new Vector2(-40f, -50f);
        heartsRect.sizeDelta = new Vector2(200f, 60f);
        HorizontalLayoutGroup heartsHbox = heartsObj.AddComponent<HorizontalLayoutGroup>();
        heartsHbox.childAlignment = TextAnchor.MiddleRight; heartsHbox.spacing = 10;
        heartsHbox.childControlWidth = false; heartsHbox.childControlHeight = false;
        heartsHbox.childForceExpandWidth = false; heartsHbox.childForceExpandHeight = false;

        for (int i=0; i<3; i++) {
            GameObject heart = new GameObject("Heart", typeof(RectTransform), typeof(Image));
            heart.transform.SetParent(heartsObj.transform, false);
            RectTransform hr = heart.GetComponent<RectTransform>();
            hr.sizeDelta = new Vector2(50f, 50f);
            Image hi = heart.GetComponent<Image>();
            hi.sprite = Resources.Load<Sprite>("Heart");
            hi.preserveAspect = true;
            Shadow heartShadow = heart.AddComponent<Shadow>();
            heartShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            heartShadow.effectDistance = new Vector2(3f, -3f);
            heartImages.Add(hi);
        }

        // Timer & Target (Center Top)
        GameObject centerTopObj = new GameObject("Center Top", typeof(RectTransform));
        centerTopObj.transform.SetParent(hudPanel.transform, false);
        RectTransform centerRect = centerTopObj.GetComponent<RectTransform>();
        centerRect.anchorMin = new Vector2(0.5f, 1); centerRect.anchorMax = new Vector2(0.5f, 1);
        centerRect.pivot = new Vector2(0.5f, 1);
        centerRect.anchoredPosition = new Vector2(0f, -50f);
        centerRect.sizeDelta = new Vector2(400f, 120f);
        VerticalLayoutGroup centerVbox = centerTopObj.AddComponent<VerticalLayoutGroup>();
        centerVbox.childAlignment = TextAnchor.UpperCenter; centerVbox.spacing = -10;
        centerVbox.childControlWidth = true; centerVbox.childControlHeight = false;

        timerText = ARAppModeController.CreateLayoutText("Timer", centerTopObj.transform, "60.0s", 56f, FontStyles.Bold, TextAlignmentOptions.Center);
        AddTMPShadow(timerText);
        hudTargetText = ARAppModeController.CreateLayoutText("HUD Target", centerTopObj.transform, "Tulang", 32f, FontStyles.Normal, TextAlignmentOptions.Center);
        AddTMPShadow(hudTargetText);

        // Exit Button (Bottom Center)
        GameObject exitBtnObj = ARAppModeController.Create3DButtonObject("Exit Button", hudPanel.transform, "Keluar Kuis", out Button exitBtn, out TMP_Text exitLbl);
        exitLbl.fontSize = 22f;
        exitLbl.textWrappingMode = TextWrappingModes.NoWrap;
        exitBtn.onClick.AddListener(ExitMinigameClicked);
        RectTransform exitRect = exitBtnObj.GetComponent<RectTransform>();
        exitRect.anchorMin = new Vector2(0.5f, 0f); exitRect.anchorMax = new Vector2(0.5f, 0f);
        exitRect.pivot = new Vector2(0.5f, 0f);
        exitRect.anchoredPosition = new Vector2(0f, 60f);
        exitRect.sizeDelta = new Vector2(200f, 52f);

        // Presentation Panel (Dim & Big Text)
        presentationPanel = new GameObject("Presentation Panel", typeof(RectTransform), typeof(Image));
        presentationPanel.transform.SetParent(quizUIRoot.transform, false);
        RectTransform presRect = presentationPanel.GetComponent<RectTransform>();
        presRect.anchorMin = Vector2.zero; presRect.anchorMax = Vector2.one;
        presRect.offsetMin = Vector2.zero; presRect.offsetMax = Vector2.zero;
        Image dimOverlay = presentationPanel.GetComponent<Image>();
        dimOverlay.color = new Color(0, 0, 0, 0.7f); // Dim screen

        bigTargetText = ARAppModeController.CreateText("Big Target Text", presentationPanel.transform, "TULANG", 100f, FontStyles.Bold, TextAlignmentOptions.Center);

        // Game Over Panel Overlay
        gameOverPanel = new GameObject("Game Over Overlay", typeof(RectTransform), typeof(Image));
        gameOverPanel.transform.SetParent(quizUIRoot.transform, false);
        RectTransform overOverlayRect = gameOverPanel.GetComponent<RectTransform>();
        overOverlayRect.anchorMin = Vector2.zero; overOverlayRect.anchorMax = Vector2.one;
        overOverlayRect.offsetMin = Vector2.zero; overOverlayRect.offsetMax = Vector2.zero;
        
        Image overBg = gameOverPanel.GetComponent<Image>();
        overBg.color = new Color(0f, 0f, 0f, 0f); // Fully transparent background
        
        GameObject overBox = ARAppModeController.CreatePanel("Game Over Box", gameOverPanel.transform, new Color(0.05f, 0.05f, 0.05f, 0.98f)).gameObject;
        overBox.AddComponent<ModalAnimator>();
        
        UnityEngine.UI.Shadow overShadow1 = overBox.AddComponent<UnityEngine.UI.Shadow>();
        overShadow1.effectColor = new Color(0f, 0f, 0f, 0.4f);
        overShadow1.effectDistance = new Vector2(3f, -3f);
        
        UnityEngine.UI.Shadow overShadow2 = overBox.AddComponent<UnityEngine.UI.Shadow>();
        overShadow2.effectColor = new Color(0f, 0f, 0f, 0.15f);
        overShadow2.effectDistance = new Vector2(8f, -8f);
        RectTransform overRect = overBox.GetComponent<RectTransform>();
        overRect.anchorMin = new Vector2(0.2f, 0.3f); overRect.anchorMax = new Vector2(0.8f, 0.7f);
        overRect.offsetMin = Vector2.zero; overRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup overVbox = overBox.AddComponent<VerticalLayoutGroup>();
        overVbox.padding = new RectOffset(40, 40, 40, 40);
        overVbox.childAlignment = TextAnchor.MiddleCenter; overVbox.spacing = 20;
        overVbox.childControlWidth = true; overVbox.childControlHeight = true;

        ARAppModeController.CreateLayoutText("Over Title", overBox.transform, "GAME OVER", 56f, FontStyles.Bold, TextAlignmentOptions.Center);
        gameOverScoreText = ARAppModeController.CreateLayoutText("Score", overBox.transform, "Skor: 0", 36f, FontStyles.Normal, TextAlignmentOptions.Center);
        gameOverAvgTimeText = ARAppModeController.CreateLayoutText("Time", overBox.transform, "Rata-rata: 0s", 36f, FontStyles.Normal, TextAlignmentOptions.Center);

        GameObject overSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        overSpacer.transform.SetParent(overBox.transform, false);
        overSpacer.GetComponent<LayoutElement>().minHeight = 24f;

        GameObject backBtnObj = ARAppModeController.Create3DButtonObject("Back Button", overBox.transform, "KEMBALI KE MENU", out Button backBtn, out TMP_Text backLbl);
        backBtn.onClick.AddListener(() => {
            ClearQuiz();
            if (ARAppModeController.Instance != null) ARAppModeController.Instance.SetMode(ARAppMode.Skeleton);
        });
        ARAppModeController.SetLayoutSize(backBtnObj, 0f, 60f, 0f, 0f);

        quizUIRoot.SetActive(false);
    }

    private void ShowPanel(GameObject activePanel)
    {
        quizUIRoot.SetActive(true);
        rulesPanel.SetActive(rulesPanel == activePanel);
        hudPanel.SetActive(hudPanel == activePanel);
        presentationPanel.SetActive(presentationPanel == activePanel);
        gameOverPanel.SetActive(gameOverPanel == activePanel);
    }

    private void UpdateHUD()
    {
        if (score != lastDisplayedScore)
        {
            scoreText.SetText("Skor: {0}", score);
            lastDisplayedScore = score;
        }

        int tenths = Mathf.CeilToInt(currentTimer * 10f);
        if (tenths != lastDisplayedTimerTenths)
        {
            timerText.text = currentTimer.ToString("F1") + "s";
            lastDisplayedTimerTenths = tenths;
        }
        
        if (hearts != lastDisplayedHearts)
        {
            for (int i=0; i<heartImages.Count; i++)
            {
                heartImages[i].color = (i < hearts) ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0.5f);
            }
            lastDisplayedHearts = hearts;
        }
    }

    private void BuildAvailableBoneList()
    {
        availableBones.Clear();

        if (database == null || BoneInteractionUI.Instance == null || SkeletonRegistry.Instance == null || SkeletonRegistry.Instance.ActiveSkeleton == null)
        {
            return;
        }

        foreach (BoneInfo bone in database.Bones)
        {
            if (bone != null && BoneInteractionUI.Instance.GetBoneTransform(bone.objectBlender) != null)
            {
                availableBones.Add(bone);
            }
        }
    }

    private static string GetDisplayName(BoneInfo bone)
    {
        if (bone == null) return "";
        return BoneInteractionUI.GetDisplayName(bone.objectBlender);
    }
}
