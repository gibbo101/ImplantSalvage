using System.Collections.Generic;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// One corpse's queued extractions. Kept beside the designation rather than inside it because
/// Designation.ExposeData is not virtual - a Designation subclass carrying extra scribed fields
/// only round-trips if it re-implements IExposable, which is a fragile thing to rely on. The
/// designation stays a plain vanilla one (so vanilla's Cancel designator clears it for free) and
/// the detail of *which* implants lives here, in the save.
/// </summary>
public class PendingExtraction : IExposable
{
    public int corpseId;
    public List<int> implantLoadIDs = new List<int>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref corpseId, "corpseId", 0);
        Scribe_Collections.Look(ref implantLoadIDs, "implantLoadIDs", LookMode.Value);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            implantLoadIDs ??= new List<int>();
        }
    }
}

/// <summary>
/// The queue of extractions marked on corpses, and the designation that shows them.
///
/// Works like its vanilla neighbours in the corpse gizmo row - Strip, Extract skull: you mark the
/// body, the mark can be cancelled, and whichever doctor is free services it according to work
/// priorities. A drafted pawn is never pulled out of a fight to do it.
/// </summary>
public static class ImplantSalvagePending
{
    public static PendingExtraction For(Corpse corpse)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        if (corpse == null || component == null)
        {
            return null;
        }

        List<PendingExtraction> all = component.pendingExtractions;
        PendingExtraction found = null;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].corpseId == corpse.thingIDNumber)
            {
                found = all[i];
                break;
            }
        }

        if (found == null)
        {
            return null;
        }

        // The designation is the source of truth. Vanilla's Cancel designator removes it without
        // knowing this list exists, so a mark that has gone means nothing is queued any more -
        // prune rather than leave an invisible queue behind.
        if (corpse.Map != null &&
            corpse.Map.designationManager.DesignationOn(corpse, ImplantSalvageDefOf.Luke_ExtractImplantMark) == null)
        {
            all.Remove(found);
            return null;
        }

        return found;
    }

    public static bool IsPending(Corpse corpse, int loadID)
    {
        PendingExtraction pending = For(corpse);
        return pending != null && pending.implantLoadIDs.Contains(loadID);
    }

    /// <summary>
    /// The next queued implant that is still actually in the body, or null. Entries can go stale -
    /// another doctor finished one first, or the part was blown off - so the queue is filtered
    /// against reality rather than trusted.
    /// </summary>
    public static Hediff NextImplant(Corpse corpse)
    {
        PendingExtraction pending = For(corpse);
        if (pending == null)
        {
            return null;
        }

        for (int i = 0; i < pending.implantLoadIDs.Count; i++)
        {
            Hediff implant = ImplantSalvageUtility.FindImplant(corpse, pending.implantLoadIDs[i]);
            if (implant != null)
            {
                return implant;
            }
        }

        return null;
    }

    /// <summary>
    /// Queue or unqueue one implant, keeping the designation in step: it exists exactly while the
    /// corpse has something queued.
    ///
    /// Not synced itself - callers go through ImplantSalvageActions, which is.
    /// </summary>
    public static void Toggle(Corpse corpse, int loadID)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        if (component == null || corpse == null)
        {
            return;
        }

        PendingExtraction pending = For(corpse);
        if (pending == null)
        {
            pending = new PendingExtraction { corpseId = corpse.thingIDNumber };
            component.pendingExtractions.Add(pending);
        }

        if (!pending.implantLoadIDs.Remove(loadID))
        {
            pending.implantLoadIDs.Add(loadID);
        }

        SyncDesignation(corpse, pending);
    }

    /// <summary>Clear everything queued on a corpse, removing its designation.</summary>
    public static void CancelAll(Corpse corpse)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;
        if (component == null || corpse == null)
        {
            return;
        }

        PendingExtraction pending = For(corpse);
        if (pending != null)
        {
            pending.implantLoadIDs.Clear();
            SyncDesignation(corpse, pending);
        }
    }

    /// <summary>
    /// Called from the JobDriver once an implant is out. Runs inside the simulation tick on every
    /// client, so it must not be synced - syncing it would apply the change twice.
    /// </summary>
    public static void NotifyExtracted(Corpse corpse, int loadID)
    {
        PendingExtraction pending = For(corpse);
        if (pending == null)
        {
            return;
        }

        pending.implantLoadIDs.Remove(loadID);
        SyncDesignation(corpse, pending);
    }

    private static void SyncDesignation(Corpse corpse, PendingExtraction pending)
    {
        GameComponent_ImplantSalvage component = GameComponent_ImplantSalvage.Current;

        bool wanted = pending.implantLoadIDs.Count > 0;

        if (!wanted)
        {
            component?.pendingExtractions.Remove(pending);
        }

        if (corpse.Map == null)
        {
            return;
        }

        DesignationManager designations = corpse.Map.designationManager;
        Designation existing = designations.DesignationOn(corpse, ImplantSalvageDefOf.Luke_ExtractImplantMark);

        if (wanted && existing == null)
        {
            // AddDesignation also clears Forbidden on the target, which is exactly right here:
            // most raid casualties lie forbidden, and marking one is an explicit instruction to
            // go and work on it.
            designations.AddDesignation(new Designation(corpse, ImplantSalvageDefOf.Luke_ExtractImplantMark));
        }
        else if (!wanted && existing != null)
        {
            designations.RemoveDesignation(existing);
        }
    }
}
