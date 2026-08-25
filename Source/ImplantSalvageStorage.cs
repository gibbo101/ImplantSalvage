using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// The per-stockpile implant selection: which implants a given stockpile, shelf or storage group
/// considers worth hauling a body for.
///
/// The rule, stated once so it cannot drift:
///
///     a corpse is accepted if AT LEAST ONE of its implants is allowed.
///
/// So a body with a denied peg leg and an allowed bionic arm still gets hauled, because the arm is
/// reason enough to bring it in. Denying an implant means "do not bother fetching bodies whose only
/// salvage is this", never "refuse a body that happens to contain this".
///
/// Stored as a set of allowed defNames rather than as a ThingFilter, for two independent reasons:
///   - Multiplayer patches ThingFilter.SetAllow globally and redirects it to whichever filter the
///     UI is currently drawing (ThingFilterMarkers.DrawnThingFilter). Building a ThingFilter while
///     the Storage tab is open would therefore write into the STOCKPILE's filter in a Multiplayer
///     session and cancel our own call.
///   - defNames degrade quietly when the mod that supplied an implant is uninstalled.
/// </summary>
public static class ImplantSalvageStorage
{
    /// <summary>
    /// A stable save-scoped identity for a storage owner. Every branch keys off a scribed ID, so
    /// the key is identical on every Multiplayer client and survives save/reload.
    /// </summary>
    public static string KeyFor(IStoreSettingsParent parent)
    {
        return parent switch
        {
            Zone zone => "z" + zone.ID,
            StorageGroup group => "g" + group.loadID,
            ThingComp comp when comp.parent != null => "t" + comp.parent.thingIDNumber,
            Thing thing => "t" + thing.thingIDNumber,
            _ => null,
        };
    }

    /// <summary>
    /// The allowed implant defNames for this storage, or null when it has never been customised -
    /// which means everything is allowed.
    /// </summary>
    public static List<string> AllowedFor(IStoreSettingsParent parent)
    {
        if (parent == null)
        {
            return null;
        }

        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        if (component == null)
        {
            return null;
        }

        string key = KeyFor(parent);
        if (key == null)
        {
            return null;
        }

        return component.storageImplantAllowed.TryGetValue(key, out List<string> allowed) ? allowed : null;
    }

    /// <summary>Is this implant wanted by this storage? Null selection means "never customised".</summary>
    public static bool Allows(List<string> allowed, ThingDef product)
    {
        return allowed == null || allowed.Contains(product.defName);
    }

    /// <summary>
    /// Does this corpse hold at least one implant the given selection allows?
    ///
    /// Callers must check HasExtractableImplant first: a corpse with no implants at all is not this
    /// filter's business and stays governed by the "allow corpses without implants" filter.
    /// </summary>
    public static bool AnyImplantAllowed(Corpse corpse, List<string> allowed)
    {
        foreach (Hediff hediff in ImplantSalvageUtility.ExtractableImplants(corpse))
        {
            ThingDef product = hediff.def.spawnThingOnRemoved;
            if (product != null && Allows(allowed, product))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every corpse ThingDef, cached. Used to answer "could this storage ever hold a body?" without
    /// walking the filter's whole allowed set - which for an everything-allowed stockpile is on the
    /// order of a thousand defs, and the Storage tab would ask once per frame.
    /// </summary>
    public static List<ThingDef> AllCorpseDefs()
    {
        if (corpseDefsCache != null)
        {
            return corpseDefsCache;
        }

        List<ThingDef> corpses = new List<ThingDef>();
        List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].IsCorpse)
            {
                corpses.Add(all[i]);
            }
        }

        corpseDefsCache = corpses;
        return corpses;
    }

    private static List<ThingDef> corpseDefsCache;

    /// <summary>
    /// Apply a stockpile's implant selection. Synced: this decides what may be hauled where, so
    /// every client has to apply the same change on the same tick.
    /// </summary>
    public static void SetStorageImplantFilter(string key, List<string> allowedDefNames)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        if (component == null || key == null)
        {
            return;
        }

        if (allowedDefNames == null)
        {
            component.storageImplantAllowed.Remove(key);
            return;
        }

        // Stored even when it allows everything: a stockpile configured to accept every implant is
        // a real choice, and must not silently revert to "never customised" on the next redraw.
        component.storageImplantAllowed[key] = new List<string>(allowedDefNames);
    }
}
