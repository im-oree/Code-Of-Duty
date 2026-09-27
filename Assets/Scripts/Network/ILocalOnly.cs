/// <summary>
/// Marks a component that may only run on the machine that owns the character.
///
/// WHY THIS EXISTS
/// <see cref="NetComponentEnabler"/> used to know about local-only components purely through a
/// hand-filled list of object references on the prefab. That list is invisible from the code:
/// nothing stops someone adding a component that reads the keyboard and never touching the
/// prefab, and nothing complains when they don't. It had already gone stale — three components
/// were added to the player after the list was authored, and one of them read input on every
/// remote body in the match.
///
/// A marker interface puts the fact next to the code it is a fact about. Implementing it is the
/// whole opt-in: <see cref="NetComponentEnabler"/> finds every one of them at spawn and switches
/// them off on bodies this machine does not control. The explicit list still works and is still
/// the right tool for things you cannot mark — native components like Camera, or third-party
/// scripts — but anything we write says so itself.
///
/// This is checked by <c>Tools/verify-input-seam.mjs</c>, which fails if a component reads
/// player intent without being gated one of the three allowed ways.
/// </summary>
/// <remarks>
/// Implementing this does NOT make a component safe to run on a remote proxy — it makes it not
/// run there at all. If a component needs to do something on remote bodies (playing a synced
/// animation, say), it must not be marked; gate the local-only part with
/// <see cref="NetOwnership.IsLocal"/> instead.
/// </remarks>
public interface ILocalOnly
{
}
