using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ImplantSalvage;

/// <summary>
/// Walk to the corpse, work on it, then pull out the one implant this job was issued for.
///
/// TargetA is the corpse; job.count carries the implant's Hediff.loadID (see
/// ImplantSalvageUtility.MakeExtractJob for why it rides on the Job rather than on this driver).
///
/// Multiplayer: everything that touches game state happens here, inside the simulation tick, so it
/// runs identically on every client. The float menu only ever issues the job.
/// </summary>
public class JobDriver_ExtractImplant : JobDriver
{
    private Corpse Corpse => job.targetA.Thing as Corpse;

    private int ImplantLoadID => job.count;

    /// <summary>The implant this job was issued for, or null once it has gone.</summary>
    private Hediff Implant => ImplantSalvageUtility.FindImplant(Corpse, ImplantLoadID);

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
        this.FailOn(() => Implant == null);

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A);

        Hediff implantForWork = Implant;
        int workTicks = implantForWork != null
            ? ImplantSalvageUtility.WorkTicksFor(implantForWork)
            : ImplantSalvageUtility.MinWorkTicks;

        Toil work = Toils_General.Wait(workTicks, TargetIndex.A);
        work.WithProgressBarToilDelay(TargetIndex.A);
        work.WithEffect(EffecterDefOf.Surgery, TargetIndex.A);
        work.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        work.activeSkill = () => SkillDefOf.Medicine;
        yield return work;

        yield return Toils_General.Do(delegate
        {
            Corpse corpse = Corpse;
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
