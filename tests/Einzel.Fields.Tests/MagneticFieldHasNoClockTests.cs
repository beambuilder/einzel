using Einzel.Fields.Magnetic;

namespace Einzel.Fields.Tests;

/// <summary>
/// A magnetic field does not depend on time, by its interface. <see cref="TimeShiftedField.Shift"/>
/// rests on that: it shifts the electric part of a field and hands the magnet on as it is.
/// </summary>
/// <remarks>
/// Nothing else would notice the day that stops being true. A magnetic element that varies
/// in time, added behind a new member or a new interface, would compile, and an ion born
/// late (<c>einzel fly</c>, a row with a birth time) would fly B as it stands at t = 0. So
/// this fails first, and says where to look.
/// </remarks>
public sealed class MagneticFieldHasNoClockTests
{
    private const string WhatToDo =
        "TimeShiftedField.Shift hands the magnetic part on unshifted because B has no clock; "
        + "if it has one now, Shift must shift it too (or refuse), and this test must say how";

    /// <summary>No member of <see cref="IMagneticField"/> takes an instant: none has a scalar parameter.</summary>
    [Fact]
    public void NoMemberOfTheMagneticInterfaceTakesATime()
    {
        var timed = typeof(IMagneticField).GetMethods()
            .Where(m => m.GetParameters().Any(p =>
                (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType) == typeof(double)))
            .Select(m => m.ToString())
            .ToList();

        Assert.True(timed.Count == 0, $"{string.Join("; ", timed)}: {WhatToDo}");
    }

    /// <summary>
    /// No magnetic field is also a time-varying one, except the wrapper whose electric part
    /// is the time-varying thing, and no interface extends the magnetic one.
    /// </summary>
    [Fact]
    public void NoMagneticFieldIsTimeVaryingButTheWrapperAroundADrive()
    {
        var types = typeof(IMagneticField).Assembly.GetTypes();

        var timed = types
            .Where(t => t.IsClass && typeof(IMagneticField).IsAssignableFrom(t) && typeof(ITimeVaryingField).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            timed.SequenceEqual([nameof(DrivenMagnetizedField)]), $"{string.Join(", ", timed)}: {WhatToDo}");

        var extended = types
            .Where(t => t.IsInterface && t != typeof(IMagneticField) && typeof(IMagneticField).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(extended.Count == 0, $"{string.Join(", ", extended)} extends IMagneticField: {WhatToDo}");
    }
}
