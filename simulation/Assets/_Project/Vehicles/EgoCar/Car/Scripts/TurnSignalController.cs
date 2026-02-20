using UnityEngine;

public class TurnSignalController : MonoBehaviour
{
    public Light leftTurnSignal;
    public Light rightTurnSignal;
    public float blinkInterval = 0.5f;

    private bool isLeftSignalOn = false;
    private bool isRightSignalOn = false;
    private float timer = 0f;

    void Update()
    {
        timer += Time.deltaTime;

        if (isLeftSignalOn && timer >= blinkInterval)
        {
            leftTurnSignal.enabled = !leftTurnSignal.enabled;
            timer = 0f;
        }

        if (isRightSignalOn && timer >= blinkInterval)
        {
            rightTurnSignal.enabled = !rightTurnSignal.enabled;
            timer = 0f;
        }
    }

    public void ActivateLeftSignal(bool activate)
    {
        isLeftSignalOn = activate;
        leftTurnSignal.enabled = activate;
    }

    public void ActivateRightSignal(bool activate)
    {
        isRightSignalOn = activate;
        rightTurnSignal.enabled = activate;
    }
}