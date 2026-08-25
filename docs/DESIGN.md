# Implant Salvage — Design Doc

**Status:** built. Slice 1 shipped 2026-08-19; the 2026-08-25 round added corpse markers, a
per-stockpile implant picker inside the storage filter tree, and the designation-based gizmo that
**superseded slice 2** (see §8/§8b). The mod now depends on Harmony and MultiplayerAPI.
**Decided:** implants only (no natural organs) · failure destroys the implant · **one named
implant per order, never an "extract all"**, no queueing in v1 · no mood/ideo penalty · distinct
from vanilla Strip · no rot penalty · two slices — right-click job first, corpse Operations tab
second

**Guiding principle:** extraction is installation run backwards. Where the design is ambiguous,
ask *"how does installing this implant work?"* and match it.
**Target:** RimWorld 1.6 · net48 · C# 12
**Working name:** Implant Salvage (`Luke.ImplantSalvage`, defName prefix `Luke_`)
**Multiplayer:** MP-safe by construction (see §6) — a hard requirement, not a nice-to-have.

---

## 1. The pitch

Right-click a corpse with a colonist selected → **"Extract bionic leg (from Corpse of Kira)"**.
The colonist walks over, spends a while working on the body, and the implant pops out as an
item on the ground. Only a pawn who actually does Doctor work can be ordered to do it, and
there's a skill-scaled chance the implant is wrecked in the process.

**The gap it fills:** in vanilla 1.6 a corpse's installed bionics are simply *gone*. Butchering
a humanlike yields meat + leather and nothing else — `Pawn.ButcherProducts` (`Verse/Pawn.cs:2977`)
hits `if (RaceProps.Humanlike) yield break;` before any body-part salvage. A raider with an
archotech arm and a bionic heart drops a corpse worth nothing. The parts only survive if you
capture him alive and run surgery bills.

---

## 2. Key decompile findings (verified, 1.6)

These shape the whole design.

### 2.1 Ludeon already wired corpse surgery — then closed the UI door

The *entire* backend for medical bills on corpses exists and is live:

| Piece | Location | State |
|---|---|---|
| `Corpse : IBillGiver` with a scribed `operationsBillStack` | `Verse/Corpse.cs:10,18,139,165,378` | present |
| `Corpse.CurrentlyUsableForBills()` / `IngredientStackCells` | `Verse/Corpse.cs:132,160` | present |
| `Bill_Medical.GiverPawn` resolves `billGiver is Corpse` → `corpse.InnerPawn` | `RimWorld/Bill_Medical.cs:77` | present |
| `WorkGiver_DoBill.ThingIsUsableBillGiver` accepts corpses | `RimWorld/WorkGiver_DoBill.cs:365,392` | present |
| `DoBillsMedicalHumanOperation` has `billGiversAllHumanlikesCorpses = true` | `Core/Defs/WorkGiverDefs/WorkGivers.xml:119` | **enabled in vanilla XML** |
| `ITab_Pawn_Health` resolves a corpse's `PawnForHealth` | `RimWorld/ITab_Pawn_Health.cs:18` | present |

The only thing stopping you is a single deliberate gate:

```csharp
// ITab_Pawn_Health.ShouldAllowOperations()
if (pawn.Dead) return false;

// HealthCardUtility.DrawPawnHealthCard()
if (pawn.Dead && allowOperations) {
    Log.Error("Called DrawPawnHealthCard with a dead pawn and allowOperations=true. " +
              "Operations are disallowed on corpses.");
```

So the Doctor work pipeline would already operate on a corpse if a bill ever landed on its
stack — and the recipe list corpses carry is a curated one-entry list, not the full surgery
catalogue (§8). **Slice 1** ships the purpose-built right-click job (zero patches); **slice 2**
unlocks this tab for queued/automated work.

### 2.2 Float menu options need **zero Harmony**

1.5 refactored right-click menus into `FloatMenuOptionProvider` subclasses, and
`FloatMenuMakerMap.Init()` (`RimWorld/FloatMenuMakerMap.cs:20-23`) builds its provider list from
`typeof(FloatMenuOptionProvider).AllSubclassesNonAbstract()`. **A mod subclass auto-registers.**
`FloatMenuOptionProvider_HandleCorpse` is the model to copy — it already targets `Corpse` via
`GetOptionsFor(Thing clickedThing, FloatMenuContext context)`.

Net result: **v1 ships with no Harmony patches at all.** Reference `Lib.Harmony` only if a later
slice needs it.

### 2.3 "Is it an implant?" is a one-field test

- `HediffDef.spawnThingOnRemoved` (`Verse/HediffDef.cs:38`) → **installed implants** (bionics,
  prosthetics, joywires, painstoppers, archotech). This is exactly our filter.
- `BodyPartDef.spawnThingOnRemoved` (`Verse/BodyPartDef.cs:38`) → **natural organs**. Deliberately
  *not* used in v1 (see §7 Q1).

`Recipe_RemoveImplant.ApplyOnPawn` (`RimWorld/Recipe_RemoveImplant.cs:39-43`) is the reference
implementation for spawn-then-remove.

### 2.4 `Hediff.loadID` is a stable, scribed int

`Verse/Hediff.cs:30,400` — `public int loadID`, scribed. This is how the job identifies *which*
implant to pull without needing a custom `Job` subclass (critical for MP, §6).

### 2.5 Concrete skill curve

`MedicalSurgerySuccessChance` (`Core/Defs/Stats/Stats_Pawns_WorkMedical.xml:110`) —
`SkillNeed_Direct` on Medicine, plus capacity factors (Sight ×0.4 capped at 1, Manipulation ×1):

| Medicine skill | 0 | 5 | 8 | 10 | 15 | 20 |
|---|---|---|---|---|---|---|
| stat value | 0.10 | 0.60 | 0.80 | 0.90 | 1.00 | 1.10 |

Reusing this stat means traits, injuries, blindness and bad backs all fold in for free.

---

## 3. UX

**Trigger:** colonist selected (undrafted) → right-click a spawned corpse.

**Every order names exactly one implant. There is no "extract all" / "salvage everything" option,
ever** — not as a convenience, not as a setting. One option → one implant → one job.

This is deliberately symmetrical with vanilla **installation**: there is no "install all available
implants" bill either. Each install is one recipe, targeting one `BodyPartRecord`, placed by hand.
Extraction is the same operation run backwards, so it gets the same granularity. Anywhere this
design is ambiguous, resolve it by asking *"how does installing this implant work?"*

**Option shape**
- 0 extractable implants → no option at all (menu stays clean).
- 1 implant → direct option, naming it: `Extract bionic arm (left arm) — 85% intact`.
- 2+ implants → parent option `Extract implant...` opening a sub-menu with **one row per
  implant**, each naming the implant, the part it sits in, and its intact chance.

**Labels must disambiguate by body part.** `Hediff.Label` (`Verse/Hediff.cs:59`) returns
"bionic arm" for *both* of a pawn's bionic arms — useless for picking. Append the part, exactly
as vanilla's `Bill_Medical.Label` does (`RimWorld/Bill_Medical.cs`):

```csharp
$"{hediff.LabelCap} ({hediff.Part.Label})"     // "Bionic arm (left arm)"
```

`BodyPartRecord.Label` (`Verse/BodyPartRecord.cs:51`) honours `customLabel`, so modded and
oddly-named parts read correctly too.

**One at a time in v1 — no queueing.** The design anchor is vanilla *installation*: you cannot say
"install all available implants", you place one bill per implant per part. Extraction mirrors that
exactly — one order, one implant, one part, and the menu closes. Three implants means three
deliberate right-clicks.

Shift-queueing (`KeyBindingDefOf.QueueOrder.IsDownEvent` → `TryTakeOrderedJob(job, tag,
requestQueueing: queueOrder)`, as in `FloatMenuOptionProvider_DressOtherPawn.cs:39,53`) is
**deferred out of v1**. It would still be one-implant-per-job and MP already carries the queue-key
state via `SyncContext.QueueOrder_Down` — so it stays cheap to add later if the repetition
actually grates in play. Not before.

**This is not Strip.** Vanilla's `FloatMenuOptionProvider_Strip` / `JobDefOf.Strip` handles
apparel and equipment only (`Corpse.AnythingToStrip()` → `InnerPawn.AnythingToStrip()`) — no
skill check, no Doctor requirement, no risk. Implant Salvage ships a **separate provider, separate
JobDef, separate work type**, and must not extend, nest under, or reuse the Strip option. A corpse
can be stripped and extracted independently, in either order.

**Wrong-pawn handling** — the ask was "error message if the wrong pawn is selected". The vanilla
idiom for this is a **greyed-out option carrying its own reason**, not a popup; it is discoverable
and does not make the player guess. Use vanilla translation keys:

| Case | Test | Disabled reason |
|---|---|---|
| Incapable of Doctor work | `pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor)` | `CannotPrioritizeWorkTypeDisabled` |
| Doctor priority set to 0 | `!pawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor)` | `CannotPrioritizeNotAssignedToWorkType` |
| No manipulation | provider's `RequiresManipulation => true` | (option hidden by base class) |
| Corpse unreachable | `pawn.CanReach(corpse, PathEndMode.Touch, Danger.Deadly)` | `NoPath` |

**Feedback on completion** — a `Messages.Message` on failure ("Kael destroyed the bionic arm while
extracting it") pointing at the surgeon, mirroring vanilla surgery-failure messaging. Success is
self-evident from the item appearing.

---

## 4. Mechanics

**Where:** in place, on the ground, wherever the corpse lies. No hauling to a medical bed, no
operating table, no medicine consumed — the patient is dead, there is no infection risk to manage.
This is the deliberate simplification that makes it feel like *salvage* rather than *surgery*.

**Work amount:** scaled by implant value, ~`600` ticks base for a simple prosthetic up to
~`2000` for archotech, modified by the surgeon's `WorkSpeedGlobal`. Interruptible/resumable.

**Outcome roll:**

```
stat          = surgeon.GetStatValue(StatDefOf.MedicalSurgerySuccessChance)   // §2.5
destroyChance = Clamp(maxDestroyChance * (1 - Clamp01(stat)), minDestroyChance, maxDestroyChance)
```

With `maxDestroyChance = 0.50`, `minDestroyChance = 0.02` (both mod settings):

| Medicine | 0 | 5 | 10 | 15 | 20 |
|---|---|---|---|---|---|
| destroy chance | 45% | 20% | 5% | 2% (floor) | 2% (floor) |

The floor preserves vanilla's "no matter how high this stat is, there is always a small chance of
failure" flavour.

**On success:** spawn `hediff.def.spawnThingOnRemoved` at the surgeon's cell; remove the hediff;
add `HediffDefOf.MissingBodyPart` for that `BodyPartRecord` so the corpse visibly loses the part
and cannot be double-extracted. Grant Medicine XP (small — it is a corpse, not a patient).

**On failure:** remove the hediff and add `MissingBodyPart` as above, but spawn nothing. The
implant is destroyed. **Decided** — the "spawn it damaged instead" alternative is a fake
consequence: `Recipe_InstallArtificialBodyPart.ApplyOnPawn` ends with
`pawn.health.AddHediff(recipe.addsHediff, part)`, building the installed hediff purely from the
RecipeDef. The ingredient item's `HitPoints` are never read. A bionic arm at 5% HP therefore
installs to exactly the same hediff as one at 100% — "damaged" would cost the player sale value
and nothing else. Destroy is what makes the choice of surgeon matter.

**Rot: no effect on the roll, at any stage. Decided — the corpse's condition is a timer, never a
modifier.** `RotStage` is not read anywhere in the outcome calculation. Rationale:

- **The timer already exists and is better.** `ThingDefGenerator_Corpses` gives every flesh corpse
  `daysToRotStart = 2.5`, `daysToDessicated = 5`, `rotDamagePerDay = 2f`,
  `dessicatedDamagePerDay = 0.7f`, plus `DeteriorationRate = 1f`. A neglected corpse takes real HP
  damage and is eventually destroyed — and every implant in it dies with it. That is already a
  legible "get to it before it is gone" deadline. A hidden success-chance modifier would be a
  second, fuzzier copy of a deadline the game already enforces.
- **A rot penalty is defeated by a freezer.** Cold storage stops rot dead and is standard colony
  practice, so the penalty would bite only colonies too early or too besieged to have one — a tax
  on players who *cannot* respond, ignorable by everyone who can. That is the opposite of a
  decision point.
- **It is thematically backwards.** Metal does not rot; a desiccated corpse arguably has *less*
  tissue in the way.
- **It breaks the guiding principle.** Installation does not scale with the patient's condition,
  only with the surgeon, the medicine and the room. Extraction run backwards scales with the
  surgeon — which `MedicalSurgerySuccessChance` already covers.

Extraction is therefore allowed at every rot stage, unmodified, right up until the corpse is gone.

---

## 5. Implementation sketch

No Harmony. Four source files plus XML.

```
ImplantSalvage/
  About/About.xml                      # supportedVersions 1.6, no hard deps
  Defs/JobDefs_ImplantSalvage.xml      # Luke_ExtractImplant
  Languages/English/Keyed/*.xml
  Source/
    ImplantSalvageMod.cs               # Mod + ModSettings (chances, toggles)
    ImplantSalvageUtility.cs           # ExtractableImplants(Corpse), DestroyChanceFor(...)
    FloatMenuOptionProvider_ExtractImplant.cs
    JobDriver_ExtractImplant.cs
```

**`ImplantSalvageUtility.ExtractableImplants(Corpse corpse)`**
Iterate `corpse.InnerPawn.health.hediffSet.hediffs` (a `List<Hediff>` — ordered, MP-safe) and
yield those where `h.def.spawnThingOnRemoved != null && h.Part != null && h.Visible`.

**`FloatMenuOptionProvider_ExtractImplant : FloatMenuOptionProvider`**

```
Drafted              => false
Undrafted            => true
Multiselect          => false
RequiresManipulation => true
GetOptionsFor(Thing clickedThing, FloatMenuContext context)   // clickedThing as Corpse
```

The option's action does **exactly one thing**:

```csharp
var job = JobMaker.MakeJob(Luke_JobDefOf.Luke_ExtractImplant, corpse);
job.count = hediff.loadID;                 // which implant — scribed int, MP-serialisable
context.FirstSelectedPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
```

**`JobDriver_ExtractImplant : JobDriver`**
`TargetA` = corpse. Reserve it, `GotoThing(TargetIndex.A, PathEndMode.Touch)`, then a wait toil
with `WithProgressBar` + `WithEffect` and `defaultCompleteMode = ToilCompleteMode.Delay`. In the
toil's `FinishAction`, re-find the hediff by `loadID` (it may be gone if a second doctor beat you
to it — fail gracefully), roll, apply. `AddFinishAction` for reservation cleanup.

**Why `job.count` and not a JobDriver field:** MP serialises the *Job* over the wire
(`SyncMethod.Register(Pawn_JobTracker.TryTakeOrderedJob).ExposeParameter(0)`), and the JobDriver
does not exist yet at that point. The data must live on the `Job`. `job.count` is an int that
scribes and syncs for free.

---

## 6. Multiplayer safety

Verified against the local Multiplayer clone:

- **The order syncs for free.** `Source/Client/Syncing/Game/SyncMethods.cs:41` registers
  `Pawn_JobTracker.TryTakeOrderedJob` with `SyncContext.QueueOrder_Down` and
  `ExposeParameter(0)`. A float-menu option whose action is *only* `TryTakeOrderedJob` needs
  **no MP-compat patch**. This is the single most important design constraint.
- **Rule: the float-menu delegate issues the job and nothing else.** No spawning, no hediff
  removal, no `Rand` in the delegate — that code runs on the clicking client only. Every effect
  happens inside the JobDriver, which runs on all clients in the synced tick.
- **Never consume `Rand` in UI code.** The displayed "% intact" is a pure function of the
  surgeon's stat — computing it must not advance the RNG stream, or opening a menu desyncs the sim.
- **Roll with `Verse.Rand.Chance` inside the toil.** Never `UnityEngine.Random`. All clients tick
  the same Rand state, so no explicit seeding is strictly required — but wrapping in
  `Rand.PushState(corpse.thingIDNumber ^ hediff.loadID)` / `Rand.PopState()` is cheap insurance
  and makes the outcome reproducible on save-reload. Recommended.
- **No unordered iteration drives state.** `hediffSet.hediffs` is a `List<T>`; never walk a
  `Dictionary`/`HashSet` to decide game state.
- **No wall-clock.** No `DateTime.Now`, no `Stopwatch`.
- Provider registration order via `AllSubclassesNonAbstract()` affects *menu row order only* — UI,
  not simulation. Harmless.

Post-build check: run a 2-client MP session, both clients extract from different corpses
simultaneously, watch the MP log for desync.

---

## 7. Open questions

**Resolved**

1. ~~**Organs too, or implants only?**~~ **Implants only.** Filter stays
   `HediffDef.spawnThingOnRemoved != null`. Natural organs (`BodyPartDef.spawnThingOnRemoved`)
   are out of scope — they drag in the organ-harvesting mood/Ideology/faction-goodwill apparatus.
   Note this also rules out vanilla's `RecipeDefOf.RemoveBodyPart` as our recipe worker, since
   `Recipe_RemoveBodyPart.ApplyOnPawn` calls `MedicalRecipesUtility.SpawnNaturalPartIfClean`
   alongside `SpawnThingsFromHediffs` (§8, Option B).
3. ~~**Failure = destroyed, or damaged?**~~ **Destroyed.** See §4 — item `HitPoints` never reach
   the installed hediff, so a damaged implant is functionally identical once installed. "Damaged"
   would be a sale-value penalty masquerading as a consequence.
4. ~~**Mood/ideo consequences?**~~ **None.** No thoughts, no `HistoryEvent`, no Ideology
   precept interaction, not even behind a setting. This is scavenging hardware, not organ
   harvesting — the implant was manufactured, not grown, and nobody mourns a joywire. Concretely:
   do **not** call `ThoughtUtility.GiveThoughtsForPawnOrganHarvested` /
   `GiveThoughtsForPawnExecuted` (which `Recipe_RemoveBodyPart.ApplyThoughts` does), and do not
   record `HistoryEventDefOf` entries. This is another reason our own recipe worker replaces
   vanilla `RemoveBodyPart` in slice 2 (§8).
5. ~~**Automation?**~~ Superseded by §8 Option B — promoted from rejected alternative to planned
   slice 2. No custom `WorkGiver_Scanner` or designation needed; the existing
   `DoBillsMedicalHumanOperation` WorkGiver already covers corpses.
6. ~~**Queueing multiple extractions?**~~ **Deferred out of v1** (§3). One order at a time,
   mirroring installation. Revisit only if the repetition actually grates in play.

7. ~~**Which races?**~~ **No race gate — let the filter decide.** See §7b; do *not* test
   `RaceProps.Humanlike`.

8. ~~**Rot penalty?**~~ **No.** Corpse condition is a *timer*, never a *modifier* — see §4.
   The only thing affecting the outcome is who you send.

**Nothing open. Design is settled; ready to build.**

---

## 7b. Which corpses actually have implants (verified)

Checked rather than assumed, because Anomaly complicates the obvious answer.

| Race | Has extractable implants? | Notes |
|---|---|---|
| **Humans / xenotypes** | Yes — the main case | All Core + Royalty + Biotech bionics, prosthetics, joywires, archotech |
| **Ghouls** (Anomaly) | Yes, but only **3** | See below |
| **Shamblers** (Anomaly) | Occasionally | Only if raised from a body that already had one |
| **Awoken corpses** (Anomaly) | Same as shamblers | |
| **Mechanoids** | No | No hediffs carry `spawnThingOnRemoved`; `ThingDefGenerator_Corpses` also gives mech corpses no recipes (`if (!pawnDef.race.IsMechanoid)`) |
| **Animals** | Not in vanilla | Vanilla installs no bionics in animals — but mods do, and those work for free |

**Ghouls yield exactly three implants, and vanilla's own data draws the line for us.** Of every
hediff in `Anomaly/Defs/HediffDefs/Hediffs_BodyParts_Prosthetic.xml`, only **three** carry
`spawnThingOnRemoved`: `AdrenalHeart`, `CorrosiveHeart`, `MetalbloodHeart`. `GhoulPlating` and
`GhoulBarbs` deliberately have none — they are grafted, not installed, and Ludeon made them
unrecoverable. Our filter reproduces that intent with no special-casing.

**Therefore: no `RaceProps.Humanlike` gate.** `hediff.def.spawnThingOnRemoved != null` is already
the correct and complete test. It yields nothing for mechs and vanilla animals (so no options
appear, no clutter), yields exactly the right three for ghouls, and picks up modded animal or
alien-race prosthetics for free without us predicting them. Adding a race check would only break
mod compatibility while changing nothing in vanilla.

**One slice-2 caveat.** `ITab_Pawn_Health.ShouldAllowOperations` also bails on
`pawn.IsMutant && !pawn.mutant.Def.entitledToMedicalCare`. In `Anomaly/Defs/Misc/Mutants.xml`,
`Shambler` and `AwokenCorpse` both set `entitledToMedicalCare = false` (`Ghoul` does not). So
shambler corpses will be extractable via the slice-1 float menu but blocked in the slice-2
Operations tab unless we relax that check too. Leave it blocked — the float menu covers the case,
and loosening a second vanilla gate widens the conflict surface for a rare corpse.

---

## 8. Option B — unlock the corpse Operations tab (slice 2) — **ABANDONED, superseded**

**Status: not built, and will not be. Superseded 2026-08-25 by the designation + WorkGiver model
(§8b).** Kept here because the reasoning is worth not re-deriving.

The idea was to unlock `ITab_Pawn_Health` on corpses so extractions could be queued as medical
bills and serviced by the existing `DoBillsMedicalHumanOperation` WorkGiver. It was promoted from
"rejected" once it turned out `ThingDefGenerator_Corpses.cs:155` gives every non-mech corpse a
curated one-entry recipe list, so unlocking the tab would *not* expose the whole surgery catalogue.

**Then a second look killed it.** The tab would render, but the Add-bill click is dead on a corpse.
`HealthCardUtility.GenerateSurgeryOption`'s action opens with:

```csharp
Pawn medPawn = thingForMedBills as Pawn;
if (medPawn != null) { ... CreateSurgeryBill(medPawn, recipe, part); }
```

`thingForMedBills` is the `Corpse`, so `medPawn` is null and the delegate does nothing.
`CreateSurgeryBill` itself takes a `Pawn` and calls `medPawn.BillStack.AddBill` / `.MapHeld`, so it
cannot be reused either. Slice 2 would therefore have needed its own recipe-options maker *and* its
own bill creation — more code and one more patched surface than the original estimate.

Full cost, had we built it: a destructive `return false` prefix on `ITab_Pawn_Health.FillTab`; a
custom `Recipe_ExtractImplant : Recipe_Surgery` worker (vanilla `RemoveBodyPart` drops natural
organs and leaves the hediff intact on failure); recipe registration onto runtime-generated corpse
ThingDefs; and a replacement for the corpse-hostile click path above.

It also had a coverage hole we had agreed to accept: `ShouldAllowOperations` bails on
`pawn.IsMutant && !mutant.Def.entitledToMedicalCare`, so **shambler and awoken corpses would have
been blocked** (§7b).

## 8b. What replaced it — designation + WorkGiver

Everything slice 2 existed for, at a fraction of the cost:

- **"Extract implant" gizmo** on a selected corpse, in the same row as Strip and Allow. It *marks*
  implants rather than ordering a pawn — one row per implant, ticked individually, so the
  one-order-one-implant rule of §3 survives intact.
- A plain vanilla **`Designation`** (`Luke_ExtractImplantMark`) carries the mark. Plain, not a
  subclass, for two reasons: `Designation.ExposeData` is not virtual, and a vanilla designation is
  cleared by vanilla's own **Cancel** designator for free — so there is no second cancel button.
  Which implants are queued lives in `GameComponent_ImplantSalvage`, with the designation treated as
  the source of truth and the queue pruned when it disappears.
- **`WorkGiver_ExtractImplant`** under the Doctor work type services the marks. Because the job
  arrives through the work system rather than as an ordered job, it **never pulls a drafted pawn**
  and respects work priorities — matching skull extraction exactly. `priorityInType` sits below
  tending the living.

**Better than slice 2 on coverage, not just cost:** the mutant gate never applies, so shambler and
awoken corpses work.

**Only thing lost:** bill-stack machinery — repeat counts, suspend, reorder — which is meaningless
for a one-shot extraction.

**The two moments §8 identified are both still served**, just not the way it predicted: the
right-click float menu is "the raid is over, I want that archotech arm *now*" (an explicit order,
allowed on drafted pawns because you picked the pawn yourself), and the gizmo is "queue these six
implants and get to them when a doctor is free".

## 8d. Still rejected

**Option C — patch `Pawn.ButcherProducts` so butchering recovers implants.** Trivial (one
postfix), but it is a different mod: no skill check, no doctor requirement, no player decision.

---

## 9. Prior art to check before building

"Harvest Organs Post Mortem" covers adjacent ground (organs + implants from corpses, via bills).
Worth installing once to see what its UX gets right and where the corpse-surgery route chafes —
and to decide whether Implant Salvage should detect it and stand down, or simply coexist.
