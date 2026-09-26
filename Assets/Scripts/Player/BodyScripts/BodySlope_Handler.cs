using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BodySlope_Handler : MonoBehaviour
{
    [SerializeField] private BodySlopeRig bodySlope;
    [SerializeField] private float maxSlopeAngle;
    [SerializeField] private float bodySlopeChangeRate = 5f;
    [SerializeField] private float valueApplyOffset = 0.01f;
    [SerializeField] private Transform playerCameraPosition;
    [SerializeField] private LayerMask collisionMask;
    public float targetAngle;
    [SerializeField] private float hitDistance;

    /// <summary>Last lean intent pushed in, kept raw so the collision probe can reuse it.</summary>
    float leanInput;

    public void setInput(float InputAngle)
    {
        leanInput = InputAngle;
    }

    void LateUpdate()
    {
        // Probe first, then build the target from this frame's clearance. Previously the probe
        // ran last, so the lean angle was always scaled by the *previous* frame's wall distance
        // and the body clipped a corner for one frame on the way in.
        CheckBodyCollision();
        targetAngle = leanInput * maxSlopeAngle * hitDistance;
        bodySlope.slopeAngle = SmoothValue(bodySlope.slopeAngle, targetAngle, bodySlopeChangeRate);
    }

    private float SmoothValue(float inputValue, float targetvalue, float changeRateValue)
    {
        if (inputValue < targetvalue - valueApplyOffset || inputValue > targetvalue + valueApplyOffset)
        {
            inputValue = Mathf.Lerp(inputValue, targetvalue, Time.deltaTime * changeRateValue);

            return inputValue;
        }
        else
        {
            return targetvalue;
        }
    }

    private void CheckBodyCollision()
    {
        // Probe along the lean the character actually asked for, whoever asked for it —
        // reading the keyboard here would make a leaning bot clip through walls.
        Vector3 probe = playerCameraPosition.right * (leanInput * 0.51f);
        Debug.DrawLine(playerCameraPosition.position, playerCameraPosition.position + probe);
        if (Physics.Linecast(playerCameraPosition.position, playerCameraPosition.position + probe, out RaycastHit raycastHit, collisionMask))
        {
            hitDistance = Mathf.Lerp(0, 1, Vector3.Distance(playerCameraPosition.position, raycastHit.point) / 0.5f);
        }
        else
        {
            hitDistance = 1;
        }
        /* if (Physics.Linecast(transform.position + Vector3.up * playerCamera.localPosition.y, transform.position + Vector3.up * playerCamera.localPosition.y + playerCamera.right * (Input.GetAxisRaw("Slope") * 0.51f), out RaycastHit raycastHit, collisionMask))
        {
            hitDistance = Mathf.Lerp(0, 1, Vector3.Distance(transform.position + Vector3.up * playerCamera.localPosition.y, raycastHit.point) / 0.5f);
            Debug.Log("hitted + " + raycastHit.transform.name);
            hit = raycastHit.transform;
        }
        else
        {
            hitDistance = 1;
        } */
    }
}
