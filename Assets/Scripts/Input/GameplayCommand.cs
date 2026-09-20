using UnityEngine;

public readonly struct GameplayCommand
{
    public GameplayCommand(float steering, float speedAdjustment, bool jumpPressed)
    {
        Steering = Mathf.Clamp(steering, -1f, 1f);
        SpeedAdjustment = Mathf.Clamp(speedAdjustment, -1f, 1f);
        JumpPressed = jumpPressed;
    }

    public float Steering { get; }
    public float SpeedAdjustment { get; }
    public bool JumpPressed { get; }

    public static GameplayCommand Neutral => new GameplayCommand(0f, 0f, false);
}
