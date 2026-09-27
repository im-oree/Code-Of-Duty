using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeOfDuty.Input;

public class RollState : MoveStateBase
{
    private float rollTime = 0.95f;          // snappier COD-style slide
    private const float slideBoost = 1.4f;   // slides carry MORE speed than the sprint that started them
    private float currentTime = 0;
    private Vector3 startVelocity;
    public RollState(CharacterMove characterMove) : base(characterMove)
    {
    }

    public override void Tick()
    {
        characterMove.bodyTurnHandler.momentaryTurn = true;
        characterController.Move(characterMove.rollVelocity * Time.deltaTime);

        // COD slide-cancel: jump out of the slide at any point, KEEPING momentum
        if (characterMove.InputSource.Pressed(InputActionId.Jump) && characterMove.CanStandUp())
        {
            characterMove.moveVelocity = characterMove.rollVelocity;
            characterMove.SetState(characterMove.jumpState);
            return;
        }

        if (currentTime < rollTime && characterMove.rollVelocity != Vector3.zero)
        {
            // ease-out: fast launch, smooth tail — never a hard stop
            float k = currentTime / rollTime;
            characterMove.rollVelocity = Vector3.Lerp(startVelocity, Vector3.zero, k * k);
            currentTime += Time.deltaTime;
        }
        else
        {
            // pop straight back up like COD (only crouch if there is no headroom)
            characterMove.SetState(characterMove.CanStandUp()
                ? characterMove.standState
                : characterMove.crouchState);
        }
    }

    public override void OnStateEnter()
    {
        currentTime = 0;
        characterMove.animator.SetBool(characterMove.rollID, true);
        characterMove.rollVelocity = characterMove.moveVelocity * slideBoost;
        startVelocity = characterMove.rollVelocity;
        characterMove.moveVelocity = Vector3.zero;
        if (characterMove.rollVelocity == Vector3.zero)
        {
            characterMove.SetState(characterMove.crouchState);
        }
    }

    public override void OnStateExit()
    {
        characterMove.rollVelocity = Vector3.zero;
        characterMove.animator.SetBool(characterMove.rollID, false);
    }
}
