using Content.Server.Chemistry.EntitySystems;
using Content.Server.Popups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Timing;
using Content.Shared.Weapons.Melee;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Fluids.EntitySystems;

/// <inheritdoc/>
public sealed class AbsorbentSystem : SharedAbsorbentSystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popups = default!;
    [Dependency] private readonly PuddleSystem _puddleSystem = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AbsorbentComponent, ComponentInit>(OnAbsorbentInit);
        SubscribeLocalEvent<AbsorbentComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<AbsorbentComponent, InteractNoHandEvent>(OnInteractNoHand);
        SubscribeLocalEvent<AbsorbentComponent, SolutionChangedEvent>(OnAbsorbentSolutionChange);
    }

    private void OnAbsorbentInit(EntityUid uid, AbsorbentComponent component, ComponentInit args)
    {
        // TODO: I know dirty on init but no prediction moment.
        UpdateAbsorbent(uid, component);
    }

    private void OnAbsorbentSolutionChange(EntityUid uid, AbsorbentComponent component, SolutionChangedEvent args)
    {
        UpdateAbsorbent(uid, component);
    }

    private void UpdateAbsorbent(EntityUid uid, AbsorbentComponent component)
    {
        if (!_solutionSystem.TryGetSolution(uid, AbsorbentComponent.SolutionName, out var solution))
            return;

        var oldProgress = component.Progress.ShallowClone();
        component.Progress.Clear();

        if (solution.TryGetReagent(PuddleSystem.EvaporationReagent, out var water))
        {
            component.Progress[_prototype.Index<ReagentPrototype>(PuddleSystem.EvaporationReagent).SubstanceColor] = water.Float();
        }

        var otherColor = solution.GetColorWithout(_prototype, PuddleSystem.EvaporationReagent);
        var other = (solution.Volume - water).Float();

        if (other > 0f)
        {
            component.Progress[otherColor] = other;
        }

        var remainder = solution.AvailableVolume;

        if (remainder > FixedPoint2.Zero)
        {
            component.Progress[Color.DarkGray] = remainder.Float();
        }

        if (component.Progress.Equals(oldProgress))
            return;

        Dirty(component);
    }

    private void OnInteractNoHand(EntityUid uid, AbsorbentComponent component, InteractNoHandEvent args)
    {
        if (args.Handled || args.Target == null)
            return;

        Mop(uid, args.Target.Value, uid, component);
        args.Handled = true;
    }

    private void OnAfterInteract(EntityUid uid, AbsorbentComponent component, AfterInteractEvent args)
    {
        if (!args.CanReach || args.Handled || args.Target == null)
            return;

        Mop(args.User, args.Target.Value, args.Used, component);
        args.Handled = true;
    }

    private void Mop(EntityUid user, EntityUid target, EntityUid used, AbsorbentComponent component)
    {
        if (!_solutionSystem.TryGetSolution(used, AbsorbentComponent.SolutionName, out var absorberSoln))
            return;

        if (_useDelay.ActiveDelay(used))
            return;

        // If it's a puddle try to grab from
        if (!TryPuddleInteract(user, used, target, component, absorberSoln))
        {
            // Do a transfer, try to get water onto us and transfer anything else to them.

            // If it's anything else transfer to
            if (!TryTransferAbsorber(user, used, target, component, absorberSoln))
                return;
        }
    }

    /// <summary>
    ///     Attempt to fill an absorber from some refillable solution.
    /// </summary>
    private bool TryTransferAbsorber(EntityUid user, EntityUid used, EntityUid target, AbsorbentComponent component, Solution absorberSoln)
    {
        if (!TryComp(target, out RefillableSolutionComponent? refillable))
            return false;

        if (!_solutionSystem.TryGetRefillableSolution(target, out var refillableSolution, refillable: refillable))
            return false;

        if (refillableSolution.Volume < 0)
        {
            var msg = Loc.GetString("mopping-system-target-container-empty", ("target", target));
            _popups.PopupEntity(msg, user, user);
            return false;
        }

        var transferAmount = component.PickupAmount / PuddleSystem.EvaporationReagentRatio -
            absorberSoln.GetReagentQuantity(PuddleSystem.EvaporationReagent);

        transferAmount = transferAmount > 0 ? transferAmount : 0;

        // Remove the non-water reagents.
        // Remove water on target
        // Then do the transfer.
        var water = refillableSolution.RemoveReagent(PuddleSystem.EvaporationReagent, transferAmount);

        // Reads from refillableSolution.AvailableVolume have to
        // be done after possibly removing some water
        if (refillableSolution.AvailableVolume == FixedPoint2.Zero){
            _popups.PopupEntity(Loc.GetString("mopping-system-full", ("used", target)), user, user);
            // Had it removed any it wouldn't be full
            DebugTools.Assert(water == FixedPoint2.Zero);
            return false;
        }

        var nonWaterAmount = component.PickupAmount > refillableSolution.AvailableVolume ?
            refillableSolution.AvailableVolume : component.PickupAmount;

        var nonWater = absorberSoln.SplitSolutionWithout(nonWaterAmount, PuddleSystem.EvaporationReagent);

        if (nonWater.Volume == FixedPoint2.Zero && absorberSoln.AvailableVolume == FixedPoint2.Zero)
        {
            _popups.PopupEntity(Loc.GetString("mopping-system-puddle-space", ("used", used)), user, user);
            return false;
        }

        if (water == FixedPoint2.Zero && nonWater.Volume == FixedPoint2.Zero)
        {
            _popups.PopupEntity(Loc.GetString("mopping-system-target-container-empty-water", ("target", target)), user, user);
            return false;
        }


        if (water > 0 && !_solutionContainerSystem.TryAddReagent(used, absorberSoln, PuddleSystem.EvaporationReagent, water,
                out _))
        {
            _popups.PopupEntity(Loc.GetString("mopping-system-full", ("used", used)), used, user);
        }

        if (nonWater.Volume > 0 && !_solutionContainerSystem.TryAddSolution(target, refillableSolution, nonWater))
        {
            absorberSoln.AddSolution(nonWater, _prototype);
            _popups.PopupEntity(Loc.GetString("mopping-system-full", ("used", target)), user, user);
        }
        _audio.PlayPvs(component.TransferSound, target);
        _useDelay.BeginDelay(used);
        return true;
    }

    /// <summary>
    ///     Logic for an absorbing entity interacting with a puddle.
    /// </summary>
    private bool TryPuddleInteract(EntityUid user, EntityUid used, EntityUid target, AbsorbentComponent absorber, Solution absorberSoln)
    {
        if (!TryComp(target, out PuddleComponent? puddle))
            return false;

        if (!_solutionSystem.TryGetSolution(target, puddle.SolutionName, out var puddleSolution) || puddleSolution.Volume <= 0)
            return false;

        // Check if the puddle has any non-evaporative reagents
        if (_puddleSystem.CanFullyEvaporate(puddleSolution))
        {
            _popups.PopupEntity(Loc.GetString("mopping-system-puddle-evaporate", ("target", target)), user, user);
            return true;
        }

        // Check if we have any evaporative reagents on our absorber to transfer
        absorberSoln.TryGetReagent(PuddleSystem.EvaporationReagent, out var available);

        // No material
        if (available == FixedPoint2.Zero)
        {
            _popups.PopupEntity(Loc.GetString("mopping-system-no-water", ("used", used)), user, user);
            return true;
        }

        // hack to compensate loss due to fractions that would be periodical
        // and make sure (available / Ratio) * Ratio is not less than available
        // otherwise the absorber would very annoyingly never fill
        // it's not necessary if the ratio is not periodical in a decimal base
        if (PuddleSystem.EvaporationReagentRatio % 2 != 0 &&
                PuddleSystem.EvaporationReagentRatio % 5 != 0){
            available += FixedPoint2.Epsilon;
        }
        available *= PuddleSystem.EvaporationReagentRatio;

        var transferMax = absorber.PickupAmount;
        var transferAmount = available > transferMax ? transferMax : available;

        var split = puddleSolution.SplitSolutionWithout(transferAmount, PuddleSystem.EvaporationReagent);

        absorberSoln.RemoveReagent(PuddleSystem.EvaporationReagent, split.Volume / PuddleSystem.EvaporationReagentRatio);
        puddleSolution.AddReagent(PuddleSystem.EvaporationReagent, split.Volume / PuddleSystem.EvaporationReagentRatio);
        absorberSoln.AddSolution(split.SplitSolution(absorberSoln.AvailableVolume), _prototype);
        // puts back the excess if any, a hack to circumvent it being a differential equation and prevent the
        // overflow of the absorber due to residual water not being checked for
        if (split.Volume > 0){
            puddleSolution.AddSolution(split, _prototype);
        }

        _solutionSystem.UpdateChemicals(used, absorberSoln);
        _solutionSystem.UpdateChemicals(target, puddleSolution);
        _audio.PlayPvs(absorber.PickupSound, target);
        _useDelay.BeginDelay(used);

        var userXform = Transform(user);
        var targetPos = _transform.GetWorldPosition(target);
        var localPos = _transform.GetInvWorldMatrix(userXform).Transform(targetPos);
        localPos = userXform.LocalRotation.RotateVec(localPos);

        _melee.DoLunge(user, Angle.Zero, localPos, null, false);

        return true;
    }
}
