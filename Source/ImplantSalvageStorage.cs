using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
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
            Zone zone => ZoneKeyPrefix + zone.ID,
            StorageGroup group => GroupKeyPrefix + group.loadID,
            ThingComp comp when comp.parent != null => ThingKeyPrefix + comp.parent.thingIDNumber,
            Thing thing => ThingKeyPrefix + thing.thingIDNumber,
            _ => null,
        };
    }

    private const string ZoneKeyPrefix = "z";
    private const string GroupKeyPrefix = "g";
    private const string ThingKeyPrefix = "t";

    /// <summary>
    /// Drop the selections of storage that no longer exists anywhere. Zones and storage groups
    /// only exist on maps. A storage building keeps its selection while it exists in any form:
    /// built, blueprinted or framed on a map, uninstalled on a map, or carried by a caravan.
    /// Runs inside the simulation tick, so every Multiplayer client drops the same keys.
    /// </summary>
    public static void PruneStaleSelections(GameComponent_ImplantSalvage component)
    {
        if (component.storageImplantAllowed.Count == 0)
        {
            return;
        }

        HashSet<int> zones = new HashSet<int>();
        HashSet<int> groups = new HashSet<int>();
        HashSet<int> things = new HashSet<int>();
        foreach (Map map in Find.Maps)
        {
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                zones.Add(zone.ID);
            }

            foreach (StorageGroup group in map.storageGroups.StorageGroupsForReading)
            {
                groups.Add(group.loadID);
            }

            foreach (Thing thing in map.listerThings.AllThings)
            {
                AddThingAndContents(things, thing);
            }
        }

        foreach (Caravan caravan in Find.WorldObjects.Caravans)
        {
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
            {
                AddThingAndContents(things, thing);
            }
        }

        List<string> stale = new List<string>();
        foreach (string key in component.storageImplantAllowed.Keys)
        {
            if (!IsLive(key, zones, groups, things))
            {
                stale.Add(key);
            }
        }

        foreach (string key in stale)
        {
            component.storageImplantAllowed.Remove(key);
        }
    }

    private static void AddThingAndContents(HashSet<int> things, Thing thing)
    {
        things.Add(thing.thingIDNumber);
        if (thing is MinifiedThing minified && minified.InnerThing != null)
        {
            things.Add(minified.InnerThing.thingIDNumber);
        }
    }

    /// <summary>Whether the key's owner exists. A key this mod did not write is kept.</summary>
    private static bool IsLive(string key, HashSet<int> zones, HashSet<int> groups, HashSet<int> things)
    {
        if (key.Length < 2 || !int.TryParse(key.Substring(1), out int id))
        {
            return true;
        }

        return key.Substring(0, 1) switch
        {
            ZoneKeyPrefix => zones.Contains(id),
            GroupKeyPrefix => groups.Contains(id),
            ThingKeyPrefix => things.Contains(id),
            _ => true,
        };
    }

    /// <summary>
    /// The allowed implant defNames for this storage, or null when it has never been customised -
    /// which means everything is allowed.
    /// </summary>
    public static List<string> AllowedFor(IStoreSettingsParent parent)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        string key = KeyFor(parent);
        if (component == null || key == null)
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
            if (Allows(allowed, hediff.def.spawnThingOnRemoved))
            {
                return true;
            }
        }

        return false;
    }

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
