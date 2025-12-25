using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Goobstation.Maths.FixedPoint;
using Content.Server.Administration.Logs;
using Content.Server.Audio;
using Content.Server.Body.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DoAfter;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Hands.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Lightning;
using Content.Server.Nutrition;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Temperature.Systems;
using Content.Server.Toolshed.Commands.Misc;
using Content.Shared._NF.Kitchen.Components;
using Content.Shared.Cargo;
using Content.Shared.Chat;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DoAfter;
using Content.Shared.EntityEffects;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Kitchen;
using Content.Shared.Kitchen.Components;
using Content.Shared.Kitchen.Prototypes;
using Content.Shared.Kitchen.UI;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.NameModifier.EntitySystems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Content.Shared.Throwing;
using Content.Shared.UserInterface;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Kitchen.EntitySystems;

public sealed partial class DeepFryerSystem : SharedDeepFryerSystem
{
    [Dependency] private readonly SharedEntityEffectsSystem _entityEffects = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly RecipeManager _recipeManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly SolutionTransferSystem _solutionTransfer = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedSuicideSystem _suicide = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly NameModifierSystem _nameModifier = default!;
    [Dependency] private readonly AmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;

    private static readonly string CookingDamageType = "Heat";
    private static readonly float CookingDamageAmount = 10.0f;
    private static readonly float PvsWarningRange = 0.5f;
    private static readonly float ThrowMissChance = 0.25f;
    private static readonly int MaximumCrispiness = 2;
    private static readonly float BloodToProteinRatio = 0.1f;
    private static readonly string MobFlavorMeat = "meaty";

    private static readonly AudioParams
        AudioParamsInsertRemove = new(0.5f, 1f, 5f, 1.5f, 1f, false, 0f, 0.2f);

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        //normal machine events
        SubscribeLocalEvent<DeepFryerComponent, ComponentInit>(OnInitDeepFryer);
        SubscribeLocalEvent<DeepFryerComponent, PowerChangedEvent>(OnPowerChange);
        SubscribeLocalEvent<DeepFryerComponent, MachineDeconstructedEvent>(OnDeconstruct);
        SubscribeLocalEvent<DeepFryerComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<DeepFryerComponent, ThrowHitByEvent>(OnThrowHitBy);
        SubscribeLocalEvent<DeepFryerComponent, SolutionChangedEvent>(OnSolutionChange);
        SubscribeLocalEvent<DeepFryerComponent, ContainerRelayMovementEntityEvent>(OnRelayMovement);
        SubscribeLocalEvent<DeepFryerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<DeepFryerComponent, AnchorStateChangedEvent>(OnAnchorChanged);

        //deepfryer specific events
        SubscribeLocalEvent<DeepFryerComponent, BeforeActivatableUIOpenEvent>(OnBeforeActivatableUIOpen);
        SubscribeLocalEvent<DeepFryerComponent, DeepFryerRemoveItemMessage>(OnRemoveItem);
        SubscribeLocalEvent<DeepFryerComponent, DeepFryerInsertItemMessage>(OnInsertItem);
        SubscribeLocalEvent<DeepFryerComponent, DeepFryerScoopVatMessage>(OnScoopVat);
        SubscribeLocalEvent<DeepFryerComponent, DeepFryerClearSlagMessage>(OnClearSlagStart);
        SubscribeLocalEvent<DeepFryerComponent, DeepFryerRemoveAllItemsMessage>(OnRemoveAllItems);
        SubscribeLocalEvent<DeepFryerComponent, ClearSlagDoAfterEvent>(OnClearSlag);

        //deepfried events
        SubscribeLocalEvent<DeepFriedComponent, ComponentInit>(OnInitDeepFried);
        SubscribeLocalEvent<DeepFriedComponent, ExaminedEvent>(OnExamineFried);
        SubscribeLocalEvent<DeepFriedComponent, PriceCalculationEvent>(OnPriceCalculation);
        SubscribeLocalEvent<DeepFriedComponent, FoodSlicedEvent>(OnSliceDeepFried);
    }
    //gets information for the UI
     private void UpdateUserInterface(EntityUid uid, DeepFryerComponent component)
    {
        var state = new DeepFryerBoundUserInterfaceState(
            GetOilLevel(uid, component),
            GetOilPurity(uid, component),
            component.FryingOilThreshold,
            EntityManager.GetNetEntityArray(component.Storage.ContainedEntities.ToArray()));

        _userInterface.SetUiState(uid, DeepFryerUiKey.Key, state);
    }

    private void OnAnchorChanged(EntityUid uid, DeepFryerComponent component, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            _container.EmptyContainer(component.Storage);
    }

    /// <summary>
    ///     Does the deep fryer have hot oil?
    /// </summary>
    /// <remarks>
    ///     This is mainly for audio.
    /// </remarks>
    private bool HasBubblingOil(EntityUid uid, DeepFryerComponent component)
    {
        return _power.IsPowered(uid) && GetOilVolume(uid, component) > FixedPoint2.Zero;
    }

    /// <summary>
    ///     Returns how much total oil is in the vat.
    /// </summary>
    public FixedPoint2 GetOilVolume(EntityUid uid, DeepFryerComponent component)
    {
        var oilVolume = FixedPoint2.Zero;

        foreach (var reagent in component.Solution)
        {
            if (component.FryingOils.Contains(reagent.Reagent.ToString()))
                oilVolume += reagent.Quantity;
        }

        return oilVolume;
    }

    /// <summary>
    ///     Returns how much total waste is in the vat.
    /// </summary>
    public FixedPoint2 GetWasteVolume(EntityUid uid, DeepFryerComponent component)
    {
        var wasteVolume = FixedPoint2.Zero;

        foreach (var reagent in component.WasteReagents)
        {
            wasteVolume += component.Solution.GetReagentQuantity(reagent.Reagent);
        }

        return wasteVolume;
    }

    /// <summary>
    ///     Returns a percentage of how much of the total solution is usable oil.
    /// </summary>
    public FixedPoint2 GetOilPurity(EntityUid uid, DeepFryerComponent component)
    {
        if (component.Solution.Volume > 0) // Frontier: ensure no negative division.
            return GetOilVolume(uid, component) / component.Solution.Volume;
        return FixedPoint2.Zero;
    }

    /// <summary>
    ///     Returns a percentage of how much of the total volume is usable oil.
    /// </summary>
    public FixedPoint2 GetOilLevel(EntityUid uid, DeepFryerComponent component)
    {
        if (component.Solution.MaxVolume > 0) // Frontier: ensure no negative division or division by zero.
            return GetOilVolume(uid, component) / component.Solution.MaxVolume;
        return FixedPoint2.Zero;
    }

    /// <summary>
    ///     This takes care of anything that would happen to an item with or
    ///     without enough oil.
    /// </summary>
    private void CookItem(EntityUid uid, DeepFryerComponent component, EntityUid item)
    {
        if (TryComp<TemperatureComponent>(item, out var tempComp))
        {
            // Push the temperature towards what it should be but no higher.
            var delta = (component.PoweredTemperature - tempComp.CurrentTemperature) * _temperature.GetHeatCapacity(item, tempComp);

            if (delta > 0f)
                _temperature.ChangeHeat(item, delta, false, tempComp);
        }

        if (TryComp<SolutionContainerManagerComponent>(item, out var solutions) && solutions.Solutions != null)
        {
            foreach (var (_, solution) in solutions.Solutions)
            {
                if (_solutionContainer.TryGetSolution(item, solution.Name, out var solutionRef))
                    _solutionContainer.SetTemperature(solutionRef!.Value, component.PoweredTemperature);
            }
        }

        // Damage non-food items and mobs.
        if ((!HasComp<EdibleComponent>(item) || HasComp<MobStateComponent>(item)) &&
            TryComp<DamageableComponent>(item, out var damageableComponent))
        {
            var damage = new DamageSpecifier(_prototype.Index<DamageTypePrototype>(CookingDamageType),
                CookingDamageAmount);

            var result = _damageableSystem.TryChangeDamage(item, damage, origin: uid);
        }
    }

    /// <summary>
    ///     Destroy a food item and replace it with a charred mess.
    /// </summary>
    private void BurnItem(EntityUid uid, DeepFryerComponent component, EntityUid item)
    {
        if (HasComp<EdibleComponent>(item) &&
            !HasComp<MobStateComponent>(item) &&
            MetaData(item).EntityPrototype?.ID != component.CharredPrototype)
        {
            var charred = Spawn(component.CharredPrototype, Transform(uid).Coordinates);
            _container.Insert(charred, component.Storage);
            Del(item);
        }
    }

    private void UpdateDeepFriedName(EntityUid uid, DeepFriedComponent component, CrispinessLevelSetPrototype? crispinessLevels = null) // Frontier: add crispinessLevelSet
    {
        if (component.OriginalName == null)
            return;

        // Frontier: assign crispiness levels to a prototype
        if (crispinessLevels == null && !_prototype.TryIndex<CrispinessLevelSetPrototype>(component.CrispinessLevelSet, out crispinessLevels))
            return;

        if (crispinessLevels.Levels.Count <= 0)
            return;

        int crispiness = int.Max(0, component.Crispiness);
        {
            string name;
            if (crispiness < crispinessLevels.Levels.Count)
                name = crispinessLevels.Levels[crispiness].Name;
            else
                name = crispinessLevels.Levels[^1].Name;
            _metaData.SetEntityName(uid, Loc.GetString(name, ("entity", component.OriginalName)));
        }
        // End Frontier
    }

    /// <summary>
    ///     Try to deep fry a single item, which can
    ///     - be cancelled by other systems, or
    ///     - fail due to the blacklist, or
    ///     - give it a crispy shader, and possibly also
    ///     - turn it into food.
    /// </summary>
    private void DeepFry(EntityUid uid, DeepFryerComponent component, EntityUid item)
    {
        if (MetaData(item).EntityPrototype?.ID == component.CharredPrototype)
            return;

        // Frontier: deep fryer-specific "recipes"
        if (TryComp<DeepFrySpawnComponent>(item, out var deepFriable))
        {
            deepFriable.Cycles--;
            if (deepFriable.Cycles <= 0)
            {
                // Get oil volume to spawn before deleting item.
                var friableVolume = GetOilAndWasteVolumeForItem(uid, component, item);

                var spawn = Spawn(deepFriable.Output, Transform(uid).Coordinates);
                EnsureComp<PreventCrispingComponent>(spawn);
                _container.Insert(spawn, component.Storage);
                Del(item);

                // Reduce volume, replace waste
                component.Solution.RemoveSolution(friableVolume);
                component.WasteToAdd += friableVolume;
            }
            return;
        }
        else if (TryComp<PreventCrispingComponent>(item, out var blacklist))
        {
            blacklist.Cycles += 1;
            if (blacklist.Cycles >= GetMaximumCrispiness(component.CrispinessLevelSet))
            {
                BurnItem(uid, component, item);
            }
            return;
        }
        // End Frontier

        // This item has already been deep-fried, and now it's progressing
        // into another stage.
        if (TryComp<DeepFriedComponent>(item, out var deepFriedComponent))
        {
            // TODO: Smoke, waste, sound, or some indication.

            deepFriedComponent.Crispiness += 1;

            var maxCrispiness = MaximumCrispiness; // Default maximum crispiness (should burn if something goes wrong)
            if (_prototype.TryIndex<CrispinessLevelSetPrototype>(deepFriedComponent.CrispinessLevelSet, out var crispinessLevels))
            {
                maxCrispiness = int.Max(0, crispinessLevels.Levels.Count - 1);
            }
            if (deepFriedComponent.Crispiness > MaximumCrispiness)
            {
                BurnItem(uid, component, item);
                return;
            }

            UpdateDeepFriedName(item, deepFriedComponent, crispinessLevels);
            return;
        }

        // Allow entity systems to conditionally forbid an attempt at deep-frying.
        var attemptEvent = new DeepFryAttemptEvent(uid);
        RaiseLocalEvent(item, attemptEvent);

        if (attemptEvent.Cancelled)
            return;

        // The attempt event is allowed to go first before the blacklist check,
        // just in case the attempt is relevant to any system in the future.
        //
        // The blacklist overrides all.
        if (_whitelist.CheckBoth(item, component.Blacklist))
        {
            _popupSystem.PopupEntity(
                Loc.GetString("deep-fryer-blacklist-item-failed",
                    ("item", item), ("deepFryer", uid)),
                uid,
                Filter.Pvs(uid, PvsWarningRange),
                true);
            return;
        }

        var beingEvent = new BeingDeepFriedEvent(uid, item);
        RaiseLocalEvent(item, beingEvent);

        // It's important to check for the MobStateComponent so we know
        // it's actually a mob, because functions like
        // MobStateSystem.IsAlive will return false if the entity lacks the
        // component.
        if (TryComp<MobStateComponent>(item, out var mobStateComponent))
        {
            if (!TryMakeMobIntoFood(item, mobStateComponent))
                return;
        }

        MakeCrispy(item, component.CrispinessLevelSet);

        var solutionQuantity = GetOilAndWasteVolumeForItem(uid, component, item);

        if (_whitelist.IsWhitelistPass(component.Whitelist, item) ||
            beingEvent.TurnIntoFood)
            MakeEdible(uid, component, item, solutionQuantity);
        else
            component.Solution.RemoveSolution(solutionQuantity);

        component.WasteToAdd += solutionQuantity;
    }

    // Frontier: oil/waste volume to a function.
    private FixedPoint2 GetOilAndWasteVolumeForItem(EntityUid uid, DeepFryerComponent component, EntityUid item)
    {
        var itemComponent = Comp<ItemComponent>(item);

        // Determine how much solution to spend on this item.
        return FixedPoint2.Min(
            component.Solution.Volume,
            itemComponent.Size.Id switch
            {
                "Tiny" => 1,
                "Small" => 5,
                "Medium" => 10,
                "Large" => 15,
                "Huge" => 30,
                "Ginormous" => 50,
                _ => 10
            } * component.SolutionSizeCoefficient);
    }
    // End Frontier

    // Frontier: maximum crispiness
    private int GetMaximumCrispiness(ProtoId<CrispinessLevelSetPrototype> crispinessLevelSet)
    {
        var maxCrispiness = MaximumCrispiness; // Default maximum crispiness (should burn if something goes wrong)
        if (_prototype.TryIndex<CrispinessLevelSetPrototype>(crispinessLevelSet, out var crispinessLevels))
        {
            maxCrispiness = int.Max(0, crispinessLevels.Levels.Count - 1);
        }
        return maxCrispiness;
    }
    // End Frontier

    private void OnInitDeepFryer(EntityUid uid, DeepFryerComponent component, ComponentInit args)
    {
        component.Storage =
            _container.EnsureContainer<Container>(uid, component.StorageName, out var containerExisted);

        if (!containerExisted)
            _sawmill.Warning(
                $"{ToPrettyString(uid)} did not have a {component.StorageName} container. It has been created.");

        if (_solutionContainer.EnsureSolution(uid, component.SolutionName, out var solutionExisted, out var solution))
            component.Solution = solution;

        if (!solutionExisted)
            _sawmill.Warning(
                $"{ToPrettyString(uid)} did not have a {component.SolutionName} solution container. It has been created.");
        foreach (var reagent in component.Solution.Contents.ToArray())
        {
            //JJ Comment - not sure this works. Need to check if Reagent.ToString is correct.
            _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.ToString(), out var proto);


            // Apply each unsafe oil effect to the deep fryer
            foreach (var effect in component.UnsafeOilVolumeEffects)
            {
                // Let the effect system handle conditions and application
                _entityEffects.TryApplyEffect(
                    target: uid,              // The deep fryer itself
                    effect: effect,
                    scale: reagent.Quantity.Float(),  // Use reagent quantity as scale
                    user: null                // No specific user caused this
                );
            }


        }
    }

    /// <summary>
    ///     Make sure the UI and interval tracker are updated anytime something
    ///     is inserted into one of the baskets.
    /// </summary>
    /// <remarks>
    ///     This is used instead of EntInsertedIntoContainerMessage so charred
    ///     items can be inserted into the deep fryer without triggering this
    ///     event.
    /// </remarks>
    private void AfterInsert(EntityUid uid, DeepFryerComponent component, EntityUid item)
    {
        if (HasBubblingOil(uid, component))
            _audio.PlayPvs(component.SoundInsertItem, uid, AudioParamsInsertRemove);

        UpdateNextFryTime(uid, component);
        UpdateUserInterface(uid, component);
    }

    private void OnPowerChange(EntityUid uid, DeepFryerComponent component, ref PowerChangedEvent args)
    {
        _appearance.SetData(uid, DeepFryerVisuals.Bubbling, args.Powered);
        UpdateNextFryTime(uid, component);
        UpdateAmbientSound(uid, component);
    }

    private void OnDeconstruct(EntityUid uid, DeepFryerComponent component, MachineDeconstructedEvent args)
    {
        // The EmptyOnMachineDeconstruct component handles the entity container for us.
        _puddle.TrySpillAt(uid, component.Solution, out var _);
    }

    private void OnDestruction(EntityUid uid, DeepFryerComponent component, DestructionEventArgs args)
    {
        _container.EmptyContainer(component.Storage, true);
    }

    /// <summary>
    ///     Allow thrown items to land in a basket.
    /// </summary>
    private void OnThrowHitBy(EntityUid uid, DeepFryerComponent component, ThrowHitByEvent args)
    {
        if (args.Handled)
            return;

        if (!CanInsertItem(uid, component, args.Thrown) ||
            !_container.Insert(args.Thrown, component.Storage))
        {
            _popupSystem.PopupEntity(
                Loc.GetString("deep-fryer-thrown-missed"),
                uid);

            return;
        }

        if (GetOilVolume(uid, component) < component.SafeOilVolume)
        {
            _popupSystem.PopupEntity(
                Loc.GetString("deep-fryer-thrown-hit-oil-low"),
                uid);
        }
        else
        {
            _popupSystem.PopupEntity(
                Loc.GetString("deep-fryer-thrown-hit-oil"),
                uid);
        }

        AfterInsert(uid, component, args.Thrown);

        args.Handled = true;
    }

    private void OnSolutionChange(EntityUid uid, DeepFryerComponent component, SolutionChangedEvent args)
    {
        UpdateUserInterface(uid, component);
        UpdateAmbientSound(uid, component);
    }

    private void OnRelayMovement(EntityUid uid, DeepFryerComponent component,
        ref ContainerRelayMovementEntityEvent args)
    {

        if (!_container.Remove(args.Entity, component.Storage, destination: Transform(uid).Coordinates))
            return;

        _popupSystem.PopupEntity(
            Loc.GetString("deep-fryer-entity-escape",
                ("victim", Identity.Entity(args.Entity, EntityManager)),
                ("deepFryer", uid)),
            uid,
            PopupType.SmallCaution);
    }

    private void OnBeforeActivatableUIOpen(EntityUid uid, DeepFryerComponent component,
        BeforeActivatableUIOpenEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnRemoveItem(EntityUid uid, DeepFryerComponent component, DeepFryerRemoveItemMessage args)
    {
        var removedItem = EntityManager.GetEntity(args.Item);
        if (removedItem.Valid)
        {
            //JJ Comment - This line should be unnecessary.
            //Some issue is keeping the UI from updating when converting straight to a Burned Mess while the UI is still open.
            //To replicate, put a Raw Meat in the fryer with no oil in it. Wait until it sputters with no effect.
            //It should transform to Burned Mess, but doesn't.
            if (!_container.Remove(removedItem, component.Storage))
                return;

            var user = args.Actor;

            _handsSystem.TryPickupAnyHand(user, removedItem);

            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(user)} took {ToPrettyString(args.Item)} out of {ToPrettyString(uid)}.");

            _audio.PlayPvs(component.SoundRemoveItem, uid, AudioParamsInsertRemove);

            UpdateUserInterface(uid, component);
        }
    }

    /// <summary>
    ///     This is a helper function for ScoopVat and ClearSlag.
    /// </summary>
    private bool TryGetActiveHandSolutionContainer(
        EntityUid fryer,
        EntityUid user,
        [NotNullWhen(true)] out EntityUid? heldItem,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solution,
        out FixedPoint2 transferAmount)
    {
        heldItem = null;
        solution = null;
        transferAmount = FixedPoint2.Zero;

        if (!TryComp<HandsComponent>(user, out var handsComponent))
            return false;

        // heldItem = handsComponent.ActiveHandEntity; // Frontier: reformat to use the hand system

        if (!_handsSystem.TryGetActiveItem(user, out heldItem) || // Frontier: reformat to use the hand system
            !TryComp<SolutionTransferComponent>(heldItem, out var solutionTransferComponent) ||
            !_solutionContainer.TryGetRefillableSolution(heldItem.Value, out var solEnt, out var _) ||
            !solutionTransferComponent.CanReceive)
        {
            _popupSystem.PopupEntity(
                Loc.GetString("deep-fryer-need-liquid-container-in-hand"),
                fryer,
                user);

            return false;
        }

        solution = solEnt;
        transferAmount = solutionTransferComponent.TransferAmount;

        return true;
    }

    private void OnScoopVat(EntityUid uid, DeepFryerComponent component, DeepFryerScoopVatMessage args)
    {
        var user = args.Actor;

        if (!TryGetActiveHandSolutionContainer(uid, user, out var heldItem, out var heldSolution,
                out var transferAmount))
            return;

        if (!_solutionContainer.TryGetSolution(uid, component.Solution.Name, out var solution))
            return;

        _solutionTransfer.Transfer(new SolutionTransferData(user,
            uid,
            solution.Value,
            heldItem.Value,
            heldSolution.Value,
            transferAmount));

        // UI update is not necessary here, because the solution change event handles it.
    }

    private void OnClearSlagStart(EntityUid uid, DeepFryerComponent component, DeepFryerClearSlagMessage args)
    {
        var user = args.Actor;

        if (!TryGetActiveHandSolutionContainer(uid, user, out var heldItem, out var heldSolution,
                out var transferAmount))
            return;

        var wasteVolume = GetWasteVolume(uid, component);
        if (wasteVolume == FixedPoint2.Zero)
        {
            _popupSystem.PopupEntity(
                Loc.GetString("deep-fryer-oil-no-slag"),
                uid,
                user);

            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Clamp((float)wasteVolume * 0.1f, 1f, 5f));

        var ev = new ClearSlagDoAfterEvent(heldSolution.Value.Comp.Solution, transferAmount);

        //JJ Comment - not sure I have DoAfterArgs configured correctly.
        var doAfterArgs = new DoAfterArgs(EntityManager, user, delay, ev, uid, uid, heldItem)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            MovementThreshold = 0.25f,
            NeedHand = true
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnRemoveAllItems(EntityUid uid, DeepFryerComponent component, DeepFryerRemoveAllItemsMessage args)
    {
        if (component.Storage.ContainedEntities.Count == 0)
            return;

        _container.EmptyContainer(component.Storage);

        var user = args.Actor;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user)} removed all items from {ToPrettyString(uid)}.");

        _audio.PlayPvs(component.SoundRemoveItem, uid, AudioParamsInsertRemove);

        UpdateUserInterface(uid, component);
    }

    private void OnClearSlag(EntityUid uid, DeepFryerComponent component, ClearSlagDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Used == null)
            return;

        FixedPoint2 reagentCount = component.WasteReagents.Count();

        var removingSolution = new Solution();
        foreach (var reagent in component.WasteReagents)
        {
            var removed = component.Solution.RemoveReagent(reagent.Reagent.ToString(), args.Amount / reagentCount);
            removingSolution.AddReagent(reagent.Reagent.ToString(), removed);
        }

        if (!_solutionContainer.TryGetSolution(uid, component.SolutionName, out var solution))
            return;

        if (!_solutionContainer.TryGetSolution(args.Used!.Value, args.Solution.Name, out var targetSolution))
            return;

        _solutionContainer.UpdateChemicals(solution.Value);
        _solutionContainer.TryMixAndOverflow(targetSolution.Value, removingSolution,
            args.Solution.MaxVolume, out var _);
    }

    private void OnInitDeepFried(EntityUid uid, DeepFriedComponent component, ComponentInit args)
    {
        component.OriginalName = _nameModifier.GetBaseName(uid); // Frontier: prevent reapplying name modifiers
        UpdateDeepFriedName(uid, component);
    }

    private void OnExamineFried(EntityUid uid, DeepFriedComponent component, ExaminedEvent args)
    {
        // Frontier: assign crispiness levels to a prototype
        if (_prototype.TryIndex<CrispinessLevelSetPrototype>(component.CrispinessLevelSet, out var crispinessLevels))
        {
            if (crispinessLevels.Levels.Count <= 0)
                return;

            int crispiness = int.Max(0, component.Crispiness);
            {
                string examineString;
                if (crispiness < crispinessLevels.Levels.Count)
                    examineString = crispinessLevels.Levels[crispiness].ExamineText;
                else
                    examineString = crispinessLevels.Levels[^1].ExamineText;
                args.PushMarkup(Loc.GetString(examineString));
            }
        }
        // End Frontier
    }

    private void OnPriceCalculation(EntityUid uid, DeepFriedComponent component, ref PriceCalculationEvent args)
    {
        args.Price *= component.PriceCoefficient;
    }

    private void OnSliceDeepFried(EntityUid uid, DeepFriedComponent component, FoodSlicedEvent args)
    {
        MakeCrispy(args.Slice, component.CrispinessLevelSet);

        // Copy relevant values to the slice.
        var sourceDeepFriedComponent = Comp<DeepFriedComponent>(args.Food);
        var sliceDeepFriedComponent = Comp<DeepFriedComponent>(args.Slice);

        sliceDeepFriedComponent.Crispiness = sourceDeepFriedComponent.Crispiness;
        sliceDeepFriedComponent.PriceCoefficient = sourceDeepFriedComponent.PriceCoefficient;

        UpdateDeepFriedName(args.Slice, sliceDeepFriedComponent);

        // TODO: Flavor profiles aren't copied to the slices. This should
        // probably be handled on upstream, but for now let's assume the
        // oil of the deep fryer is overpowering enough for this small
        // hack. This is likely the only place where it would be useful.
        if (TryComp<FlavorProfileComponent>(args.Food, out var sourceFlavorProfileComponent) &&
            TryComp<FlavorProfileComponent>(args.Slice, out var sliceFlavorProfileComponent))
        {
            sliceFlavorProfileComponent.Flavors.UnionWith(sourceFlavorProfileComponent.Flavors);
            sliceFlavorProfileComponent.IgnoreReagents.UnionWith(sourceFlavorProfileComponent.IgnoreReagents);
        }
    }

    public void SetDeepFriedCrispinessLevelSet(EntityUid uid, DeepFriedComponent component, ProtoId<CrispinessLevelSetPrototype> crispiness)
    {
        component.CrispinessLevelSet = crispiness;
        UpdateDeepFriedName(uid, component);
    }
}

public sealed class DeepFryAttemptEvent : CancellableEntityEventArgs
{
    public EntityUid DeepFryer { get; }

    public DeepFryAttemptEvent(EntityUid deepFryer)
    {
        DeepFryer = deepFryer;
    }
}

public sealed class BeingDeepFriedEvent : EntityEventArgs
{
    public EntityUid DeepFryer { get; }
    public EntityUid Item { get; }
    public bool TurnIntoFood { get; set; }

    public BeingDeepFriedEvent(EntityUid deepFryer, EntityUid item)
    {
        DeepFryer = deepFryer;
        Item = item;
    }
}


