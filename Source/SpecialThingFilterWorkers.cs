using Verse;

namespace ImplantSalvage;

/// <summary>
/// Corpses that still hold an implant worth salvaging.
///
/// One filter pair covers three separate player asks, because storage settings and bill ingredient
/// filters are the same ThingFilter UI:
///   - a stockpile that accepts only implanted corpses (untick the companion filter below);
///   - a crematorium that refuses to burn a body with a bionic in it (untick this one);
///   - a butcher table that does the same.
///
/// Both filters ship allowedByDefault, so adding this mod changes no existing stockpile, bill or
/// save. Protecting implants is something the player switches on deliberately.
///
/// Matches() honours the per-implant settings list, so unticking prosthetic legs means a corpse
/// carrying nothing but prosthetic legs counts as having nothing worth saving and burns normally.
/// </summary>
public class SpecialThingFilterWorker_CorpsesWithImplants : SpecialThingFilterWorker
{
    public override bool Matches(Thing t)
    {
        return t is Corpse corpse && ImplantSalvageUtility.HasSalvageableImplant(corpse);
    }

    public override bool CanEverMatch(ThingDef def)
    {
        return def.IsCorpse;
    }
}

/// <summary>
/// The companion: corpses with nothing left worth pulling out. Untick this on a stockpile to get a
/// salvage-only pile that hauls implanted bodies and ignores the rest.
/// </summary>
public class SpecialThingFilterWorker_CorpsesNoImplants : SpecialThingFilterWorker
{
    public override bool Matches(Thing t)
    {
        return t is Corpse corpse && !ImplantSalvageUtility.HasSalvageableImplant(corpse);
    }

    public override bool CanEverMatch(ThingDef def)
    {
        return def.IsCorpse;
    }
}
