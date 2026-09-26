using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeOfDuty.Input;

public class CrouchState : MoveStateBase
{
    public CrouchState(CharacterMove characterMove) : base(characterMove)
    {
    }

    public override void Tick()
    {
        var input = characterMove.InputSource;
        var horizontalInput = input.Move.x;
        var verticalInput = input.Move.y;

        Quaternion moveForward = Quaternion.Euler(0, characterMove.directionOrienter.rotation.eulerAngles.y, 0);

        characterMove.moveVelocity = Vector3.ClampMagnitude(moveForward * Vector3.forward * verticalInput + moveForward * Vector3.right * horizontalInput, 1) * characterMove.crouchSpeed;

        // Toggle mode: stand on second press. Hold mode: stand on release.
        // Which one is a player preference rather than a device read, so it stays in settings.
        bool wantsUp = InputBindings.CrouchIsToggle
            ? input.Pressed(InputActionId.Crouch)
            : !input.Held(InputActionId.Crouch);
        if (wantsUp)
        {
            if (!characterMove.CanStandUp())
            {
                return;
            }
            else
            {
                characterMove.SetState(characterMove.standState);
            }
        }

        characterController.Move(characterMove.moveVelocity * Time.deltaTime);

        characterMove.bodyTurnHandler.momentaryTurn = horizontalInput + verticalInput > 0;

        characterMove.animator.SetFloat(characterMove.horizontalInputID, horizontalInput);
        characterMove.animator.SetFloat(characterMove.verticalInputID, verticalInput);

        // foot-sliding prevention: clip speed follows actual ground speed
        characterMove.SyncLocomotionAnimation(characterMove.moveVelocity.magnitude, characterMove.crouchAnimReferenceSpeed);
    }

    public override void OnStateExit()
    {
        characterMove.animator.SetBool(characterMove.crouchID, false);
    }

    public override void OnStateEnter()
    {
        characterMove.animator.SetBool(characterMove.crouchID, true);
    }

}
