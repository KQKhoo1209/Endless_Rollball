using UnityEngine;
using UnityEngine.InputSystem;

public sealed class KeyboardGameplayInput : MonoBehaviour, IGameplayInput
{
    private Vector2 movementInput;
    private bool jumpQueued;

    private void Update()
    {
        Vector2 keyboardInput = ReadKeyboardInput();
        Vector2 gamepadInput = Gamepad.current?.leftStick.ReadValue() ?? Vector2.zero;

        movementInput = Vector2.ClampMagnitude(keyboardInput + gamepadInput, 1f);
        jumpQueued |= WasJumpPressed();
    }

    public GameplayCommand ReadCommand()
    {
        GameplayCommand command = new GameplayCommand(
            movementInput.x,
            movementInput.y,
            jumpQueued);

        jumpQueued = false;
        return command;
    }

    private static Vector2 ReadKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float steering = ReadAxis(
            keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
            keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed);

        float speedAdjustment = ReadAxis(
            keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
            keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed);

        return new Vector2(steering, speedAdjustment);
    }

    private static bool WasJumpPressed()
    {
        bool keyboardJump = Keyboard.current?.spaceKey.wasPressedThisFrame ?? false;
        bool gamepadJump = Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false;
        return keyboardJump || gamepadJump;
    }

    private static float ReadAxis(bool negativePressed, bool positivePressed)
    {
        return (positivePressed ? 1f : 0f) - (negativePressed ? 1f : 0f);
    }

    private void OnDisable()
    {
        movementInput = Vector2.zero;
        jumpQueued = false;
    }
}
