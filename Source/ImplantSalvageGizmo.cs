using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// "Extract implant" and "Cancel" on a selected corpse, in the same gizmo row as Strip and Allow.
///
/// Marks the body rather than ordering a specific pawn, exactly like vanilla's skull extraction:
/// the mark is visible and cancellable, and whichever doctor is free services it through the work
/// system. An earlier version sent the nearest capable colonist immediately, which had two faults
/// this fixes - there was nothing to cancel, and it happily dragged a drafted pawn out of a fight.
///
/// The right-click menu keeps its immediate behaviour, including on drafted pawns. That is a
/// different thing and deliberately so: there you have picked the pawn yourself, so it is an order,
/// not a job posting.
///
/// There is no Cancel gizmo here. Vanilla's Cancel designator already clears our mark - that is the
/// pay-off for using a plain Designation - and skull extraction does not ship its own either.
/// Individual implants can still be unticked one at a time from the same list.
/// </summary>
public static class ImplantSalvageGizmo
{
    public static IEnumerable<Gizmo> GizmosFor(Corpse corpse)
    {
        if (corpse?.Map == null || !corpse.Spawned)
        {
            yield break;
        }

        ThingDef best = ImplantSalvageUtility.BestSalvageProduct(corpse);
        if (best == null)
        {
            // Nothing installed: no buttons, so ordinary corpses keep a clean gizmo row.
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "Luke_ExtractImplantGizmo".Translate(),
            defaultDesc = "Luke_ExtractImplantGizmoDesc".Translate(),
            icon = best.uiIcon,
            iconDrawScale = 0.85f,
            action = delegate { OpenImplantMenu(corpse); },
        };
    }

    /// <summary>
    /// One row per implant, ticking it on or off. Each is a separate deliberate choice - there is
    /// no "queue everything" entry, matching the rule that an order always names one implant.
    /// </summary>
    private static void OpenImplantMenu(Corpse corpse)
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>();

        foreach (Hediff implant in ImplantSalvageUtility.ExtractableImplants(corpse))
        {
            Hediff localImplant = implant;
            bool pending = ImplantSalvagePending.IsPending(corpse, localImplant.loadID);

            string label = ImplantSalvageUtility.ImplantLabel(localImplant);
            label = pending
                ? "Luke_ExtractImplantCancelOne".Translate(label)
                : "Luke_ExtractImplant".Translate(label);

            options.Add(new FloatMenuOption(label, delegate
            {
                ImplantSalvageActions.ToggleExtraction(corpse, localImplant.loadID);
            }, localImplant.def.spawnThingOnRemoved));
        }

        if (options.Count > 0)
        {
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
