using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// Draws a small marker over every corpse still holding a salvageable implant, so a battlefield
/// can be read at a glance instead of right-clicking each body in turn.
///
/// The marker is the implant's own item icon - an archotech arm reads differently from a joywire,
/// which is the information the player actually wants. Vanilla renders item icons as map overlays
/// the same way (OverlayDrawer.RenderForbiddenRefuelOverlay draws the chemfuel icon), so this is
/// a native idiom rather than a hack.
///
/// No Harmony: Map.FillComponents() instantiates every non-abstract MapComponent subclass it can
/// find, so this class registers itself - the same trick the float-menu provider uses.
///
/// Purely visual. Nothing here touches game state, which is also why the refresh counter below may
/// safely be a frame count rather than a tick count: no simulation, so nothing to keep in sync.
/// </summary>
public class MapComponent_ImplantMarkers : MapComponent
{
    /// <summary>
    /// How often the corpse list is rebuilt, in rendered frames. Walking every corpse's hediffs
    /// once per frame would be waste; a corpse gaining or losing an implant is rare and half a
    /// second of staleness is invisible. Frames rather than ticks so markers still appear the
    /// moment the setting is toggled while the game is paused.
    /// </summary>
    private const int RefreshIntervalFrames = 30;

    /// <summary>
    /// Tucked into the corner of the cell. The forbidden X owns the centre (a full-size plane at
    /// the corpse's DrawPos), so an offset marker sits beside it rather than on top of it.
    /// </summary>
    private const float MarkerOffset = 0.28f;

    /// <summary>Above the forbidden X and the question mark, using vanilla's own altitude step.</summary>
    private static readonly float MarkerAltitude =
        AltitudeLayer.MetaOverlays.AltitudeFor() + 0.03658537f * 7f;

    private readonly List<Corpse> markedCorpses = new List<Corpse>();
    private readonly List<ThingDef> markedProducts = new List<ThingDef>();

    private int framesSinceRebuild = int.MaxValue;

    public MapComponent_ImplantMarkers(Map map) : base(map)
    {
    }

    public override void MapComponentUpdate()
    {
        if (!ImplantSalvageMod.Settings.showCorpseMarker)
        {
            return;
        }

        // Same guard vanilla puts on its own map drawing (Map.MapUpdate): only the map on screen,
        // and not while the planet view is up.
        if (Find.CurrentMap != map || !WorldRendererUtility.DrawingMap)
        {
            return;
        }

        if (framesSinceRebuild >= RefreshIntervalFrames)
        {
            RebuildCache();
            framesSinceRebuild = 0;
        }

        framesSinceRebuild++;

        for (int i = 0; i < markedCorpses.Count; i++)
        {
            DrawMarker(markedCorpses[i], markedProducts[i]);
        }
    }

    private void RebuildCache()
    {
        markedCorpses.Clear();
        markedProducts.Clear();

        List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
        for (int i = 0; i < corpses.Count; i++)
        {
            if (corpses[i] is not Corpse corpse || !corpse.Spawned)
            {
                continue;
            }

            // Never mark a corpse the player cannot see - a marker glowing through fog of war
            // would hand out free information about ground they have not explored.
            if (corpse.Fogged())
            {
                continue;
            }

            ThingDef product = ImplantSalvageUtility.BestSalvageProduct(corpse);
            if (product?.uiIcon != null)
            {
                markedCorpses.Add(corpse);
                markedProducts.Add(product);
            }
        }
    }

    private static void DrawMarker(Corpse corpse, ThingDef product)
    {
        // The cache is up to RefreshIntervalFrames stale, so the corpse may have been butchered,
        // hauled into a grave or burned since it was built.
        if (!corpse.Spawned)
        {
            return;
        }

        Vector3 pos = corpse.DrawPos;
        pos.x += MarkerOffset;
        pos.z += MarkerOffset;
        pos.y = MarkerAltitude;

        Material material = MaterialPool.MatFrom(product.uiIcon, ShaderDatabase.MetaOverlay, product.uiIconColor);
        Graphics.DrawMesh(MeshPool.plane05, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one), material, 0);
    }
}
