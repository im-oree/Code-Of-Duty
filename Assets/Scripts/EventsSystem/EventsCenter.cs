using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Raise the weapon-change event from code. Normally it is fired by animation events
    /// through reflection, but a loadout swap has to tell the same listeners to re-read the
    /// weapon without an animation having played.
    /// </summary>
    public void InvokeWeaponChange(bool changing) => OnWeaponChange?.Invoke(changing);

    static readonly HashSet<string> reported = new HashSet<string>();

    /// <summary>
    /// Fire an animation event by name, delivering it to every subscriber independently.
    ///
    /// Each subscriber is invoked inside its own try/catch, and that is the whole point. These
    /// events arrive in ordered batches from the animator: drawing a weapon fires the gun
    /// position offset, then both hand IK weights, then the slot weight that actually lifts the
    /// gun into the hands, then the parenting, then the IK targets. Previously the batch was a
    /// single reflective Invoke over a multicast delegate, so the first subscriber to throw
    /// aborted everything behind it -- including the slot weight. One null reference in the
    /// first handler therefore left the player holding nothing, with hands posed for a gun that
    /// was still sitting on its holster mount, and the only clue was a stack trace pointing at
    /// a method that had nothing to do with the missing gun.
    ///
    /// Failures are logged once per event and handler, loudly enough to be fixed but without
    /// filling the console at sixty frames a second.
    /// </summary>
    public void EventInvoke(string eventName, object[] parameters)
    {
        var eventInfo = GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (eventInfo == null)
        {
            ReportOnce("missing:" + eventName,
                $"EventsCenter has no event named '{eventName}'. Check the animation event spelling.");
            return;
        }

        // Null simply means nobody has subscribed.
        if (!(eventInfo.GetValue(this) is Delegate handlers)) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                handler.DynamicInvoke(parameters);
            }
            catch (Exception exception)
            {
                // Reflection wraps whatever the handler threw; the inner one is the real fault.
                Exception cause = exception is TargetInvocationException wrapped && wrapped.InnerException != null
                    ? wrapped.InnerException
                    : exception;

                string target = handler.Method != null
                    ? handler.Method.DeclaringType?.Name + "." + handler.Method.Name
                    : "<unknown>";

                ReportOnce(eventName + "->" + target,
                    $"Animation event '{eventName}' failed in {target}: {cause.Message}\n{cause.StackTrace}");
            }
        }
    }

    static void ReportOnce(string key, string message)
    {
        if (!reported.Add(key)) return;
        Debug.LogError(message);
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

