using Einzel.Core.Geometry;
using Einzel.Fields;

namespace Einzel.Transport.Integration;

/// <summary>
/// The Dormand-Prince 5(4) embedded Runge-Kutta pair.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 11 makes adaptive-step Runge-Kutta with per-step error control
/// the default integrator. Dormand-Prince 5(4) is the standard choice: seven
/// stages, a fifth-order solution with an embedded fourth-order estimate for
/// error control, and first-same-as-last, so an accepted step costs six field
/// evaluations rather than seven.
/// </para>
/// <para>
/// A note on where this sits in the error budget. Spec section 8 is explicit that
/// the instinct to reach for a higher-order integrator is "usually the wrong
/// lever", because in a solved field the dominant error is the interpolant's
/// discontinuous derivatives at cell boundaries, not the integrator's truncation.
/// Stage 1 has no interpolant, which is exactly why the integrator can be
/// characterised honestly here: whatever error shows up against a closed form is
/// the integrator's own.
/// </para>
/// <para>
/// All coefficients are written as exact rational quotients rather than decimal
/// literals, so the compiler rounds each once, correctly, instead of inheriting
/// whatever a transcription rounded to.
/// </para>
/// </remarks>
internal static class DormandPrince54
{
    /// <summary>The order of the solution that is propagated.</summary>
    public const int Order = 5;

    // Stage times, as fractions of the step. Unused while the field is static -
    // every stage then samples the same field - and load-bearing the moment it is
    // not, because a stage evaluated at the wrong instant of an RF cycle is a
    // wrong force applied with full fifth-order confidence.
    internal const double C2 = 1.0 / 5.0;
    internal const double C3 = 3.0 / 10.0;
    internal const double C4 = 4.0 / 5.0;
    internal const double C5 = 8.0 / 9.0;
    internal const double C6 = 1.0;
    internal const double C7 = 1.0;

    // Stage coefficients.
    internal const double A21 = 1.0 / 5.0;

    internal const double A31 = 3.0 / 40.0;
    internal const double A32 = 9.0 / 40.0;

    internal const double A41 = 44.0 / 45.0;
    internal const double A42 = -56.0 / 15.0;
    internal const double A43 = 32.0 / 9.0;

    internal const double A51 = 19372.0 / 6561.0;
    internal const double A52 = -25360.0 / 2187.0;
    internal const double A53 = 64448.0 / 6561.0;
    internal const double A54 = -212.0 / 729.0;

    internal const double A61 = 9017.0 / 3168.0;
    internal const double A62 = -355.0 / 33.0;
    internal const double A63 = 46732.0 / 5247.0;
    internal const double A64 = 49.0 / 176.0;
    internal const double A65 = -5103.0 / 18656.0;

    // Fifth-order weights. Also the seventh stage's coefficients, which is what
    // makes the method first-same-as-last.
    internal const double B1 = 35.0 / 384.0;
    internal const double B3 = 500.0 / 1113.0;
    internal const double B4 = 125.0 / 192.0;
    internal const double B5 = -2187.0 / 6784.0;
    internal const double B6 = 11.0 / 84.0;

    // Fourth-order weights, for the embedded error estimate.
    internal const double E1 = 5179.0 / 57600.0;
    internal const double E3 = 7571.0 / 16695.0;
    internal const double E4 = 393.0 / 640.0;
    internal const double E5 = -92097.0 / 339200.0;
    internal const double E6 = 187.0 / 2100.0;
    internal const double E7 = 1.0 / 40.0;

    /// <summary>
    /// Takes one step, producing the fifth-order state, the difference between
    /// the fifth- and fourth-order solutions, and the derivative at the new state
    /// for reuse as the next step's first stage.
    /// </summary>
    /// <param name="state">The state at the start of the step.</param>
    /// <param name="k1">The derivative at <paramref name="state"/>.</param>
    /// <param name="stepSize">The step size, in seconds.</param>
    /// <param name="field">The field being integrated through.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="result">The fifth-order state at the end of the step.</param>
    /// <param name="errorPosition">Position difference between the two solutions, in metres.</param>
    /// <param name="errorVelocity">Velocity difference between the two solutions, in metres per second.</param>
    /// <param name="derivativeAtResult">The derivative at <paramref name="result"/>.</param>
    /// <param name="timeSeconds">
    /// The instant the step starts at. Ignored by a static field; every stage of a
    /// driven one samples a different point in the cycle.
    /// </param>
    /// <param name="endSampleSeconds">
    /// The instant the two end stages (<c>c = 1</c>: the sixth stage and the derivative at
    /// the result) sample the field at, or NaN, the default, for
    /// <c><paramref name="timeSeconds"/> + <paramref name="stepSize"/></c> exactly as
    /// before. A step landing on a waveform edge passes the instant one ulp before it, so
    /// that the stages see the near side of the jump; the state still advances by
    /// <paramref name="stepSize"/>.
    /// </param>
    public static void Step(
        in PhaseState state,
        in PhaseDerivative k1,
        double stepSize,
        IElectrostaticField field,
        double chargeToMass,
        out PhaseState result,
        out Vec3 errorPosition,
        out Vec3 errorVelocity,
        out PhaseDerivative derivativeAtResult,
        double timeSeconds = 0.0,
        double endSampleSeconds = double.NaN)
    {
        var h = stepSize;
        var landing = !double.IsNaN(endSampleSeconds);

        var k2 = Derivative(Offset(state, h, (A21, k1)), field, chargeToMass, timeSeconds + (C2 * h));
        var k3 = Derivative(Offset(state, h, (A31, k1), (A32, k2)), field, chargeToMass, timeSeconds + (C3 * h));
        var k4 = Derivative(
            Offset(state, h, (A41, k1), (A42, k2), (A43, k3)), field, chargeToMass, timeSeconds + (C4 * h));
        var k5 = Derivative(
            Offset(state, h, (A51, k1), (A52, k2), (A53, k3), (A54, k4)),
            field, chargeToMass, timeSeconds + (C5 * h));
        var k6 = Derivative(
            Offset(state, h, (A61, k1), (A62, k2), (A63, k3), (A64, k4), (A65, k5)),
            field, chargeToMass, landing ? endSampleSeconds : timeSeconds + (C6 * h));

        result = Offset(state, h, (B1, k1), (B3, k3), (B4, k4), (B5, k5), (B6, k6));

        var k7 = Derivative(result, field, chargeToMass, landing ? endSampleSeconds : timeSeconds + (C7 * h));
        derivativeAtResult = k7;

        var fourth = Offset(state, h, (E1, k1), (E3, k3), (E4, k4), (E5, k5), (E6, k6), (E7, k7));

        errorPosition = result.Position - fourth.Position;
        errorVelocity = result.Velocity - fourth.Velocity;
    }

    /// <summary>The equation of motion: velocity, and acceleration from the field.</summary>
    /// <param name="state">The state to evaluate at.</param>
    /// <param name="field">The field.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="timeSeconds">The instant to sample a driven field at.</param>
    /// <returns>The derivative.</returns>
    public static PhaseDerivative Derivative(
        in PhaseState state, IElectrostaticField field, double chargeToMass, double timeSeconds = 0.0)
    {
        var position = state.Position;

        // One branch, predictable, and outside nothing: a static field is by far
        // the common case and pays a type test it will always fail the same way.
        var acceleration = field is ITimeVaryingField driven
            ? driven.ElectricFieldAt(in position, timeSeconds)
            : field.ElectricFieldAt(in position);

        return new PhaseDerivative(state.Velocity, acceleration * chargeToMass);
    }

    private static PhaseState Offset(
        in PhaseState origin,
        double h,
        params ReadOnlySpan<(double Weight, PhaseDerivative Derivative)> stages)
    {
        var position = origin.Position;
        var velocity = origin.Velocity;

        foreach (var (weight, derivative) in stages)
        {
            var scale = h * weight;
            position += derivative.Velocity * scale;
            velocity += derivative.Acceleration * scale;
        }

        return new PhaseState(position, velocity);
    }
}
