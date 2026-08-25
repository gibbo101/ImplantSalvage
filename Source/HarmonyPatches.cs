using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ImplantSalvage;

[StaticConstructorOnStartup]
public static class HarmonyPatches
{
    static HarmonyPatches()
    {
        new Harmony("luke.implantsalvage").PatchAll(Assembly.GetExecutingAssembly());
    }
}

/// <summary>
/// Records which storage the Storage tab is drawing, so the filter tree can tell whose implant
/// selection it is editing. Listing_TreeThingFilter only knows its ThingFilter, and there is no
/// reverse lookup from a filter to its owner - Multiplayer solves the same problem the same way
/// (ThingFilterMarkers wraps this exact method).
///
/// A finalizer clears it rather than a plain postfix, so an exception thrown anywhere inside
/// FillTab cannot leave a stale owner pinned for the next tab that draws.
/// </summary>
[HarmonyPatch(typeof(ITab_Storage), "FillTab")]
public static class ITab_Storage_FillTab_Patch
{
    private static readonly MethodInfo SelParentGetter =
        AccessTools.PropertyGetter(typeof(ITab_Storage), "SelStoreSettingsParent");

    public static void Prefix(ITab_Storage __instance)
    {
        StorageFilterContext.Current = SelParentGetter?.Invoke(__instance, null) as IStoreSettingsParent;
    }

    public static void Finalizer()
    {
        StorageFilterContext.Current = null;
    }
}

/// Injects the implant picker into the filter tree, directly beneath the "allow corpses with /
/// without implants" pair - those two are its parent switch, so it belongs under them.
///
/// Anchoring to DoSpecialFilter rather than to the start or end of DoCategoryChildren is what puts
/// it there: DoCategoryChildren draws special filters, then child categories, then thing defs, and
/// there is no hook between those stages short of a transpiler. Drawing immediately after the last
/// of our two filters lands in exactly the right place without rewriting anyone's IL.
///
/// The flag exists because DoSpecialFilter is also called for a category's inherited parent
/// filters, so the def alone is not enough to know we are inside the Corpses subtree.
/// </summary>
public static class ImplantTreeAnchor
{
    public static bool DrawingCorpsesChildren;
    public static int CorpsesIndentLevel;
}

[HarmonyPatch(typeof(Listing_TreeThingFilter), "DoCategoryChildren")]
public static class Listing_TreeThingFilter_DoCategoryChildren_Patch
{
    public static void Prefix(TreeNode_ThingCategory node, int indentLevel)
    {
        ImplantTreeAnchor.DrawingCorpsesChildren = node?.catDef == ThingCategoryDefOf.Corpses;
        ImplantTreeAnchor.CorpsesIndentLevel = indentLevel;
    }

    public static void Finalizer()
    {
        ImplantTreeAnchor.DrawingCorpsesChildren = false;
    }
}

[HarmonyPatch(typeof(Listing_TreeThingFilter), "DoSpecialFilter")]
public static class Listing_TreeThingFilter_DoSpecialFilter_Patch
{
    public static void Postfix(Listing_TreeThingFilter __instance, SpecialThingFilterDef sfDef)
    {
        if (!ImplantTreeAnchor.DrawingCorpsesChildren
            || sfDef != ImplantSalvageDefOf.Luke_AllowCorpsesNoImplants
            || StorageFilterContext.Current == null)
        {
            return;
        }

        ImplantTreeInStorage.Draw(__instance, ImplantTreeAnchor.CorpsesIndentLevel);
    }
}

/// <summary>
/// Applies a stockpile's implant selection to corpses.
///
/// The rule, stated once so it cannot drift: a corpse carrying implants is accepted if AT LEAST
/// ONE of them is allowed here. A body with a denied peg leg and an allowed bionic arm is still
/// worth fetching, because the arm is reason enough.
///
/// A corpse with no implants at all is left entirely alone - it is governed by the existing
/// "allow corpses without implants" filter, and must not be swept up by an implant selection that
/// has nothing to say about it.
/// </summary>
[HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.AllowedToAccept), typeof(Thing))]
public static class StorageSettings_AllowedToAccept_Patch
{
    public static void Postfix(StorageSettings __instance, Thing t, ref bool __result)
    {
        // Only ever narrows an acceptance vanilla already granted.
        if (!__result || t is not Corpse corpse)
        {
            return;
        }

        List<string> allowed = ImplantSalvageStorage.AllowedFor(__instance.owner);
        if (allowed == null)
        {
            return;
        }

        if (!ImplantSalvageUtility.HasExtractableImplant(corpse))
        {
            return;
        }

        __result = ImplantSalvageStorage.AnyImplantAllowed(corpse, allowed);
    }
}

/// <summary>
/// Puts "Extract implant" in a corpse's gizmo row, alongside Strip and Allow.
///
/// Postfixing an iterator method: the original's IEnumerable arrives as __result and is re-yielded
/// untouched before ours is appended, so every gizmo vanilla (or another mod) produced still shows,
/// in its original order.
/// </summary>
[HarmonyPatch(typeof(Corpse), nameof(Corpse.GetGizmos))]
public static class Corpse_GetGizmos_Patch
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Corpse __instance)
    {
        foreach (Gizmo gizmo in __result)
        {
            yield return gizmo;
        }

        foreach (Gizmo extra in ImplantSalvageGizmo.GizmosFor(__instance))
        {
            yield return extra;
        }
    }
}
