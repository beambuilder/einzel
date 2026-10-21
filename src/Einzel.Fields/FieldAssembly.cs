using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Fields;

/// <summary>
/// Several field elements acting at once, summed.
/// </summary>
/// <remarks>
/// <para>
/// Superposition is exact for electrostatics, so summing element fields and
/// element potentials is the whole implementation. Spec section 10 leans on the
/// same property much harder: solving once per electrode at unit potential turns
/// voltage optimisation from a solve-per-iteration problem into arithmetic.
/// </para>
/// <para>
/// The two structural queries do not sum, and each is handled conservatively. A
/// run is field-free only where every element is field-free, so the shortest run
/// wins. Discontinuities are trickier — see
/// <see cref="SignedDistanceToDiscontinuity"/>.
/// </para>
/// </remarks>
public sealed class SuperposedField : IElectrostaticField, IConductorBounded
{
    private readonly IElectrostaticField[] _elements;

    /// <summary>Creates a superposition.</summary>
    /// <param name="elements">The elements to sum.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
    public SuperposedField(IEnumerable<IElectrostaticField> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _elements = [.. elements];
    }

    /// <summary>The elements being summed.</summary>
    public IReadOnlyList<IElectrostaticField> Elements => _elements;

    /// <inheritdoc/>
    /// <remarks>
    /// The nearest conductor over every element that has any. Potentials
    /// superpose; solid bodies do not, they simply coexist, so this is a union
    /// rather than a sum - which is why it cannot ride on the same loop as the
    /// field itself.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            if (element is IConductorBounded bounded)
            {
                var distance = bounded.SignedDistanceToConductor(in position);

                if (distance < nearest)
                {
                    nearest = distance;
                }
            }
        }

        return nearest;
    }

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position)
    {
        string? nearestName = null;
        var nearest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            if (element is IConductorBounded bounded)
            {
                var distance = bounded.SignedDistanceToConductor(in position);

                if (distance < nearest)
                {
                    nearest = distance;
                    nearestName = bounded.ConductorAt(in position);
                }
            }
        }

        return nearestName;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var total = Vec3.Zero;

        foreach (var element in _elements)
        {
            total += element.ElectricFieldAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var total = 0.0;

        foreach (var element in _elements)
        {
            total += element.PotentialAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The finest member governs. A sum is resolved no better than its
    /// least-resolved term, and a step that outruns any one element's grid is
    /// stepping over structure that element holds.
    /// </remarks>
    public double ResolutionLength
    {
        get
        {
            var finest = double.PositiveInfinity;

            foreach (var element in _elements)
            {
                finest = Math.Min(finest, element.ResolutionLength);
            }

            return finest;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A run is field-free only where every element is, so the shortest run
    /// governs. Returning less than the true run length is always safe; returning
    /// more would advance an ion in a straight line through a region where it
    /// should have been accelerating.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var shortest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            shortest = Math.Min(shortest, element.FieldFreeRunLength(in position, in direction));

            if (shortest <= 0.0)
            {
                return 0.0;
            }
        }

        return shortest;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The magnitude is the distance to the nearest declared discontinuity; the
    /// sign is the product of the element signs, so crossing any single boundary
    /// flips it and the integrator localises the crossing by bisection.
    /// </para>
    /// <para>
    /// The known limit: a step crossing two boundaries at once returns the sign to
    /// where it started and the crossing is missed. That is benign rather than
    /// silent — the step-size controller sees the resulting error and refines
    /// until the crossings separate — but it means the exact-landing guarantee
    /// holds for one boundary per step, not two. Nothing in schema v0.1 can build
    /// such a geometry; a stacked-ring mirror could, and the fix then is to track
    /// element regions rather than to sum signs.
    /// </para>
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;
        var sign = 1;

        foreach (var element in _elements)
        {
            var distance = element.SignedDistanceToDiscontinuity(in position);

            if (!double.IsFinite(distance))
            {
                continue;
            }

            nearest = Math.Min(nearest, Math.Abs(distance));
            sign *= distance < 0.0 ? -1 : 1;
        }

        return double.IsPositiveInfinity(nearest) ? double.PositiveInfinity : nearest * sign;
    }
}

/// <summary>
/// Builds engine field objects from a validated model.
/// </summary>
/// <remarks>
/// The seam between the declarative document and the engine. It lives here
/// rather than in Einzel.Core because Core is the innermost assembly and cannot
/// reference the field types built from it.
/// </remarks>
public static class FieldAssembly
{
    /// <summary>Builds the field described by a compiled model.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="store">
    /// Where each solved channel is loaded from or kept, or null to solve every one.
    /// </param>
    /// <returns>The assembled field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">A solved element did not converge.</exception>
    /// <remarks>
    /// Throws on a solve that missed its tolerance, rather than handing back a field
    /// that is indistinguishable from a converged one. Callers that produce a
    /// reportable result should use <see cref="BuildReported"/> instead and carry the
    /// warning onto the number, which is what GRD-2 asks for; there is nowhere to
    /// attach a taint on a bare field, so the only honest alternatives here are to
    /// throw or to hide it.
    /// </remarks>
    public static IElectrostaticField Build(CompiledModel model, Solved.ISolveStore? store = null)
    {
        var (field, warnings) = BuildReported(model, store);

        // Narrowed from "any warning at all" to "any warning saying the field is WRONG",
        // and the distinction is about WHO KNOWS THE THING, not about how bad it is.
        //
        // An unconverged solve is evidence only the engine has: nothing in the document
        // says the residual missed its tolerance, the field looks identical either way, and
        // a bare field has no envelope to carry it on. Dropping that silently is the seam
        // this project has already lost numbers at, so refusing is the only honest option.
        //
        // A region's potential step is not that. It is a consequence of geometry the author
        // wrote down and can see, the field is exactly what the document declares, and the
        // trajectory across it is exactly right. Refusing every one would make `Build`
        // unusable for the composed beamlines a region exists to enable, in exchange for
        // repeating something the document already says.
        var defects = warnings
            .Where(w => w.Severity == Core.Results.WarningSeverity.ValidityViolation)
            .ToList();

        if (defects.Count > 0)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.ConvergenceFailed,
                Path = "/fields",
                Constraint = defects[0].Message,
                Suggestion = "run 'einzel solve' to see the residual and the convergence factor "
                    + "for every element and channel, or use FieldAssembly.BuildReported to carry "
                    + "the warning onto a result instead of failing",
            });
        }

        return field;
    }

    /// <summary>Builds the field, and the warnings its solves earned.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="store">
    /// Where each solved channel is loaded from or kept, or null to solve every one. A channel
    /// loaded earns the same warnings as one solved: its report is kept, and what the mesh
    /// samples is asked of the geometry again.
    /// </param>
    /// <returns>The assembled field, and any non-suppressible warnings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// A solved field that missed its tolerance is not distinguishable from one that
    /// met it by looking at it, so the evidence has to travel separately. The
    /// segmented quadrupole lost its ion at the wrong working point for a whole
    /// revision because the report saying so was discarded at this seam.
    /// </remarks>
    public static (IElectrostaticField Field, IReadOnlyList<Core.Results.ValidityWarning> Warnings)
        BuildReported(CompiledModel model, Solved.ISolveStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var elements = new List<IElectrostaticField>(model.Fields.Count);
        var warnings = new List<Core.Results.ValidityWarning>();

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var element = model.Fields[index];

            if (element.Region is { } bounded)
            {
                // A hard edge steps the potential at the face, and the step is reported.
                // A fringe makes the potential continuous there, so there is no step to
                // measure - what is reported instead is that a fringe is a modelling choice.
                warnings.Add(bounded.FringeSi > 0.0
                    ? new Core.Results.ValidityWarning(
                        "field.region-fringe",
                        $"field element {index} is bounded by a region whose faces carry a fringe of "
                        + $"{bounded.FringeSi * 1e3:G4} mm: the element rises linearly from nothing at the "
                        + "face to full strength that far inside, so its potential is continuous across the "
                        + "face and the field there is the gradient of that ramp rather than a step. The "
                        + "fringe stands in for a real electrode's field decaying over about a bore radius; "
                        + "it is a declared shape, not the solved fringe of any geometry",
                        Core.Results.WarningSeverity.Provenance)
                    : RegionStep(element, bounded, index, model.AccelerationPotentialSi));
            }

            switch (element.Kind)
            {
                case CompiledFieldKind.FieldFree:
                    // Contributes nothing to the sum, and adding it would only make
                    // FieldFreeRunLength do redundant work.
                    break;

                case CompiledFieldKind.Uniform:
                    elements.Add(Sequenced(element, Analytic));
                    break;

                case CompiledFieldKind.HalfSpaceUniform:
                    elements.Add(Sequenced(element, Analytic));
                    break;

                case CompiledFieldKind.IdealQuadrupoleRf:
                    elements.Add(Sequenced(element, Analytic));
                    break;

                case CompiledFieldKind.QuadroLogarithmic:
                    elements.Add(Sequenced(element, Analytic));
                    break;

                case CompiledFieldKind.Solved3D:
                {
                    var geometry = Solved.Geometry3D.Of(element.Solve3D!);

                    var (built, report3d) = Solved.GeometryBuilder3D.BuildField(geometry, store);
                    elements.Add(built);
                    Note(warnings, report3d, index, "solved3d");
                    break;
                }

                case CompiledFieldKind.Solved2D:
                {
                    // The solve happens here, once per build. Nothing about what
                    // the electrodes add up to is known at this level.
                    var (plane, report2d) = Solved.GeometryBuilder.Build(element.Solve!, store);

                    // A tilted extrusion axis is applied here rather than baked into the
                    // geometry, because a rotation of a solved field is exact while a
                    // solve of a rotated geometry is not: the anisotropy the tilt exists
                    // to create is far below the field error of any affordable mesh.
                    elements.Add(element.Solve!.TiltHalfTurns == 0.0
                        ? plane
                        : new RotatedField(
                            plane,
                            element.Solve!.TiltHalfTurns,
                            element.Solve!.TiltCentreX,
                            element.Solve!.TiltCentreZ));
                    Note(warnings, report2d, index, "solved2d");
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(model), element.Kind, "unhandled field element kind");
            }
        }

        // Chosen by what the sum contains, not by what the caller asks for. A
        // SuperposedField satisfies only IElectrostaticField, and a driven member
        // answers that interface at t = 0 - so a driven element summed with anything
        // else would silently become a snapshot of the RF at the top of its cycle,
        // with no exception and nothing in the result to say so. Same failure the
        // diffusive mode was found stepping a density through.
        var field = elements.Count switch
        {
            0 => FieldFreeSpace.Instance,
            1 => elements[0],
            _ when elements.Exists(e => e is ITimeVaryingField) =>
                new DrivenSuperposedField(elements),
            _ => (IElectrostaticField)new SuperposedField(elements),
        };

        return (field, warnings);
    }

    /// <summary>Records a solve that missed its tolerance, per GRD-2.</summary>
    /// <remarks>
    /// A validity violation rather than an advisory, so it cannot be suppressed. An
    /// unconverged field is not a result with wider error bars - the residual says
    /// nothing about how far the potential is from the true one, only that the
    /// iteration stopped moving - so every number downstream of it is unquantified
    /// rather than imprecise.
    /// </remarks>
    /// <summary>One analytic element, at the values a single compilation holds.</summary>
    /// <remarks>
    /// A declared region is applied here rather than at the call sites, so a sequenced
    /// element gets it on every phase: this is the one place an analytic field is built.
    /// </remarks>
    private static IElectrostaticField Analytic(CompiledField element)
    {
        var field = AnalyticCore(element);

        return element.Region is { } region
            ? Einzel.Fields.Analytic.BoundedField.Around(field, region)
            : field;
    }

    /// <summary>How much potential an ion gains or loses crossing a region boundary.</summary>
    /// <remarks>
    /// <para>
    /// A region is a box and a box is not an equipotential of anything interesting, so the
    /// potential does not match across it: an ion crossing gains or loses whatever the
    /// inner field held there. That is a real energy error and it is reported <b>whether or
    /// not it crosses a threshold</b>, which is REG-2's rule — a reader who sees 0.3 V
    /// knows the boundary was checked, and one who sees nothing cannot tell that from its
    /// not having been checked.
    /// </para>
    /// <para>
    /// Sampled on a grid over the six faces rather than maximised analytically, because the
    /// inner field is any analytic kind and only it knows its own shape. The sampling is
    /// stated in the message: a coarse grid can miss a narrow spike, and a number that
    /// does not say how it was obtained invites more trust than it has earned.
    /// </para>
    /// </remarks>
    private static Core.Results.ValidityWarning RegionStep(
        CompiledField element,
        Core.Model.FieldRegion region,
        int index,
        double beamPotentialSi)
    {
        const int Samples = 17;

        var inner = AnalyticCore(element);
        var worst = 0.0;
        var skipped = 0;

        for (var a = 0; a < Samples; a++)
        {
            var fa = (double)a / (Samples - 1);

            for (var b = 0; b < Samples; b++)
            {
                var fb = (double)b / (Samples - 1);

                var x = region.MinX + (fa * (region.MaxX - region.MinX));
                var y = region.MinY + (fa * (region.MaxY - region.MinY));
                var z = region.MinZ + (fb * (region.MaxZ - region.MinZ));

                var yb = region.MinY + (fb * (region.MaxY - region.MinY));

                foreach (var point in new[]
                {
                    new Vec3(region.MinX, y, z), new Vec3(region.MaxX, y, z),
                    new Vec3(x, region.MinY, z), new Vec3(x, region.MaxY, z),
                    new Vec3(x, yb, region.MinZ), new Vec3(x, yb, region.MaxZ),
                })
                {
                    double potential;

                    try
                    {
                        potential = Math.Abs(inner.PotentialAt(in point));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // An analytic field may refuse a point rather than return a huge
                        // number there - the quadro-logarithmic field does exactly that on
                        // its axis, where the central electrode is, and a sampling grid
                        // over a centred box lands on it. A refused point is not a step;
                        // it is a place the field says has no potential. Counted rather
                        // than passed over, because a survey with holes in it should say
                        // how many.
                        skipped++;
                        continue;
                    }

                    if (double.IsFinite(potential) && potential > worst)
                    {
                        worst = potential;
                    }
                }
            }
        }

        // Reported as a fraction of what the ion carries as well as in volts, because a
        // step means nothing on its own: 100 V beside a 10 V beam and beside a 4 kV beam
        // are different statements.
        var beam = Math.Abs(beamPotentialSi);

        var relative = beam > 0.0
            ? $" - {worst / beam:P3} of the {beam:G4} V this ion is accelerated through"
            : string.Empty;

        // QUALIFIED, NOT A VIOLATION, and the first version of this had it wrong.
        //
        // An ion is moved by the FIELD, and the field is exactly the declared one on each
        // side of the boundary - so a bounded uniform field is an accelerating gap followed
        // by a field-free drift, which is an ordinary instrument. Measured: the flight time
        // across one matches sqrt(2 m L / (q E)) + (D - L) / v to six figures. The step
        // costs the trajectory nothing.
        //
        // What it does cost is stated below rather than overstated: the potential is what
        // jumps, so the energy-drift diagnostic is meaningless across the boundary, and the
        // piecewise field is not conservative across it - an ion that crosses MORE THAN
        // ONCE, entering by one face and leaving by another, can gain or lose energy that
        // no electrode supplied.
        return new Core.Results.ValidityWarning(
            "field.region-potential-step",
            $"field element {index} is bounded by a region, and its potential reaches "
                + $"{worst:G4} V on that boundary{relative}. The TRAJECTORY is unaffected: an "
                + "ion is moved by the field, which is exactly the declared one on each side, "
                + "so a straight crossing is an ordinary accelerating gap. What the step "
                + "costs is that the energy-drift diagnostic jumps at the boundary, and that "
                + "the piecewise field is not conservative across it - an ion that crosses "
                + "more than once, in by one face and out by another, can gain or lose energy "
                + "no electrode supplied. Sampled on a "
                + $"{Samples}x{Samples} grid over each of the six faces, so a narrower spike "
                + "than that spacing would be missed"
                + (skipped > 0
                    ? $", and {skipped} of those points were refused by the field itself "
                      + "(a singular axis, say) and took no part"
                    : string.Empty)
                + ".",
            Core.Results.WarningSeverity.Qualified);
    }

    /// <summary>The unbounded analytic field itself, before any region is applied.</summary>
    private static IElectrostaticField AnalyticCore(CompiledField element) => element.Kind switch
    {
        CompiledFieldKind.Uniform => UniformField.Create(element.Field),

        CompiledFieldKind.HalfSpaceUniform => HalfSpaceUniformField.Create(
            element.PlanePoint,
            element.InwardNormal,
            Quantity.Si(element.PotentialGradientSi, Dimension.ElectricField)),

        CompiledFieldKind.QuadroLogarithmic => QuadroLogarithmicField.Create(
            Quantity.Si(element.CurvatureSi, Dimension.ElectricFieldGradient),
            Quantity.Si(element.CharacteristicRadiusSi, Dimension.LengthDimension),
            element.Centre),

        CompiledFieldKind.IdealQuadrupoleRf => Einzel.Fields.Analytic.IdealQuadrupoleRf.Create(
            Quantity.Si(element.DirectPotentialSi, Dimension.ElectricPotential),
            Quantity.Si(element.DriveAmplitudeSi, Dimension.ElectricPotential),
            Quantity.Si(element.DriveFrequencySi, Dimension.Frequency),
            Quantity.Si(element.InscribedRadiusSi, Dimension.LengthDimension),
            axis: element.Axis),

        _ => throw new ArgumentOutOfRangeException(
            nameof(element),
            element.Kind,
            "only the analytic kinds are built this way; a solved element carries its "
            + "phases as re-weightings inside its own builder"),
    };

    /// <summary>
    /// An element, switched by the instrument's timeline where the timeline reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sequence is the instrument's, so an analytic element follows it exactly as a
    /// solved one does. The validator leaves <c>Phases</c> empty when every phase compiles
    /// this element to the same numbers, so a static element stays static rather than
    /// answering a time-varying interface and handing the integrator switch instants to
    /// land on for nothing.
    /// </para>
    /// <para>
    /// Wrapped generically rather than per kind. A per-phase branch inside each analytic
    /// field would be a special case layered on shared infrastructure, and there would be
    /// one more of them every time a field kind is added.
    /// </para>
    /// </remarks>
    private static IElectrostaticField Sequenced(
        CompiledField element, Func<CompiledField, IElectrostaticField> build) =>
        element.Phases.Count == 0
            ? build(element)
            : new SequencedField(
                [.. element.Phases.Select(build)], element.PhaseBoundariesSeconds);

    private static void Note(
        List<Core.Results.ValidityWarning> warnings, Solved.SolveReport report, int index, string kind)
    {
        // What the builder found about the mesh before solving it, named by element because
        // the builder does not know which element of the model it was handed. Carried whether
        // or not the solve converged: a solve can converge perfectly on a coin toss.
        foreach (var found in report.Warnings)
        {
            warnings.Add(found with { Message = $"field element {index} ({kind}): {found.Message}" });
        }

        if (report.Converged)
        {
            return;
        }

        var relative = report.InitialResidual > 0.0
            ? report.FinalResidual / report.InitialResidual
            : 0.0;

        warnings.Add(new Core.Results.ValidityWarning(
            "field.not-converged",
            $"field element {index} ({kind}) stopped at a relative residual of {relative:G3} after "
            + $"{report.Cycles} cycles, at a convergence factor of {report.ConvergenceFactor:F3}. The "
            + "potential it produced is not known to be the solution of the geometry that was declared, "
            + "so nothing computed through it carries its stated accuracy",
            Core.Results.WarningSeverity.ValidityViolation));
    }
}
