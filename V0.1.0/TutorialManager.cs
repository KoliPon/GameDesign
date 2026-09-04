using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("=== 核心組件對接 ===")]
    public UDPReceiver udpReceiver;

    [Header("=== RPG UI 元素 ===")]
    public GameObject dialoguePanel;
    public TMP_Text npcNameText;
    public TMP_Text dialogueText;
    public TMP_Text hintOverlayText;
    public Button nextButton;

    [Header("=== 綠燈提示 UI (選填) ===")]
    public Image greenLightVisualElement;

    [Header("=== 自由練習場 UI ===")]
    public GameObject practiceGroupPanel;
    public Image practiceCircleIcon;
    public Image practiceSquareIcon;
    public Image practiceTriangleIcon;

    [Header("=== 傾斜選單 UI ===")]
    public Image reCalibrateHighlight;
    public Image startBattleHighlight;
    public Image selectionProgressBar;

    private enum TutorialStep
    {
        Welcome,
        ExplainCircle, WaitCircle, BreakCircle,
        ExplainSquare, WaitSquare, BreakSquare,
        ExplainTriangle, WaitTriangle, BreakTriangle,
        FreePracticeSandbox
    }
    private TutorialStep currentStep = TutorialStep.Welcome;

    private bool isWaitingForPlayerWave = false;

    private bool hasPracticedCircle = false;
    private bool hasPracticedSquare = false;
    private bool hasPracticedTriangle = false;

    private int currentSelectedOption = 0;
    private float selectionTimer = 0f;
    private float requiredHoldTime = 1.5f;

    private float sandboxCooldownTimer = 0f;
    private float requiredCooldownTime = 1.5f;

    void Start()
    {
        dialoguePanel.SetActive(true);
        if (practiceGroupPanel != null) practiceGroupPanel.SetActive(false);
        if (hintOverlayText != null) hintOverlayText.text = "";
        if (greenLightVisualElement != null) greenLightVisualElement.gameObject.SetActive(false);

        SetHighlightActive(0);
        UpdateDialogueLayout();
    }

    void Update()
    {
        if (currentStep == TutorialStep.FreePracticeSandbox)
        {
            if (sandboxCooldownTimer > 0)
            {
                sandboxCooldownTimer -= Time.deltaTime;
                currentSelectedOption = 0;
                selectionTimer = 0f;
                if (selectionProgressBar != null) selectionProgressBar.fillAmount = 0f;
                return;
            }

            if (currentSelectedOption != 0)
            {
                selectionTimer += Time.deltaTime;
                if (selectionProgressBar != null)
                {
                    selectionProgressBar.gameObject.SetActive(true);
                    selectionProgressBar.fillAmount = selectionTimer / requiredHoldTime;
                }

                if (selectionTimer >= requiredHoldTime)
                {
                    int chosen = currentSelectedOption;
                    currentSelectedOption = 0;
                    if (selectionProgressBar != null) selectionProgressBar.gameObject.SetActive(false);

                    if (chosen == -1) OnClickReCalibrate();
                    else if (chosen == 1) OnClickStartBattle();
                }
            }
            else
            {
                if (selectionProgressBar != null && selectionProgressBar.gameObject.activeSelf)
                    selectionProgressBar.gameObject.SetActive(false);
            }

            if (hasPracticedCircle && hasPracticedSquare && hasPracticedTriangle)
            {
                dialogueText.text = "「不可思議！你具備非常大的天賦！現在準備就緒後，請【向右傾斜魔杖定格】，我們來實戰一次！」";
            }
        }
    }

    public void HandleWandTilt(string tiltData)
    {
        if (currentStep != TutorialStep.FreePracticeSandbox || sandboxCooldownTimer > 0) return;

        try
        {
            string[] parts = tiltData.Split(':');
            float ax = float.Parse(parts[1]);
            float tiltThreshold = 0.35f;

            if (ax < -tiltThreshold)
            {
                if (currentSelectedOption != 1)
                {
                    currentSelectedOption = 1;
                    selectionTimer = 0f;
                    SetHighlightActive(1);
                }
            }
            else if (ax > tiltThreshold)
            {
                if (currentSelectedOption != -1)
                {
                    currentSelectedOption = -1;
                    selectionTimer = 0f;
                    SetHighlightActive(-1);
                }
            }
            else
            {
                if (currentSelectedOption != 0)
                {
                    currentSelectedOption = 0;
                    selectionTimer = 0f;
                    SetHighlightActive(0);
                }
            }
        }
        catch (Exception) { }
    }

    void SetHighlightActive(int option)
    {
        if (reCalibrateHighlight != null) reCalibrateHighlight.gameObject.SetActive(option == -1);
        if (startBattleHighlight != null) startBattleHighlight.gameObject.SetActive(option == 1);
    }

    public void OnNextButtonClicked()
    {
        if (isWaitingForPlayerWave) return;

        switch (currentStep)
        {
            case TutorialStep.Welcome:
                currentStep = TutorialStep.ExplainCircle;
                UpdateDialogueLayout();
                break;
            case TutorialStep.ExplainCircle:
                currentStep = TutorialStep.WaitCircle;
                StartDirectPracticeFlow("Circle");
                break;
            case TutorialStep.BreakCircle:
                currentStep = TutorialStep.ExplainSquare;
                UpdateDialogueLayout();
                break;
            case TutorialStep.ExplainSquare:
                currentStep = TutorialStep.WaitSquare;
                StartDirectPracticeFlow("Square");
                break;
            case TutorialStep.BreakSquare:
                currentStep = TutorialStep.ExplainTriangle;
                UpdateDialogueLayout();
                break;
            case TutorialStep.ExplainTriangle:
                currentStep = TutorialStep.WaitTriangle;
                StartDirectPracticeFlow("Triangle");
                break;
            case TutorialStep.BreakTriangle:
                currentStep = TutorialStep.FreePracticeSandbox;
                EnterPracticeSandbox();
                break;
        }
    }

    void StartDirectPracticeFlow(string shape)
    {
        isWaitingForPlayerWave = true;
        nextButton.gameObject.SetActive(false);
        if (greenLightVisualElement != null) greenLightVisualElement.gameObject.SetActive(true);
        if (hintOverlayText != null) hintOverlayText.text = $"!! 請當場揮出一次【{shape.ToUpper()}】!!";
        dialogueText.text = "「就是現在！動手揮動魔杖！完成後請收招定格。」";
    }

    public void HandleMagicWandInputInTutorial(string spellName)
    {
        if (string.IsNullOrEmpty(spellName)) return;
        string cleanSpell = spellName.Trim();

        if (currentStep == TutorialStep.FreePracticeSandbox)
        {
            if (cleanSpell.Equals("Circle", StringComparison.OrdinalIgnoreCase))
            {
                hasPracticedCircle = true;
                if (practiceCircleIcon != null) practiceCircleIcon.color = new Color(1f, 0.84f, 0f, 1f);
            }
            if (cleanSpell.Equals("Square", StringComparison.OrdinalIgnoreCase))
            {
                hasPracticedSquare = true;
                if (practiceSquareIcon != null) practiceSquareIcon.color = new Color(0f, 0.75f, 1f, 1f);
            }
            if (cleanSpell.Equals("Triangle", StringComparison.OrdinalIgnoreCase))
            {
                hasPracticedTriangle = true;
                if (practiceTriangleIcon != null) practiceTriangleIcon.color = new Color(0.6f, 0.2f, 1f, 1f);
            }
            return;
        }

        if (isWaitingForPlayerWave)
        {
            bool stepSuccess = false;
            if (currentStep == TutorialStep.WaitCircle && cleanSpell.Equals("Circle", StringComparison.OrdinalIgnoreCase))
            {
                currentStep = TutorialStep.BreakCircle;
                stepSuccess = true;
            }
            else if (currentStep == TutorialStep.WaitSquare && cleanSpell.Equals("Square", StringComparison.OrdinalIgnoreCase))
            {
                currentStep = TutorialStep.BreakSquare;
                stepSuccess = true;
            }
            else if (currentStep == TutorialStep.WaitTriangle && cleanSpell.Equals("Triangle", StringComparison.OrdinalIgnoreCase))
            {
                currentStep = TutorialStep.BreakTriangle;
                stepSuccess = true;
            }

            if (stepSuccess)
            {
                isWaitingForPlayerWave = false;
                if (greenLightVisualElement != null) greenLightVisualElement.gameObject.SetActive(false);
                nextButton.gameObject.SetActive(true);
                UpdateDialogueLayout();
            }
        }
    }

    void EnterPracticeSandbox()
    {
        sandboxCooldownTimer = requiredCooldownTime;
        currentSelectedOption = 0;
        selectionTimer = 0f;
        SetHighlightActive(0);
        if (selectionProgressBar != null)
        {
            selectionProgressBar.fillAmount = 0f;
            selectionProgressBar.gameObject.SetActive(false);
        }

        nextButton.gameObject.SetActive(false);
        dialoguePanel.SetActive(true);
        npcNameText.text = "【???】";
        dialogueText.text = "「看來你很快就上手了嘛！現在試著隨意揮舞招式熱身。若想實戰，請【向右傾斜魔杖並穩住】；若想重新教學，請【向左傾斜並穩住】。」";

        if (hintOverlayText != null) hintOverlayText.text = " ";
        if (practiceGroupPanel != null) practiceGroupPanel.SetActive(true);
        ResetIconColors();
    }

    void ResetIconColors()
    {
        if (practiceCircleIcon != null) practiceCircleIcon.color = new Color(1, 1, 1, 0.3f);
        if (practiceSquareIcon != null) practiceSquareIcon.color = new Color(1, 1, 1, 0.3f);
        if (practiceTriangleIcon != null) practiceTriangleIcon.color = new Color(1, 1, 1, 0.3f);
    }

    public void OnClickReCalibrate()
    {
        Debug.Log("正在重置並加載教學場景...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClickStartBattle()
    {
        Debug.Log("[傳送門啟動] 正在前往戰鬥場景...");
        SceneManager.LoadScene("BattleScene");
    }

    void UpdateDialogueLayout()
    {
        npcNameText.text = "【???】";
        nextButton.gameObject.SetActive(true);

        switch (currentStep)
        {
            case TutorialStep.Welcome:
                dialogueText.text = "「呦新來的。看你什麼都還不會，讓我先教會你基本的法術吧...」";
                break;
            case TutorialStep.ExplainCircle:
                if (hintOverlayText != null) hintOverlayText.text = "幾何感應：圓圈術";
                dialogueText.text = "「首先是【圓圈術】。請點擊下一步確認，並當場在空中畫出一個大圓圈。」";
                break;
            case TutorialStep.BreakCircle:
                if (hintOverlayText != null) hintOverlayText.text = "圓圈感知成功！";
                dialogueText.text = "「精準的圖形！準備好了請按下一步，我們來試試看正方形。」";
                break;
            case TutorialStep.ExplainSquare:
                if (hintOverlayText != null) hintOverlayText.text = "幾何感應：正方形";
                dialogueText.text = "「接下來是【正方形】。準備好點擊下一步，並在空中俐落地劃出方形。」";
                break;
            case TutorialStep.BreakSquare:
                if (hintOverlayText != null) hintOverlayText.text = "正方形感知成功！";
                dialogueText.text = "「結構很清晰。下一步預備最後一項：三角形。」";
                break;
            case TutorialStep.ExplainTriangle:
                if (hintOverlayText != null) hintOverlayText.text = "幾何感應：三角形";
                dialogueText.text = "「最後是銳利的【三角形】。下一步點擊後，請在空中劃出三角邊緣。」";
                break;
            case TutorialStep.BreakTriangle:
                if (hintOverlayText != null) hintOverlayText.text = "所有軌跡校準完畢！";
                dialogueText.text = "「不可思議！上手的速度還真快！點擊下一步進入自由熱身吧。」";
                break;
        }
    }

    public void OnSignalFromPython(string signal) { }
}