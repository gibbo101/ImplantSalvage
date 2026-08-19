using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ImplantSalvage;

/// <summary>
/// Adds "Extract &lt;implant&gt;" to the right-click menu on a corpse.
///
/// No Harmony patch is needed: FloatMenuMakerMap.Init() builds its provider list from
/// typeof(FloatMenuOptionProvider).AllSubclassesNonAbstract(), so this class registers itself.
///
/// Multiplayer: every option's action does exactly one game-state thing -
/// Pawn_JobTracker.TryTakeOrderedJob - which Multiplayer already syncs. Nothing else may happen
/// in these delegates: they run only on the client that clicked. In particular the intact-chance
/// label is a pure read of a stat and must never consume Rand, or opening a menu would desync
/// the simulation.
///
/// This is not Strip. Vanilla's FloatMenuOptionProvider_Strip / JobDefOf.Strip handle apparel and
/// equipment only, with no skill check and no Doctor requirement. The two are independent and a
/// corpse can be stripped and extracted in either order.
/// </summary>
public class FloatMenuOptionProvider_ExtractImplant : FloatMenuOptionProvider
{
    protected override bool Drafted => false;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;

    protected override bool RequiresManipulation => true;

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
    {
        if (clickedThing is not Corpse corpse)
        {
            yield break;
        }

        List<Hediff> implants = new List<Hediff>();
        foreach (Hediff hediff in ImplantSalvageUtility.ExtractableImplants(corpse))
        {
            implants.Add(hediff);
        }

        // Nothing installed: no option at all, so the menu stays clean on ordinary corpses.
        if (implants.Count == 0)
        {
            yield break;
        }

        Pawn surgeon = context.FirstSelectedPawn;
        string cannotReason = ImplantSalvageUtility.CannotExtractReason(surgeon, corpse);

        // One implant: name it directly. There is deliberately no "extract all" at any count -
        // extraction mirrors installation, which is always one implant at a time.
        if (implants.Count == 1)
        {
            yield return MakeOption(surgeon, corpse, implants[0], cannotReason);
            yield break;
        }

        // Several: a submenu with one row per implant, each naming the implant and its body part.
        string submenuLabel = "Luke_ExtractImplantSubmenu".Translate();
        if (cannotReason != null)
        {
            yield return new FloatMenuOption(submenuLabel + ": " + cannotReason, null);
            yield break;
        }

        yield return new FloatMenuOption(submenuLabel, delegate
        {
            List<FloatMenuOption> subOptions = new List<FloatMenuOption>();
            foreach (Hediff hediff in ImplantSalvageUtility.ExtractableImplants(corpse))
            {
                subOptions.Add(MakeOption(surgeon, corpse, hediff,
                    ImplantSalvageUtility.CannotExtractReason(surgeon, corpse)));
            }

            if (subOptions.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(subOptions));
            }
        });
    }

    private static FloatMenuOption MakeOption(Pawn surgeon, Corpse corpse, Hediff implant, string cannotReason)
    {
        string label = "Luke_ExtractImplant".Translate(ImplantSalvageUtility.ImplantLabel(implant));

        if (cannotReason != null)
        {
            return new FloatMenuOption(label + ": " + cannotReason, null);
        }

        if (ImplantSalvageMod.Settings.showIntactChance)
        {
            float intact = 1f - ImplantSalvageUtility.DestroyChanceFor(surgeon);
            label += " (" + "Luke_ExtractImplantIntact".Translate(intact.ToStringPercent()) + ")";
        }

        int implantLoadID = implant.loadID;

        FloatMenuOption option = new FloatMenuOption(label, delegate
        {
            Job job = JobMaker.MakeJob(ImplantSalvageDefOf.Luke_ExtractImplant, corpse);

            // Which implant. An int on the Job, because Multiplayer serialises the Job itself
            // (SyncMethod.Register(...TryTakeOrderedJob).ExposeParameter(0)) and the JobDriver
            // does not exist yet at that point - so the data cannot live on the driver.
            job.count = implantLoadID;

            surgeon.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }, implant.def.spawnThingOnRemoved);

        return FloatMenuUtility.DecoratePrioritizedTask(option, surgeon, new LocalTargetInfo(corpse));
    }
}
