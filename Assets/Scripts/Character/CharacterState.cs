using System.Collections.Generic;
using System.Text;

namespace CodeOfDuty.Character
{
    // ------------------------------------------------------------------------
    //  Five orthogonal channels. A flat state machine would need
    //  SPRINT_RELOADING_RIFLE, CROUCH_WALK_ADS_PISTOL, ... so state is split into
    //  channels that can each hold a state simultaneously, plus a few scalar facts.
    // ------------------------------------------------------------------------

    public enum LocomotionState { Idle, Walk, Sprint, TacSprint, CrouchIdle, CrouchWalk, Slide, Jump, Air, Landing }
    public enum TraversalState { None, Vault, Mantle }
    public enum WeaponActionState { None, Firing, Reloading, Switching, Inspecting, Melee, Cycling }
    public enum AimState { Hip, Ads }
    public enum CarryState { Ready, Lowered, Stowed }

    /// <summary>A record of one accepted transition or one rejection. Bounded to the last 200.</summary>
    public struct StateTransitionRecord
    {
        public string channel;
        public string from;
        public string to;
        public string source;
        public bool forced;
        public bool accepted;
        public string reason;
    }

    /// <summary>
    /// THE single authority for what the character is doing. Behaviour changes state only through
    /// the Request* methods, which validate against the transition tables and the cross-channel
    /// interaction rules; state is read only through the getters and derived helpers.
    ///
    /// Nothing else may hold an <c>isReloading</c>-style mirror boolean.
    /// </summary>
    public class CharacterState
    {
        const int LogCapacity = 200;

        // ---------------------------------------------------------------- channels

        public LocomotionState Locomotion { get; private set; } = LocomotionState.Idle;
        public TraversalState Traversal { get; private set; } = TraversalState.None;
        public WeaponActionState WeaponAction { get; private set; } = WeaponActionState.None;
        public AimState Aim { get; private set; } = AimState.Hip;
        public CarryState Carry { get; private set; } = CarryState.Ready;

        // ---------------------------------------------------------------- facts (scalars, not channels)

        /// <summary>Whether the character is standing on ground. A fact, not a state.</summary>
        public bool IsGrounded { get; set; }

        /// <summary>The equipped weapon id; empty hands is <c>null</c>.</summary>
        public string WeaponId { get; set; }

        // ---------------------------------------------------------------- logs

        readonly List<StateTransitionRecord> accepted = new List<StateTransitionRecord>(LogCapacity);
        readonly List<StateTransitionRecord> rejections = new List<StateTransitionRecord>(LogCapacity);

        public IReadOnlyList<StateTransitionRecord> AcceptedLog => accepted;
        public IReadOnlyList<StateTransitionRecord> RejectionLog => rejections;

        // ---------------------------------------------------------------- transitions

        public static readonly Dictionary<LocomotionState, LocomotionState[]> RawLocomotionTable =
            new Dictionary<LocomotionState, LocomotionState[]>
        {
            { LocomotionState.Idle,       new[] { LocomotionState.Walk, LocomotionState.Sprint, LocomotionState.CrouchIdle, LocomotionState.Jump, LocomotionState.Air, LocomotionState.Slide } },
            { LocomotionState.Walk,       new[] { LocomotionState.Idle, LocomotionState.Sprint, LocomotionState.CrouchWalk, LocomotionState.CrouchIdle, LocomotionState.Jump, LocomotionState.Air, LocomotionState.Slide } },
            { LocomotionState.Sprint,     new[] { LocomotionState.Idle, LocomotionState.Walk, LocomotionState.TacSprint, LocomotionState.Jump, LocomotionState.Air, LocomotionState.Slide, LocomotionState.CrouchWalk } },
            { LocomotionState.TacSprint,  new[] { LocomotionState.Sprint, LocomotionState.Idle, LocomotionState.Walk, LocomotionState.Jump, LocomotionState.Air, LocomotionState.Slide } },
            { LocomotionState.CrouchIdle, new[] { LocomotionState.Idle, LocomotionState.CrouchWalk, LocomotionState.Walk, LocomotionState.Jump, LocomotionState.Air } },
            { LocomotionState.CrouchWalk, new[] { LocomotionState.CrouchIdle, LocomotionState.Walk, LocomotionState.Idle, LocomotionState.Jump, LocomotionState.Air } },
            { LocomotionState.Slide,      new[] { LocomotionState.Idle, LocomotionState.Walk, LocomotionState.CrouchIdle, LocomotionState.CrouchWalk, LocomotionState.Air, LocomotionState.Jump } },
            { LocomotionState.Jump,       new[] { LocomotionState.Air, LocomotionState.Landing } },
            { LocomotionState.Air,        new[] { LocomotionState.Landing } },
            { LocomotionState.Landing,    new[] { LocomotionState.Idle, LocomotionState.Walk, LocomotionState.Sprint, LocomotionState.CrouchIdle, LocomotionState.CrouchWalk } },
        };

        public static readonly Dictionary<TraversalState, TraversalState[]> RawTraversalTable =
            new Dictionary<TraversalState, TraversalState[]>
        {
            { TraversalState.None,   new[] { TraversalState.Vault, TraversalState.Mantle } },
            { TraversalState.Vault,  new[] { TraversalState.None } },
            { TraversalState.Mantle, new[] { TraversalState.None } },
        };

        public static readonly Dictionary<WeaponActionState, WeaponActionState[]> RawWeaponActionTable =
            new Dictionary<WeaponActionState, WeaponActionState[]>
        {
            { WeaponActionState.None,       new[] { WeaponActionState.Firing, WeaponActionState.Reloading, WeaponActionState.Switching, WeaponActionState.Inspecting, WeaponActionState.Melee, WeaponActionState.Cycling } },
            { WeaponActionState.Firing,     new[] { WeaponActionState.None, WeaponActionState.Reloading } },
            { WeaponActionState.Reloading,  new[] { WeaponActionState.None, WeaponActionState.Switching } },
            { WeaponActionState.Switching,  new[] { WeaponActionState.None } },
            { WeaponActionState.Inspecting, new[] { WeaponActionState.None, WeaponActionState.Firing } },
            { WeaponActionState.Melee,      new[] { WeaponActionState.None } },
            { WeaponActionState.Cycling,    new[] { WeaponActionState.None } },
        };

        public static readonly Dictionary<AimState, AimState[]> RawAimTable =
            new Dictionary<AimState, AimState[]>
        {
            { AimState.Hip, new[] { AimState.Ads } },
            { AimState.Ads, new[] { AimState.Hip } },
        };

        public static readonly Dictionary<CarryState, CarryState[]> RawCarryTable =
            new Dictionary<CarryState, CarryState[]>
        {
            { CarryState.Ready,   new[] { CarryState.Lowered, CarryState.Stowed } },
            { CarryState.Lowered, new[] { CarryState.Ready, CarryState.Stowed } },
            { CarryState.Stowed,  new[] { CarryState.Ready } },
        };

        static Dictionary<LocomotionState, LocomotionState[]> locomotionTable;
        static Dictionary<TraversalState, TraversalState[]> traversalTable;
        static Dictionary<WeaponActionState, WeaponActionState[]> weaponActionTable;
        static Dictionary<AimState, AimState[]> aimTable;
        static Dictionary<CarryState, CarryState[]> carryTable;

        static Dictionary<T, T[]> Build<T>(Dictionary<T, T[]> raw)
        {
            var set = new Dictionary<T, HashSet<T>>();
            foreach (var pair in raw) set[pair.Key] = new HashSet<T>(pair.Value);
            var frozen = new Dictionary<T, T[]>();
            foreach (var pair in set)
            {
                var arr = new T[pair.Value.Count];
                pair.Value.CopyTo(arr);
                frozen[pair.Key] = arr;
            }
            return frozen;
        }

        static CharacterState()
        {
            locomotionTable = Build(RawLocomotionTable);
            traversalTable = Build(RawTraversalTable);
            weaponActionTable = Build(RawWeaponActionTable);
            aimTable = Build(RawAimTable);
            carryTable = Build(RawCarryTable);
        }

        // ---------------------------------------------------------------- requests

        public bool RequestLocomotion(LocomotionState to, string source, bool force = false)
        {
            if (to == Locomotion) return true;
            if (!force && !HasTransition(locomotionTable, Locomotion, to))
                return Reject("locomotion", Locomotion.ToString(), to.ToString(), source, force,
                    "no transition from " + Locomotion + " to " + to);
            if (!CheckInteractions("locomotion", to.ToString(), source, out string veto))
                return Reject("locomotion", Locomotion.ToString(), to.ToString(), source, force, veto);

            LocomotionState from = Locomotion;
            Locomotion = to;
            ApplyInteractionEffects("locomotion", to.ToString());
            Accept("locomotion", from.ToString(), to.ToString(), source, force);
            return true;
        }

        public bool RequestTraversal(TraversalState to, string source, bool force = false)
        {
            if (to == Traversal) return true;
            if (!force && !HasTransition(traversalTable, Traversal, to))
                return Reject("traversal", Traversal.ToString(), to.ToString(), source, force,
                    "no transition from " + Traversal + " to " + to);
            if (!CheckInteractions("traversal", to.ToString(), source, out string veto))
                return Reject("traversal", Traversal.ToString(), to.ToString(), source, force, veto);

            TraversalState from = Traversal;
            Traversal = to;
            ApplyInteractionEffects("traversal", to.ToString());
            Accept("traversal", from.ToString(), to.ToString(), source, force);
            return true;
        }

        public bool RequestWeaponAction(WeaponActionState to, string source, bool force = false)
        {
            if (to == WeaponAction) return true;
            if (!force && !HasTransition(weaponActionTable, WeaponAction, to))
                return Reject("weaponAction", WeaponAction.ToString(), to.ToString(), source, force,
                    "no transition from " + WeaponAction + " to " + to);
            if (!CheckInteractions("weaponAction", to.ToString(), source, out string veto))
                return Reject("weaponAction", WeaponAction.ToString(), to.ToString(), source, force, veto);

            WeaponActionState from = WeaponAction;
            WeaponAction = to;
            ApplyInteractionEffects("weaponAction", to.ToString());
            Accept("weaponAction", from.ToString(), to.ToString(), source, force);
            return true;
        }

        public bool RequestAim(AimState to, string source, bool force = false)
        {
            if (to == Aim) return true;
            if (!force && !HasTransition(aimTable, Aim, to))
                return Reject("aim", Aim.ToString(), to.ToString(), source, force,
                    "no transition from " + Aim + " to " + to);
            if (!CheckInteractions("aim", to.ToString(), source, out string veto))
                return Reject("aim", Aim.ToString(), to.ToString(), source, force, veto);

            AimState from = Aim;
            Aim = to;
            ApplyInteractionEffects("aim", to.ToString());
            Accept("aim", from.ToString(), to.ToString(), source, force);
            return true;
        }

        public bool RequestCarry(CarryState to, string source, bool force = false)
        {
            if (to == Carry) return true;
            if (!force && !HasTransition(carryTable, Carry, to))
                return Reject("carry", Carry.ToString(), to.ToString(), source, force,
                    "no transition from " + Carry + " to " + to);
            if (!CheckInteractions("carry", to.ToString(), source, out string veto))
                return Reject("carry", Carry.ToString(), to.ToString(), source, force, veto);

            CarryState from = Carry;
            Carry = to;
            ApplyInteractionEffects("carry", to.ToString());
            Accept("carry", from.ToString(), to.ToString(), source, force);
            return true;
        }

        static bool HasTransition<T>(Dictionary<T, T[]> table, T from, T to)
        {
            if (!table.TryGetValue(from, out T[] allowed)) return false;
            for (int i = 0; i < allowed.Length; i++)
                if (EqualityComparer<T>.Default.Equals(allowed[i], to)) return true;
            return false;
        }

        // ---------------------------------------------------------------- interaction rules (declared ONCE)

        /// <summary>Vetoes. Returns false when the requested change must be refused.</summary>
        bool CheckInteractions(string channel, string to, string source, out string veto)
        {
            veto = null;

            bool traversing = Traversal == TraversalState.Vault || Traversal == TraversalState.Mantle;
            bool traversalEntering = channel == "traversal" && (to == nameof(TraversalState.Vault) || to == nameof(TraversalState.Mantle));

            // traversal owns the whole body while climbing
            if (traversing && !(channel == "traversal"))
            {
                veto = "traversal in progress (" + Traversal + ") vetoes " + channel;
                return false;
            }

            // sliding cannot start a climb
            if (channel == "traversal" && Locomotion == LocomotionState.Slide)
            {
                veto = "slide vetoes traversal";
                return false;
            }

            // ADS requires the weapon actually in position
            if (channel == "aim" && to == nameof(AimState.Ads))
            {
                if (Carry != CarryState.Ready)
                {
                    veto = "carry is " + Carry + " — cannot ADS";
                    return false;
                }
                if (Locomotion == LocomotionState.Sprint || Locomotion == LocomotionState.TacSprint)
                {
                    veto = "sprinting vetoes ADS";
                    return false;
                }
            }

            // traversal cannot begin while mid weapon action or aiming
            if (traversalEntering && WeaponAction != WeaponActionState.None)
            {
                veto = "weapon action " + WeaponAction + " vetoes traversal";
                return false;
            }

            return true;
        }

        /// <summary>Consequences — applied when a change lands, never scattered through gameplay code.</summary>
        void ApplyInteractionEffects(string channel, string to)
        {
            if (channel == "traversal")
            {
                if (to == nameof(TraversalState.Vault) || to == nameof(TraversalState.Mantle))
                {
                    // both hands on the ledge: stow the weapon, drop aim, stop any weapon action
                    ForceAim(AimState.Hip);
                    ForceWeaponAction(WeaponActionState.None);
                    ForceCarry(CarryState.Stowed);
                }
                else if (to == nameof(TraversalState.None))
                {
                    ForceCarry(CarryState.Ready);
                }
            }

            if (channel == "weaponAction" && (to == nameof(WeaponActionState.Reloading) || to == nameof(WeaponActionState.Switching)))
            {
                ForceAim(AimState.Hip);
            }

            if (channel == "locomotion")
            {
                if (to == nameof(LocomotionState.Sprint) || to == nameof(LocomotionState.TacSprint))
                {
                    ForceAim(AimState.Hip);
                    if (to == nameof(LocomotionState.TacSprint))
                        ForceCarry(CarryState.Lowered);
                }
                else if (to == nameof(LocomotionState.Idle) || to == nameof(LocomotionState.Walk))
                {
                    // leaving tac sprint restores the raised weapon
                    if (Carry == CarryState.Lowered) ForceCarry(CarryState.Ready);
                }
            }
        }

        // forced transitions are only for completion/consequence; still logged
        void ForceAim(AimState to)
        {
            if (Aim == to) return;
            AimState from = Aim;
            Aim = to;
            Accept("aim", from.ToString(), to.ToString(), "CharacterState.interaction", true);
        }

        void ForceWeaponAction(WeaponActionState to)
        {
            if (WeaponAction == to) return;
            WeaponActionState from = WeaponAction;
            WeaponAction = to;
            Accept("weaponAction", from.ToString(), to.ToString(), "CharacterState.interaction", true);
        }

        void ForceCarry(CarryState to)
        {
            if (Carry == to) return;
            CarryState from = Carry;
            Carry = to;
            Accept("carry", from.ToString(), to.ToString(), "CharacterState.interaction", true);
        }

        // ---------------------------------------------------------------- derived helpers (prefer these)

        public bool CanFire() =>
            !IsBusy() &&
            Traversal == TraversalState.None &&
            (WeaponAction == WeaponActionState.None || WeaponAction == WeaponActionState.Firing) &&
            Locomotion != LocomotionState.Sprint &&
            Locomotion != LocomotionState.TacSprint;

        public bool IsBusy() =>
            WeaponAction == WeaponActionState.Reloading ||
            WeaponAction == WeaponActionState.Switching ||
            WeaponAction == WeaponActionState.Melee ||
            WeaponAction == WeaponActionState.Cycling ||
            WeaponAction == WeaponActionState.Inspecting;

        public bool CanTraverse() =>
            Traversal == TraversalState.None &&
            WeaponAction == WeaponActionState.None &&
            Locomotion != LocomotionState.Slide;

        public bool CanAds() =>
            Carry == CarryState.Ready &&
            Traversal == TraversalState.None &&
            Locomotion != LocomotionState.Sprint &&
            Locomotion != LocomotionState.TacSprint;

        // ---------------------------------------------------------------- logging

        void Accept(string channel, string from, string to, string source, bool forced)
        {
            Push(accepted, new StateTransitionRecord
            {
                channel = channel, from = from, to = to, source = source, forced = forced, accepted = true
            });
        }

        bool Reject(string channel, string from, string to, string source, bool force, string reason)
        {
            Push(rejections, new StateTransitionRecord
            {
                channel = channel, from = from, to = to, source = source, forced = force, accepted = false, reason = reason
            });
            return false;
        }

        static void Push(List<StateTransitionRecord> log, StateTransitionRecord record)
        {
            if (log.Count >= LogCapacity) log.RemoveAt(0);
            log.Add(record);
        }

        /// <summary>Every channel + fact right now, for the debug overlay.</summary>
        public string Snapshot()
        {
            var sb = new StringBuilder(160);
            sb.Append("locomotion=").Append(Locomotion)
              .Append(" traversal=").Append(Traversal)
              .Append(" weaponAction=").Append(WeaponAction)
              .Append(" aim=").Append(Aim)
              .Append(" carry=").Append(Carry)
              .Append(" grounded=").Append(IsGrounded)
              .Append(" weapon=").Append(string.IsNullOrEmpty(WeaponId) ? "none" : WeaponId);
            return sb.ToString();
        }

        /// <summary>Editor-time check: every enum member must appear in its transition table.</summary>
        public static bool TableIsComplete<T>(Dictionary<T, T[]> table)
        {
            foreach (T value in System.Enum.GetValues(typeof(T)))
                if (!table.ContainsKey(value))
                    return false;
            return true;
        }
    }
}
