using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Model;

/// <summary>
/// The compiled path of the expression evaluator, which a waveform written as text runs
/// through at every integrator step.
/// </summary>
/// <remarks>
/// The claim is that compiling changes when the work is done and nothing else: the
/// function it returns gives what <see cref="ExpressionEvaluator.Evaluate"/> gives with
/// the variable bound as a parameter, to the bit, and it refuses what the interpreter
/// refuses with the same words.
/// </remarks>
public sealed class ExpressionCompileTests
{
    private static readonly Dictionary<string, Quantity> Parameters = new(StringComparer.Ordinal)
    {
        ["volts"] = Quantity.From(4800.0, "V"),
        ["ratio"] = Quantity.Number(0.35),
        ["rise"] = Quantity.Number(0.1),
    };

    /// <summary>Every operator and function, each reading the variable.</summary>
    public static TheoryData<string> Expressions =>
    [
        "1 - 2*floor(2*p)",
        "-p",
        "0.5 - -cosPi(p) * 2",
        "(p - 0.5) / 0.25 * ratio",
        "+p * volts / volts",
        "abs(p - 0.5)",
        "sqrt(p)",
        "floor(p * 10) / 10",
        "mod(7*p, 1)",
        "mod(-3*p, 0.3)",
        "mod(5, p + 0.25)",
        "cosPi(2*p)",
        "sinPi(2*p + ratio)",
        "asinPi(2*p - 1)",
        "log(p + 0.1)",
        "min(p, 0.4)",
        "max(p - 0.5, 0)",
        "max(-1, min(1, (1 - 4*abs(p - 0.5)) / (1 - 2*rise)))",
        "max(-0.7, min(0.7, sinPi(2*p))) / 0.7",
        "1 - 2*floor(mod(3*p, 1) + 1 - 0.2)",
        "p / (p + 1) - sinPi(p)",
        "max(p, 0.6) - max(0.3, cosPi(p))",
    ];

    [Theory]
    [MemberData(nameof(Expressions))]
    public void TheCompiledFunctionAgreesWithTheInterpreterToTheBit(string expression)
    {
        var compiled = ExpressionEvaluator.Compile(expression, Parameters, "p", "/test");
        var bound = new Dictionary<string, Quantity>(Parameters, StringComparer.Ordinal);

        for (var k = 0; k < 1000; k++)
        {
            // Uniform phases plus thirds and sevenths, so not every sample is a short binary fraction.
            foreach (var phase in new[] { k / 1000.0, k / 3001.0, (k + 0.5) / 7007.0 })
            {
                bound["p"] = Quantity.Number(phase);
                var interpreted = ExpressionEvaluator.Evaluate(expression, bound, "/test").SiValue;

                Assert.Equal(BitConverter.DoubleToInt64Bits(interpreted), BitConverter.DoubleToInt64Bits(compiled(phase)));
            }
        }
    }

    [Fact]
    public void AConstantExpressionCompilesToTheNumberTheInterpreterGives()
    {
        var compiled = ExpressionEvaluator.Compile("sinPi(ratio) * 3 - sqrt(2)", Parameters, "p", "/test");
        var interpreted = ExpressionEvaluator.Evaluate("sinPi(ratio) * 3 - sqrt(2)", Parameters, "/test").SiValue;

        Assert.Equal(interpreted, compiled(0.123));
        Assert.Equal(interpreted, compiled(0.987));
    }

    [Fact]
    public void ANameThatIsNeitherTheVariableNorAParameterIsRefusedAsTheInterpreterRefusesIt()
    {
        var compiled = Assert.Throws<EinzelException>(
            () => ExpressionEvaluator.Compile("1 - q * p", Parameters, "p", "/fields/0/waveform"));
        var interpreted = Assert.Throws<EinzelException>(
            () => ExpressionEvaluator.Evaluate("1 - q * 0.5", Parameters, "/fields/0/waveform"));

        Assert.Equal(ErrorCodes.SchemaInvalid, compiled.Error.Code);
        Assert.Equal("/fields/0/waveform", compiled.Error.Path);
        Assert.Equal(interpreted.Error.Constraint, compiled.Error.Constraint);
        Assert.Equal(interpreted.Error.Suggestion, compiled.Error.Suggestion);
    }

    [Theory]
    [InlineData("1 - (p")]
    [InlineData("1 - 2 p")]
    [InlineData("1 - $p")]
    [InlineData("cube(p)")]
    [InlineData("")]
    public void MalformedTextIsRefusedWithTheInterpretersWords(string expression)
    {
        var bound = new Dictionary<string, Quantity>(Parameters, StringComparer.Ordinal) { ["p"] = Quantity.Number(0.5) };
        var compiled = Assert.Throws<EinzelException>(() => ExpressionEvaluator.Compile(expression, Parameters, "p", "/x"));
        var interpreted = Assert.Throws<EinzelException>(() => ExpressionEvaluator.Evaluate(expression, bound, "/x"));

        Assert.Equal(interpreted.Error.Constraint, compiled.Error.Constraint);
    }

    [Fact]
    public void ARangeOrDivisorRefusalOnTheVariableWaitsForAValue()
    {
        // At compile time the variable has no value, so asinPi and mod must not judge one:
        // both compile, and the caller's sampling sees NaN where the interpreter would refuse.
        var arcsine = ExpressionEvaluator.Compile("asinPi(p - 2)", Parameters, "p", "/w");
        var modulus = ExpressionEvaluator.Compile("mod(1, p)", Parameters, "p", "/w");

        Assert.True(double.IsNaN(arcsine(0.5)));
        Assert.Equal(-1.0 / 6.0, arcsine(1.5), 1e-15);
        Assert.True(double.IsNaN(modulus(0.0)));
        Assert.Equal(0.1, modulus(0.3), 1e-15);
    }

    [Fact]
    public void ADimensionedResultIsRefusedOnce()
    {
        var failure = Assert.Throws<EinzelException>(
            () => ExpressionEvaluator.Compile("volts * p", Parameters, "p", "/w"));

        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal("/w", failure.Error.Path);
        Assert.Contains("dimensionless", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInconsistentSumIsRefusedAtCompileEvenThoughItReadsTheVariable()
    {
        Assert.Throws<EinzelException>(() => ExpressionEvaluator.Compile("p + volts", Parameters, "p", "/w"));
    }

    [Fact]
    public void AParameterNamedLikeTheVariableIsRefused()
    {
        var shadowing = new Dictionary<string, Quantity>(Parameters, StringComparer.Ordinal) { ["p"] = Quantity.Number(1.0) };

        var failure = Assert.Throws<EinzelException>(
            () => ExpressionEvaluator.Compile("1 - p", shadowing, "p", "/w"));

        Assert.Contains("shadows", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueRefusalThatDependsOnTheVariableIsLeftToTheCallerAsANonFiniteResult()
    {
        // log refuses a non-positive argument when it can see the value; compiled, the
        // value does not exist yet, and the caller samples for exactly this.
        var compiled = ExpressionEvaluator.Compile("log(p - 0.5)", Parameters, "p", "/w");

        Assert.True(double.IsNaN(compiled(0.25)));
        Assert.Equal(double.NegativeInfinity, compiled(0.5));
        Assert.Throws<EinzelException>(() => ExpressionEvaluator.Compile("log(0 - 0.5) * p", Parameters, "p", "/w"));
    }
}
