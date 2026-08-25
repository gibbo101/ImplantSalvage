using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ImplantSalvage;

/// <summary>
/// Doctor work: service corpses marked for implant extraction.
///
/// This is what makes the marked-body model behave like vanilla's skull extraction. Because the
/// job arrives through the work system rather than as an ordered job, a drafted pawn is never
/// pulled in (drafted pawns do not run work givers at all), and a colonist with Doctor switched
/// off simply never picks it up.
/// </summary>
public class WorkGiver_ExtractImplant : WorkGiver_Scanner
{
    public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Corpse);

    public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

    /// <summary>
    /// Scan only the marked corpses rather than every body on the map - after a big raid the corpse
    /// list is long and almost none of it is designated.
    /// </summary>
    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        foreach (Designation designation in
                 pawn.Map.designationManager.SpawnedDesignationsOfDef(ImplantSalvageDefOf.Luke_ExtractImplantMark))
        {
            if (designation.target.Thing is Corpse corpse)
            {
                yield return corpse;
            }
        }
    }

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        return !pawn.Map.designationManager.AnySpawnedDesignationOfDef(ImplantSalvageDefOf.Luke_ExtractImplantMark);
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Corpse corpse || !corpse.Spawned)
        {
            return false;
        }

        if (pawn.Map.designationManager.DesignationOn(corpse, ImplantSalvageDefOf.Luke_ExtractImplantMark) == null)
        {
            return false;
        }

        // Everything queued may already be gone - another doctor got there first, or the part was
        // destroyed. Leave the designation for the tidy-up in the JobDriver rather than churning.
        if (ImplantSalvagePending.NextImplant(corpse) == null)
        {
            return false;
        }

        if (!pawn.CanReserve(corpse, 1, -1, null, forced))
        {
            return false;
        }

        return pawn.CanReach(corpse, PathEndMode.ClosestTouch, MaxPathDanger(pawn));
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Corpse corpse)
        {
            return null;
        }

        Hediff implant = ImplantSalvagePending.NextImplant(corpse);
        if (implant == null)
        {
            return null;
        }

        Job job = JobMaker.MakeJob(ImplantSalvageDefOf.Luke_ExtractImplant, corpse);
        job.count = implant.loadID;
        return job;
    }
}
