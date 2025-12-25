namespace Content.Shared.Kitchen;

/// <summary>
/// Raised on an entity when it is inside a fryer and it starts frying.
/// unused for now, might swap to it later.
/// </summary>
public sealed class BeingFriedEvent(EntityUid fryer, EntityUid? user) : HandledEntityEventArgs
{
    public EntityUid DeepFryer = fryer;
    public EntityUid? User = user;
}
