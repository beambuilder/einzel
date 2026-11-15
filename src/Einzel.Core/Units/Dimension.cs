namespace Einzel.Core.Units;

/// <summary>
/// The physical dimension of a quantity, as exponents over the seven SI base
/// dimensions.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 9: internally SI, double precision, without exception, and at
/// every boundary units are explicit and required. Carrying the dimension
/// alongside every value is what makes <c>{"energy": 4000}</c> a validation
/// error rather than a silent factor of 1.602e-19.
/// </para>
/// <para>
/// Exponents are held as signed bytes and constrained to [-8, 8]. Nothing in ion
/// optics needs more, and the bound turns exponent overflow from a wrong answer
/// into an exception.
/// </para>
/// </remarks>
public readonly struct Dimension : IEquatable<Dimension>
{
    private const int MaxExponent = 8;

    private readonly sbyte _length;
    private readonly sbyte _mass;
    private readonly sbyte _time;
    private readonly sbyte _current;
    private readonly sbyte _temperature;
    private readonly sbyte _amount;
    private readonly sbyte _luminous;

    /// <summary>Constructs a dimension from exponents over the SI base dimensions.</summary>
    /// <param name="length">Exponent of length (metre).</param>
    /// <param name="mass">Exponent of mass (kilogram).</param>
    /// <param name="time">Exponent of time (second).</param>
    /// <param name="current">Exponent of electric current (ampere).</param>
    /// <param name="temperature">Exponent of thermodynamic temperature (kelvin).</param>
    /// <param name="amount">Exponent of amount of substance (mole).</param>
    /// <param name="luminous">Exponent of luminous intensity (candela).</param>
    /// <exception cref="ArgumentOutOfRangeException">An exponent lies outside [-8, 8].</exception>
    public Dimension(
        int length = 0,
        int mass = 0,
        int time = 0,
        int current = 0,
        int temperature = 0,
        int amount = 0,
        int luminous = 0)
    {
        _length = Narrow(length, nameof(length));
        _mass = Narrow(mass, nameof(mass));
        _time = Narrow(time, nameof(time));
        _current = Narrow(current, nameof(current));
        _temperature = Narrow(temperature, nameof(temperature));
        _amount = Narrow(amount, nameof(amount));
        _luminous = Narrow(luminous, nameof(luminous));
    }

    private static sbyte Narrow(int value, string name)
    {
        if (value is < -MaxExponent or > MaxExponent)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"Dimension exponents are constrained to [-{MaxExponent}, {MaxExponent}].");
        }

        return (sbyte)value;
    }

    /// <summary>Exponent of length.</summary>
    public int Length => _length;

    /// <summary>Exponent of mass.</summary>
    public int Mass => _mass;

    /// <summary>Exponent of time.</summary>
    public int Time => _time;

    /// <summary>Exponent of electric current.</summary>
    public int Current => _current;

    /// <summary>Exponent of thermodynamic temperature.</summary>
    public int Temperature => _temperature;

    /// <summary>Exponent of amount of substance.</summary>
    public int Amount => _amount;

    /// <summary>Exponent of luminous intensity.</summary>
    public int Luminous => _luminous;

    /// <summary><see langword="true"/> when every exponent is zero.</summary>
    public bool IsDimensionless =>
        _length == 0
        && _mass == 0
        && _time == 0
        && _current == 0
        && _temperature == 0
        && _amount == 0
        && _luminous == 0;

    /// <summary>A pure number: angles, ratios, efficiencies, parts per million.</summary>
    public static Dimension Dimensionless => default;

    /// <summary>Length, metre.</summary>
    public static Dimension LengthDimension { get; } = new(length: 1);

    /// <summary>Mass, kilogram.</summary>
    public static Dimension MassDimension { get; } = new(mass: 1);

    /// <summary>Time, second.</summary>
    public static Dimension TimeDimension { get; } = new(time: 1);

    /// <summary>Electric current, ampere.</summary>
    public static Dimension CurrentDimension { get; } = new(current: 1);

    /// <summary>Thermodynamic temperature, kelvin.</summary>
    public static Dimension TemperatureDimension { get; } = new(temperature: 1);

    /// <summary>Amount of substance, mole.</summary>
    public static Dimension AmountDimension { get; } = new(amount: 1);

    /// <summary>Luminous intensity, candela.</summary>
    public static Dimension LuminousDimension { get; } = new(luminous: 1);

    /// <summary>Velocity, metre per second.</summary>
    public static Dimension Velocity { get; } = new(length: 1, time: -1);

    /// <summary>Acceleration, metre per second squared.</summary>
    public static Dimension Acceleration { get; } = new(length: 1, time: -2);

    /// <summary>Force, newton.</summary>
    public static Dimension Force { get; } = new(length: 1, mass: 1, time: -2);

    /// <summary>Energy, joule.</summary>
    public static Dimension Energy { get; } = new(length: 2, mass: 1, time: -2);

    /// <summary>Pressure, pascal.</summary>
    public static Dimension Pressure { get; } = new(length: -1, mass: 1, time: -2);

    /// <summary>Frequency, hertz.</summary>
    public static Dimension Frequency { get; } = new(time: -1);

    /// <summary>Electric charge, coulomb.</summary>
    public static Dimension Charge { get; } = new(time: 1, current: 1);

    /// <summary>Electric potential, volt.</summary>
    public static Dimension ElectricPotential { get; } = new(length: 2, mass: 1, time: -3, current: -1);

    /// <summary>Electric field strength, volt per metre.</summary>
    public static Dimension ElectricField { get; } = new(length: 1, mass: 1, time: -3, current: -1);

    /// <summary>Magnetic flux density, tesla: a volt second per square metre.</summary>
    /// <remarks>
    /// <para>
    /// The B of the Lorentz force <c>F = q (E + v x B)</c>, so that <c>v x B</c> has the
    /// dimension of an electric field - a velocity times a tesla is a volt per metre, which
    /// is the identity a dimension system has to get right for the two terms to be added.
    /// </para>
    /// <para>
    /// Distinct from magnetic field strength H (ampere per metre), which differs by the
    /// permeability. This engine models no permeable material, so B is the quantity an ion
    /// feels and the only one a document declares.
    /// </para>
    /// </remarks>
    public static Dimension MagneticFluxDensity { get; } = new(mass: 1, time: -2, current: -1);

    /// <summary>Electric field per unit length: volts per metre squared.</summary>
    /// <remarks>
    /// The curvature of a potential rather than its slope. It appears wherever a field is
    /// linear in position - which is to say wherever the motion is harmonic - and the
    /// oscillation frequency is sqrt(q k / m) from it directly. Distinct from
    /// <see cref="ElectricField"/> by one power of length, which is exactly the distinction
    /// a dimension system exists to keep: a curvature quoted as a field is wrong by a
    /// length, and at millimetre scales that is a factor of a thousand.
    /// </remarks>
    public static Dimension ElectricFieldGradient { get; } =
        new(mass: 1, time: -3, current: -1);

    /// <summary>Number density, reciprocal cubic metre.</summary>
    public static Dimension NumberDensity { get; } = new(length: -3);

    /// <summary>Area, square metre. Collision cross sections live here.</summary>
    public static Dimension Area { get; } = new(length: 2);

    /// <summary>Volume, L^3.</summary>
    public static Dimension Volume { get; } = new(length: 3);

    /// <summary>Electrical mobility, square metre per volt second.</summary>
    public static Dimension Mobility { get; } = new(mass: -1, time: 2, current: 1);

    /// <summary>Adds exponents, the dimension of a product.</summary>
    /// <param name="left">Left dimension.</param>
    /// <param name="right">Right dimension.</param>
    /// <returns>The product dimension.</returns>
    public static Dimension operator *(Dimension left, Dimension right) => new(
        left._length + right._length,
        left._mass + right._mass,
        left._time + right._time,
        left._current + right._current,
        left._temperature + right._temperature,
        left._amount + right._amount,
        left._luminous + right._luminous);

    /// <summary>Subtracts exponents, the dimension of a quotient.</summary>
    /// <param name="left">Numerator dimension.</param>
    /// <param name="right">Denominator dimension.</param>
    /// <returns>The quotient dimension.</returns>
    public static Dimension operator /(Dimension left, Dimension right) => new(
        left._length - right._length,
        left._mass - right._mass,
        left._time - right._time,
        left._current - right._current,
        left._temperature - right._temperature,
        left._amount - right._amount,
        left._luminous - right._luminous);

    /// <summary>Named alternate for the multiplication operator.</summary>
    /// <param name="left">Left dimension.</param>
    /// <param name="right">Right dimension.</param>
    /// <returns>The product dimension.</returns>
    public static Dimension Multiply(Dimension left, Dimension right) => left * right;

    /// <summary>Named alternate for the division operator.</summary>
    /// <param name="left">Numerator dimension.</param>
    /// <param name="right">Denominator dimension.</param>
    /// <returns>The quotient dimension.</returns>
    public static Dimension Divide(Dimension left, Dimension right) => left / right;

    /// <summary>Raises a dimension to an integer power.</summary>
    /// <param name="value">The dimension.</param>
    /// <param name="exponent">The power.</param>
    /// <returns>The raised dimension.</returns>
    public static Dimension Pow(Dimension value, int exponent) => new(
        value._length * exponent,
        value._mass * exponent,
        value._time * exponent,
        value._current * exponent,
        value._temperature * exponent,
        value._amount * exponent,
        value._luminous * exponent);

    /// <summary>Determines whether two dimensions have identical exponents.</summary>
    /// <param name="left">Left dimension.</param>
    /// <param name="right">Right dimension.</param>
    /// <returns><see langword="true"/> when the exponents match.</returns>
    public static bool operator ==(Dimension left, Dimension right) => left.Equals(right);

    /// <summary>Determines whether two dimensions differ.</summary>
    /// <param name="left">Left dimension.</param>
    /// <param name="right">Right dimension.</param>
    /// <returns><see langword="true"/> when the exponents differ.</returns>
    public static bool operator !=(Dimension left, Dimension right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(Dimension other) =>
        _length == other._length
        && _mass == other._mass
        && _time == other._time
        && _current == other._current
        && _temperature == other._temperature
        && _amount == other._amount
        && _luminous == other._luminous;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Dimension other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        _length, _mass, _time, _current, _temperature, _amount, _luminous);

    /// <summary>
    /// Renders the dimension in SI base symbols, for example <c>m^2 kg s^-2</c>
    /// for energy. Used in error messages, where naming the expected dimension is
    /// most of the recovery instruction required by AGT-3.
    /// </summary>
    /// <returns>A human-readable dimension string, or <c>1</c> when dimensionless.</returns>
    public override string ToString()
    {
        if (IsDimensionless)
        {
            return "1";
        }

        var parts = new List<string>(7);
        Append(parts, "m", _length);
        Append(parts, "kg", _mass);
        Append(parts, "s", _time);
        Append(parts, "A", _current);
        Append(parts, "K", _temperature);
        Append(parts, "mol", _amount);
        Append(parts, "cd", _luminous);
        return string.Join(' ', parts);

        static void Append(List<string> parts, string symbol, sbyte exponent)
        {
            if (exponent == 0)
            {
                return;
            }

            parts.Add(exponent == 1 ? symbol : $"{symbol}^{exponent}");
        }
    }
}
