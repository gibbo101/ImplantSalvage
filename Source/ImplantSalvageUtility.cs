using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ImplantSalvage;

/// <summary>
/// Shared core: which implants a corpse holds, how likely a given surgeon is to wreck one, and
/// the extraction itself. Deliberately the single place both slice 1 (the right-click job) and a
/// future slice 2 (corpse Operations tab) call into.
/// </summary>
public static class ImplantSalvageUtility
{
    private const int MinWorkTicks = 600;
    private const int MaxWorkTicks = 2000;

    /// <summary>
    /// Implants that can be pulled out of a corpse.
    ///
    /// The filter is exactly <c>HediffDef.spawnThingOnRemoved != null</c> - the same field vanilla
    /// uses to decide what an installed implant drops when removed surgically. It is deliberately
    /// race-agnostic:
    ///   - mechanoids and vanilla animals carry no such hediff, so they silently yield nothing;
    ///   - ghouls yield exactly the three Anomaly hearts (adrenal, corrosive, metalblood) - ghoul
    ///     plating and barbs have no spawnThingOnRemoved because they are grafted, and vanilla
    ///     intends them to be unrecoverable;
    ///   - modded prosthetics, including animal and alien-race ones, work for free.
    /// Natural organs use BodyPartDef.spawnThingOnRemoved instead and are intentionally NOT
    /// touched here - this mod is implants only.
    ///
    /// Iterates the hediff List in order, so it is safe to drive game state from (no unordered
    /// collection walk, which would desync in Multiplayer).
    /// </summary>
    public static IEnumerable<Hediff> ExtractableImplants(Corpse corpse)
    {
        Pawn inner = corpse?.InnerPawn;
        if (inner?.health?.hediffSet == null)
        {
            yield break;
        }

        List<Hediff> hediffs = inner.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff.def.spawnThingOnRemoved != null && hediff.Part != null && hediff.Visible)
            {
                yield return hediff;
            }
        }
    }

    /// <summary>
    /// Re-find an implant by its scribed <see cref="Hediff.loadID"/>. The job carries the loadID
    /// rather than a reference because Multiplayer serialises the Job before the JobDriver exists,
    /// so the data has to live on the Job itself (as job.count).
    /// Returns null if it has gone - another doctor may have got there first.
    /// </summary>
    public static Hediff FindImplant(Corpse corpse, int loadID)
    {
        foreach (Hediff hediff in ExtractableImplants(corpse))
        {
            if (hediff.loadID == loadID)
            {
                return hediff;
            }
        }

        return null;
    }

    /// <summary>
    /// Chance the implant is wrecked, driven entirely by the surgeon. Reusing vanilla's
    /// MedicalSurgerySuccessChance means Medicine skill, traits, sight and manipulation all fold
    /// in for free. With the default settings that runs roughly 45% / 20% / 5% / 2% at Medicine
    /// 0 / 5 / 10 / 15+.
    ///
    /// Note what is deliberately absent: the corpse's rot stage. Corpse condition is a timer
    /// (rot damage and deterioration eventually destroy the body and everything in it), never a
    /// modifier on the roll. The only thing that changes the outcome is who you send.
    /// </summary>
    public static float DestroyChanceFor(Pawn surgeon)
    {
        ImplantSalvageSettings settings = ImplantSalvageMod.Settings;
        float stat = surgeon.GetStatValue(StatDefOf.MedicalSurgerySuccessChance);
        float chance = settings.maxDestroyChance * (1f - Mathf.Clamp01(stat));
        return Mathf.Clamp(chance, settings.minDestroyChance, settings.maxDestroyChance);
    }

    /// <summary>
    /// "Bionic arm (left arm)". The part matters: Hediff.Label alone returns "bionic arm" for both
    /// of a pawn's bionic arms, which makes the menu impossible to choose from. Vanilla's
    /// Bill_Medical.Label disambiguates the same way.
    /// </summary>
    public static string ImplantLabel(Hediff hediff)
    {
        return $"{hediff.LabelCap} ({hediff.Part.Label})";
    }

    /// <summary>Pricier implants are fiddlier to get out. Clamped so it never becomes a slog.</summary>
    public static int WorkTicksFor(Hediff hediff)
    {
        float marketValue = hediff.def.spawnThingOnRemoved?.BaseMarketValue ?? 0f;
        return Mathf.RoundToInt(Mathf.Clamp(MinWorkTicks + marketValue * 0.35f, MinWorkTicks, MaxWorkTicks));
    }

    /// <summary>
    /// Why <paramref name="surgeon"/> cannot be ordered to extract, or null if they can.
    /// Returned as a reason string so the float menu can show a greyed-out option that explains
    /// itself, which is the vanilla idiom - far better than a popup after the click.
    /// </summary>
    public static string CannotExtractReason(Pawn surgeon, Corpse corpse)
    {
        if (surgeon.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
        {
            return "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Doctor.gerundLabel);
        }

        if (surgeon.workSettings == null || !surgeon.workSettings.WorkIsActive(WorkTypeDefOf.Doctor))
        {
            return "CannotPrioritizeNotAssignedToWorkType".CanTranslate()
                ? (string)"CannotPrioritizeNotAssignedToWorkType".Translate(WorkTypeDefOf.Doctor.gerundLabel)
                : (string)"CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Doctor.gerundLabel);
        }

        if (!surgeon.CanReserve(corpse))
        {
            return "Reserved".Translate();
        }

        if (!surgeon.CanReach(corpse, PathEndMode.Touch, Danger.Deadly))
        {
            return "NoPath".Translate();
        }

        return null;
    }

    /// <summary>
    /// Apply the extraction. Called from the JobDriver's finish action - i.e. inside the simulation
    /// tick, on every Multiplayer client - never from UI code.
    ///
    /// Success spawns the implant; failure destroys it. Either way the hediff is replaced with a
    /// missing part, so the corpse visibly loses it and cannot be extracted twice.
    ///
    /// No thoughts, no HistoryEvents, no Ideology interaction: this is scavenging hardware, not
    /// organ harvesting.
    /// </summary>
    public static void Extract(Pawn surgeon, Corpse corpse, Hediff implant)
    {
        Pawn inner = corpse.InnerPawn;
        BodyPartRecord part = implant.Part;
        ThingDef product = implant.def.spawnThingOnRemoved;
        string implantLabel = ImplantLabel(implant);

        // Seeded so the outcome survives a save/reload unchanged (no save-scumming) and stays
        // identical across Multiplayer clients. Verse.Rand only - never UnityEngine.Random.
        bool destroyed;
        Rand.PushState(Gen.HashCombineInt(corpse.thingIDNumber, implant.loadID));
        try
        {
            destroyed = Rand.Chance(DestroyChanceFor(surgeon));
        }
        finally
        {
            Rand.PopState();
        }

        inner.health.RemoveHediff(implant);
        if (!inner.health.hediffSet.PartIsMissing(part))
        {
            inner.health.AddHediff(HediffDefOf.MissingBodyPart, part);
        }

        if (destroyed)
        {
            Messages.Message(
                "Luke_ExtractImplantFailed".Translate(surgeon.LabelShort, implantLabel, corpse.Label),
                surgeon,
                MessageTypeDefOf.NegativeEvent);
        }
        else if (product != null)
        {
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(product), surgeon.Position, surgeon.Map, ThingPlaceMode.Near);
        }

        surgeon.skills?.Learn(SkillDefOf.Medicine, 100f);
        corpse.InnerPawn.Drawer?.renderer?.SetAllGraphicsDirty();
    }
}
