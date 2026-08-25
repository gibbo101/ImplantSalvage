using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ImplantSalvage;

/// <summary>
/// Walk to the corpse, work on it, then pull out the one implant this job was issued for.
///
/// TargetA is the corpse; job.count carries the implant's Hediff.loadID (see the provider for why
/// it rides on the Job rather than on this driver).
///
/// Multiplayer: everything that touches game state happens here, inside the simulation tick, so it
/// runs identically on every client. The float menu only ever issues the job.
/// </summary>
public class JobDriver_ExtractImplant : JobDriver
{
    private Corpse Corpse => job.targetA.Thing as Corpse;

    private int ImplantLoadID => job.count;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        // Reserving the corpse stops two doctors racing for the same body.
        return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);

        // The implant can vanish mid-job - another doctor finished first, or the corpse was
        // butchered. Bail rather than extracting something the player did not pick.
        this.FailOn(() => Corpse == null || ImplantSalvageUtility.FindImplant(Corpse, ImplantLoadID) == null);

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A);

        Hediff implantForWork = Corpse != null ? ImplantSalvageUtility.FindImplant(Corpse, ImplantLoadID) : null;
        int workTicks = implantForWork != null ? ImplantSalvageUtility.WorkTicksFor(implantForWork) : 600;

        Toil work = Toils_General.Wait(workTicks, TargetIndex.A);
        work.WithProgressBarToilDelay(TargetIndex.A);
        work.WithEffect(EffecterDefOf.Surgery, TargetIndex.A);
        work.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        work.activeSkill = () => SkillDefOf.Medicine;
        yield return work;

        yield return Toils_General.Do(delegate
        {
            Corpse corpse = Corpse;
            if (corpse == null)
            {
                return;
            }

            Hediff implant = ImplantSalvageUtility.FindImplant(corpse, ImplantLoadID);
            if (implant == null)
            {
                return;
            }

            int loadID = implant.loadID;
            ImplantSalvageUtility.Extract(pawn, corpse, implant);

            // Runs on every client inside the synced tick, so this must NOT go through a sync
            // method - that would apply the removal twice.
            ImplantSalvagePending.NotifyExtracted(corpse, loadID);
        });
    }
}
