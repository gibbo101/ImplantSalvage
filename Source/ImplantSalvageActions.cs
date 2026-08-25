using Multiplayer.API;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// Every mutation of shared, simulation-affecting state goes through here so it can be registered
/// as a Multiplayer sync method in one place (see MultiplayerCompat).
///
/// Static methods on purpose: MP syncs a static call by name and arguments alone, with no instance
/// to serialise on the far side.
/// </summary>
public static class ImplantSalvageActions
{
    /// <summary>
    /// Push the destroy-chance curve into the current game.
    ///
    /// Synced because DestroyChanceFor feeds a Rand.Chance roll inside the JobDriver - clients
    /// running different numbers would produce different extraction outcomes from the same tick.
    /// Called when the settings window closes, not while a slider is being dragged, so this is one
    /// message per edit rather than one per frame.
    /// </summary>
    public static void SetDestroyChances(float maxDestroyChance, float minDestroyChance)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        if (component == null)
        {
            return;
        }

        component.maxDestroyChance = maxDestroyChance;
        component.minDestroyChance = minDestroyChance;
    }

    /// <summary>
    /// Queue or unqueue one implant on a corpse. Synced: the designation and its queue are shared
    /// state, so every client must apply the change on the same tick.
    /// </summary>
    public static void ToggleExtraction(Corpse corpse, int implantLoadID)
    {
        ImplantSalvagePending.Toggle(corpse, implantLoadID);
    }

    /// <summary>Clear every extraction queued on a corpse - the Cancel button.</summary>
    public static void CancelExtractions(Corpse corpse)
    {
        ImplantSalvagePending.CancelAll(corpse);
    }
}

[StaticConstructorOnStartup]
public static class MultiplayerCompat
{
    static MultiplayerCompat()
    {
        if (!MP.enabled)
        {
            return;
        }

        MP.RegisterSyncMethod(typeof(ImplantSalvageActions), nameof(ImplantSalvageActions.SetDestroyChances));
        MP.RegisterSyncMethod(typeof(ImplantSalvageStorage), nameof(ImplantSalvageStorage.SetStorageImplantFilter));
        MP.RegisterSyncMethod(typeof(ImplantSalvageActions), nameof(ImplantSalvageActions.ToggleExtraction));
        MP.RegisterSyncMethod(typeof(ImplantSalvageActions), nameof(ImplantSalvageActions.CancelExtractions));
    }
}
