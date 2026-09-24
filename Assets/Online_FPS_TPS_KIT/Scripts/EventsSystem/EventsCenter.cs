using System.Reflection;
using UnityEngine;

public class EventsCenter : MonoBehaviour
{
    public delegate void RightHandIKWeightUpdate(float weight);
    public event RightHandIKWeightUpdate OnRightHandIKWeightUpdate;

    public delegate void LeftHandIKWeightUpdate(float weight2);
    public event LeftHandIKWeightUpdate OnLeftHandIKWeightUpdate;

    public delegate void GunWeightUpdate(float activeID);
    public event GunWeightUpdate OnGunWeightUpdate;

    public delegate void GunOffsetRelativeToParent(int handID, int ApplyOffset);
    public event GunOffsetRelativeToParent OnGunOffsetRelativeToParent;

    public delegate void ApplyGunPositionOffset(float applyOffset);
    public event ApplyGunPositionOffset OnApplyGunPositionOffset;

    public delegate void GunParentChange(float handID);
    public event GunParentChange OnGunParentChange;

    public delegate void HandIKTargetChange(int HandId, string TargetComponentName);
    public event HandIKTargetChange OnHandIKTargetChange;

    public delegate void WeaponChange(bool changed);
    public event WeaponChange OnWeaponChange;

    public void EventInvoke(string eventName, object[] parameters)
    {
        // Debug.Log(eventName);
        var eventInfo = this.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (eventInfo != null)
        {
            var eventMember = eventInfo.GetValue(this);
            // Note : If event_member is null, nobody registered to the event, you can't call it.
            eventMember?.GetType().GetMethod("Invoke")?.Invoke(eventMember, parameters);
        }
    }

    private void OnEnable()
    {
        var animator = GetComponent<Animator>();
        var stateMachineBehaviours = animator.GetBehaviours<StateMachineBehaviour>();
        foreach (var stateMachineBehaviour in stateMachineBehaviours)
        {
            if (stateMachineBehaviour is IEventCenterComponent iEventCenterComponent)
            {
                iEventCenterComponent.EventsCenter = this;
            }
        }
    }
}

