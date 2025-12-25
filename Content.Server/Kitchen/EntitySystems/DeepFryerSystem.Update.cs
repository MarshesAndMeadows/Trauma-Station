using System.Linq;
using Content.Server.Kitchen.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server.Kitchen.EntitySystems;

public sealed partial class DeepFryerSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var deepFryers = EntityManager.EntityQueryEnumerator<DeepFryerComponent>();
        while (deepFryers.MoveNext(out var uid, out var component))
        {
            if (_gameTiming.CurTime < component.NextFryTime ||
                !_power.IsPowered(uid))
            {
                continue;
            }

            UpdateNextFryTime(uid, component);

            if (!_solutionContainer.TryGetSolution(uid, component.Solution.Name, out var solution))
                continue;

            // Heat the vat solution and contained entities.
            _solutionContainer.SetTemperature(solution.Value, component.PoweredTemperature);

            foreach (var item in component.Storage.ContainedEntities)
                CookItem(uid, component, item);

            // Do something bad if there's enough heat but not enough oil.
            var oilVolume = GetOilVolume(uid, component);

            if (oilVolume < component.SafeOilVolume)
            {
                foreach (var item in component.Storage.ContainedEntities.ToArray())
                    BurnItem(uid, component, item);

                if (oilVolume > FixedPoint2.Zero)
                {
                    //JJ Comment - this code block makes the Linter fail, and doesn't seem to be necessary with the changes I made.
                    foreach (var reagent in component.Solution.Contents.ToArray())
                    {
                        _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.ToString(), out var proto);

                        // Apply each unsafe oil effect to the deep fryer
                        foreach (var effect in component.UnsafeOilVolumeEffects)
                        {
                            //TODO verify this doesnt make game explode
                            // Let the effect system handle conditions and application
                            _entityEffects.TryApplyEffect(
                                target: uid,              // The deep fryer itself
                                effect: effect,
                                //sussy scaling, set to 1 if its borked
                                scale: reagent.Quantity.Float(),  // Use reagent quantity as scale
                                user: null                // No specific user caused this
                            );
                        }

                    }

                    component.Solution.RemoveAllSolution();

                    _popupSystem.PopupEntity(
                        Loc.GetString("deep-fryer-oil-volume-low",
                            ("deepFryer", uid)),
                        uid,
                        PopupType.SmallCaution);

                    continue;
                }
            }

            // We only alert the chef that there's a problem with oil purity
            // if there's anything to cook beyond this point.
            if (!component.Storage.ContainedEntities.Any())
            {
                continue;
            }

            if (GetOilPurity(uid, component) < component.FryingOilThreshold)
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("deep-fryer-oil-purity-low",
                        ("deepFryer", uid)),
                    uid,
                    Filter.Pvs(uid, PvsWarningRange),
                    true);
                continue;
            }

            foreach (var item in component.Storage.ContainedEntities.ToArray())
                DeepFry(uid, component, item);

            // After the round of frying, replace the spent oil with a
            // waste product.
            if (component.WasteToAdd > FixedPoint2.Zero)
            {
                foreach (var reagent in component.WasteReagents)
                    component.Solution.AddReagent(reagent.Reagent.ToString(), reagent.Quantity * component.WasteToAdd);

                component.WasteToAdd = FixedPoint2.Zero;

                _solutionContainer.UpdateChemicals(solution.Value, true);
            }

            UpdateUserInterface(uid, component);
        }
    }

    private void UpdateAmbientSound(EntityUid uid, DeepFryerComponent component)
    {
        _ambientSound.SetAmbience(uid, HasBubblingOil(uid, component));
    }

    private void UpdateNextFryTime(EntityUid uid, DeepFryerComponent component)
    {
        component.NextFryTime = _gameTiming.CurTime + component.FryInterval;
    }

}
