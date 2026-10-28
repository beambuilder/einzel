using Einzel.Core.Errors;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>
/// Evaluates the arithmetic expressions that make a model parametric.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 9: "Every placement is a parametric expression, never a baked
/// number: named parameters with units and bounds; derived parameters as
/// sandboxed expressions." The reason is section 13's tolerance work — bake a
/// design down to numbers and "move this stripe 50 microns and re-solve" stops
/// being sayable, which is the whole point of the sweep machinery.
/// </para>
/// <para>
/// Expressions evaluate over <see cref="Quantity"/>, not over doubles, so
/// dimensions propagate through the arithmetic and a term that adds a length to a
/// voltage fails where it is written rather than thousands of steps later. That
/// makes the evaluator a unit checker as much as a calculator.
/// </para>
/// <para>
/// Sandboxed in the sense that matters here: the grammar is arithmetic and a
/// handful of functions, there is no way to name anything but a parameter, and
/// evaluation cannot loop. It is not a scripting language and is not meant to
/// become one — spec section 5 is explicit that extension goes through Python
/// behind a process boundary, not through the model format.
/// </para>
/// </remarks>
public static class ExpressionEvaluator
{
    /// <summary>Evaluates an expression against a set of resolved parameters.</summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="parameters">Parameters already resolved to quantities.</param>
    /// <param name="path">JSON Pointer to the expression, for the error object.</param>
    /// <returns>The resulting quantity, with its dimension derived from the operands.</returns>
    /// <exception cref="EinzelException">The expression is malformed, names an unknown parameter, or is dimensionally inconsistent.</exception>
    public static Quantity Evaluate(
        string expression, IReadOnlyDictionary<string, Quantity> parameters, string path)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(expression))
        {
            throw Failure(path, "an expression may not be empty", "supply an expression, for example 'depth * 0.5'");
        }

        var parser = new Parser(expression, parameters, path, null);
        var value = parser.ParseExpression();
        parser.ExpectEnd();

        return value.Value;
    }

    /// <summary>
    /// Compiles an expression of one free variable into a function, with every parameter
    /// bound as a constant.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="parameters">Parameters already resolved to quantities, bound once here.</param>
    /// <param name="variable">The name of the free variable, which is dimensionless.</param>
    /// <param name="path">JSON Pointer to the expression, for the error object.</param>
    /// <returns>The expression as a function of the variable, dimensionless in and out.</returns>
    /// <exception cref="EinzelException">
    /// The expression is malformed, names anything but the variable and a declared
    /// parameter, is dimensionally inconsistent, has a dimensioned result, or a parameter
    /// carries the variable's name.
    /// </exception>
    /// <remarks>
    /// <para>
    /// What a waveform written as text needs: the integrator asks for the drive at every
    /// step, and <see cref="Evaluate"/> walks the text and checks dimensions each time it
    /// is called. Here the text is parsed once, by the same parser, and the dimension
    /// check runs once on the way through; what comes out is a tree of closures over
    /// doubles, with every sub-expression that does not read the variable folded to the
    /// number <see cref="Evaluate"/> would have given it. The closures capture nothing
    /// mutable, so one compiled function serves any number of threads.
    /// </para>
    /// <para>
    /// <strong>The same arithmetic, to the bit.</strong> Each operator and function applies
    /// the same double operation the interpreter applies to a quantity's SI value, in the
    /// same order, so the compiled function and <see cref="Evaluate"/> with the variable
    /// bound as a parameter agree exactly wherever <see cref="Evaluate"/> succeeds. Where
    /// it would refuse a value - a logarithm of a non-positive number, an arcsine outside
    /// [-1, 1], a modulus by zero - the refusal depends on the variable and cannot run at
    /// compile time, so the function returns what the arithmetic gives (NaN or an
    /// infinity) and the caller samples it and refuses a non-finite result.
    /// </para>
    /// </remarks>
    public static Func<double, double> Compile(
        string expression, IReadOnlyDictionary<string, Quantity> parameters, string variable, string path)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(variable);

        if (string.IsNullOrWhiteSpace(expression))
        {
            throw Failure(
                path,
                "an expression may not be empty",
                $"supply an expression of '{variable}', for example '1 - 2*floor(2*{variable})'");
        }

        // Refused rather than resolved one way or the other: either reading makes a
        // document whose meaning depends on a rule nobody wrote down in it.
        if (parameters.ContainsKey(variable))
        {
            throw Failure(
                path,
                $"the variable '{variable}' shadows the parameter of the same name",
                $"rename the parameter; inside this expression '{variable}' is the free variable");
        }

        var parser = new Parser(expression, parameters, path, variable);
        var node = parser.ParseExpression();
        parser.ExpectEnd();

        if (!node.Value.Dimension.IsDimensionless)
        {
            throw Failure(
                path,
                $"the expression must be dimensionless, but has dimension {node.Value.Dimension}",
                "divide by a parameter of the same dimension, for example volts / amplitude");
        }

        return node.Function;
    }

    /// <summary>The parameter names an expression refers to.</summary>
    /// <param name="expression">The expression text.</param>
    /// <returns>Every identifier that is not a known function name.</returns>
    /// <remarks>
    /// Used to order derived parameters before evaluating them, and to detect a
    /// cycle rather than recursing into one.
    /// </remarks>
    public static IReadOnlyList<string> References(string expression)
    {
        var names = new List<string>();

        if (string.IsNullOrEmpty(expression))
        {
            return names;
        }

        for (var i = 0; i < expression.Length; i++)
        {
            if (!char.IsLetter(expression[i]) && expression[i] != '_')
            {
                continue;
            }

            var start = i;

            while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
            {
                i++;
            }

            var name = expression[start..i];

            // A name immediately followed by '(' is a call, not a parameter.
            var next = i;

            while (next < expression.Length && char.IsWhiteSpace(expression[next]))
            {
                next++;
            }

            if (next < expression.Length && expression[next] == '(')
            {
                continue;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static EinzelException Failure(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });

    /// <summary>
    /// A parsed value: its quantity, and for a compiled expression how it depends on the
    /// free variable.
    /// </summary>
    /// <remarks>
    /// <c>Run</c> is null for anything that does not read the variable, so a constant
    /// sub-expression folds to one number at compile time and <see cref="Evaluate"/>,
    /// which has no variable, never builds a delegate. For one that does, <c>Value</c>
    /// carries the dimension the check needs and a placeholder magnitude (zero) that no
    /// result reads. The one place it surfaces is a units error raised on such an operand:
    /// the error's observed value then shows that placeholder, while its dimension, which
    /// is what the error is about, is right.
    /// </remarks>
    private readonly record struct Node(Quantity Value, Func<double, double>? Run)
    {
        public Func<double, double> Function
        {
            get
            {
                if (Run is not null)
                {
                    return Run;
                }

                var constant = Value.SiValue;
                return _ => constant;
            }
        }

        public static Node Combine(Node left, Node right, Quantity value, Func<double, double, double> op)
        {
            if (left.Run is null && right.Run is null)
            {
                return new Node(value, null);
            }

            if (left.Run is null)
            {
                var a = left.Value.SiValue;
                var g = right.Run!;
                return new Node(value, x => op(a, g(x)));
            }

            var f = left.Run;

            if (right.Run is null)
            {
                var b = right.Value.SiValue;
                return new Node(value, x => op(f(x), b));
            }

            var h = right.Run;
            return new Node(value, x => op(f(x), h(x)));
        }
    }

    /// <summary>Recursive-descent parser over the expression grammar.</summary>
    private sealed class Parser(
        string text, IReadOnlyDictionary<string, Quantity> parameters, string path, string? variable)
    {
        private int _position;

        public Node ParseExpression()
        {
            var left = ParseTerm();

            while (true)
            {
                SkipWhitespace();

                if (Match('+'))
                {
                    var right = ParseTerm();
                    left = Node.Combine(left, right, left.Value + right.Value, static (a, b) => a + b);
                }
                else if (Match('-'))
                {
                    var right = ParseTerm();
                    left = Node.Combine(left, right, left.Value - right.Value, static (a, b) => a - b);
                }
                else
                {
                    return left;
                }
            }
        }

        private Node ParseTerm()
        {
            var left = ParseUnary();

            while (true)
            {
                SkipWhitespace();

                if (Match('*'))
                {
                    var right = ParseUnary();
                    left = Node.Combine(left, right, left.Value * right.Value, static (a, b) => a * b);
                }
                else if (Match('/'))
                {
                    var right = ParseUnary();
                    left = Node.Combine(left, right, left.Value / right.Value, static (a, b) => a / b);
                }
                else
                {
                    return left;
                }
            }
        }

        private Node ParseUnary()
        {
            SkipWhitespace();

            if (Match('-'))
            {
                var inner = ParseUnary();
                var run = inner.Run;
                return new Node(-inner.Value, run is null ? null : x => -run(x));
            }

            Match('+');
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            SkipWhitespace();

            if (Match('('))
            {
                var inner = ParseExpression();
                SkipWhitespace();

                if (!Match(')'))
                {
                    throw Failure(path, $"unbalanced parentheses in '{text}'", "add the missing ')'");
                }

                return inner;
            }

            if (_position < text.Length && (char.IsDigit(text[_position]) || text[_position] == '.'))
            {
                return ParseNumber();
            }

            if (_position < text.Length && (char.IsLetter(text[_position]) || text[_position] == '_'))
            {
                return ParseIdentifier();
            }

            throw Failure(
                path,
                $"unexpected character at position {_position} of '{text}'",
                "expressions accept numbers, parameter names, + - * /, parentheses, and "
                + "abs, sqrt, log, floor, mod, min, max, cosPi, sinPi, asinPi");
        }

        private Node ParseNumber()
        {
            var start = _position;

            while (_position < text.Length
                && (char.IsDigit(text[_position]) || text[_position] is '.' or 'e' or 'E'
                    || ((text[_position] is '+' or '-') && _position > start && text[_position - 1] is 'e' or 'E')))
            {
                _position++;
            }

            var literal = text[start.._position];

            return double.TryParse(literal, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? new Node(Quantity.Number(value), null)
                : throw Failure(path, $"'{literal}' is not a number", "use a decimal literal such as 0.5 or 1e-3");
        }

        private Node ParseIdentifier()
        {
            var start = _position;

            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '_'))
            {
                _position++;
            }

            var name = text[start.._position];
            SkipWhitespace();

            if (Match('('))
            {
                return ParseCall(name);
            }

            if (variable is not null && name == variable)
            {
                return new Node(Quantity.Number(0.0), static x => x);
            }

            if (parameters.TryGetValue(name, out var value))
            {
                return new Node(value, null);
            }

            throw Failure(
                path,
                $"'{name}' is not a declared parameter",
                parameters.Count == 0
                    ? "declare it under /parameters"
                    : $"declared parameters are: {string.Join(", ", parameters.Keys.Order(StringComparer.Ordinal))}");
        }

        private Node ParseCall(string name)
        {
            var arguments = new List<Node>();

            // Whether each argument was *written* as a literal zero, which `min` and `max`
            // need in order to accept one against a dimensioned operand. The text and not
            // the value, which is the rule placements already follow and for the reason
            // given there: a value test would make dimensional validity depend on a number,
            // so a document naming a dimensionless parameter that happened to be zero would
            // validate at nominal and fail with a units error partway through a sweep.
            var literalZero = new List<bool>();

            SkipWhitespace();

            if (!Match(')'))
            {
                do
                {
                    SkipWhitespace();
                    var from = _position;
                    arguments.Add(ParseExpression());
                    literalZero.Add(IsWrittenZero(text.AsSpan(from, _position - from)));
                    SkipWhitespace();
                }
                while (Match(','));

                if (!Match(')'))
                {
                    throw Failure(path, $"unbalanced parentheses in call to '{name}'", "add the missing ')'");
                }
            }

            var values = arguments.ConvertAll(argument => argument.Value);
            var constant = arguments.TrueForAll(argument => argument.Run is null);
            var value = Apply(name, values, literalZero, constant);

            return constant ? new Node(value, null) : new Node(value, Lift(name, arguments));
        }

        /// <summary>
        /// A function applied to arguments that read the free variable, as the same double
        /// operation <see cref="Apply"/> performs on their SI values.
        /// </summary>
        /// <remarks>
        /// Reached only after <see cref="Apply"/> has accepted the name, the argument count
        /// and the dimensions. A literal zero that <c>min</c> and <c>max</c> lend a dimension
        /// to is still zero, so the operands here are the arguments as written.
        /// </remarks>
        private static Func<double, double> Lift(string name, List<Node> arguments)
        {
            var f = arguments[0].Function;
            var g = arguments.Count > 1 ? arguments[1].Function : f;

            return name switch
            {
                "abs" => x => Math.Abs(f(x)),
                "sqrt" => x => Math.Sqrt(f(x)),
                "floor" => x => Math.Floor(f(x)),
                "cosPi" => x => double.CosPi(f(x)),
                "sinPi" => x => double.SinPi(f(x)),
                "asinPi" => x => Math.Asin(f(x)) / Math.PI,
                "log" => x => Math.Log(f(x)),
                "mod" => x =>
                {
                    var a = f(x);
                    var b = g(x);
                    return a - (Math.Floor(a / b) * b);
                },
                "min" => x =>
                {
                    var a = f(x);
                    var b = g(x);
                    return a.CompareTo(b) <= 0 ? a : b;
                },
                _ => x =>
                {
                    var a = f(x);
                    var b = g(x);
                    return a.CompareTo(b) >= 0 ? a : b;
                },
            };
        }

        /// <summary>
        /// The two operands of a comparison, with a literal zero given the other's dimension.
        /// </summary>
        /// <remarks>
        /// A clamp is written <c>max(x - start, 0)</c>, and the grammar has no unit literals,
        /// so the zero is dimensionless and would not compare against a length. The same
        /// argument that lets a placement write <c>0</c> for "on axis" applies here, and with
        /// the same restriction: only a zero <em>written</em> as a literal qualifies, never a
        /// parameter that happens to hold zero, because dimensions must not change under a
        /// parameter override. Two literal zeros compare as they always did.
        /// </remarks>
        private static (Quantity Left, Quantity Right) Comparable(
            List<Quantity> arguments, List<bool> literalZero)
        {
            var left = arguments[0];
            var right = arguments[1];

            if (left.Dimension == right.Dimension || literalZero.Count < 2)
            {
                return (left, right);
            }

            if (literalZero[0] && !literalZero[1])
            {
                return (Quantity.Si(0.0, right.Dimension), right);
            }

            if (literalZero[1] && !literalZero[0])
            {
                return (left, Quantity.Si(0.0, left.Dimension));
            }

            return (left, right);
        }

        /// <summary>Whether a span of expression text is a zero literal and nothing else.</summary>
        private static bool IsWrittenZero(ReadOnlySpan<char> written)
        {
            var trimmed = written.Trim();

            if (trimmed.IsEmpty)
            {
                return false;
            }

            foreach (var c in trimmed)
            {
                if (c is not ('0' or '.'))
                {
                    return false;
                }
            }

            // "0", "0.0", "00" and ".0" all qualify; "." alone does not, and neither does
            // anything carrying a digit, a parameter name or an operator.
            return trimmed.IndexOf('0') >= 0;
        }

        /// <remarks>
        /// <paramref name="checkValues"/> is false only for a compiled expression whose
        /// arguments read the variable: a refusal that depends on a value cannot be made
        /// before the value exists, and the caller samples the compiled function instead.
        /// </remarks>
        private Quantity Apply(string name, List<Quantity> arguments, List<bool> literalZero, bool checkValues)
        {
            switch (name)
            {
                case "abs" when arguments.Count == 1:
                    return Quantity.Abs(arguments[0]);

                case "sqrt" when arguments.Count == 1:
                    // Restricted to dimensionless: the square root of a length has
                    // no representation in an integer-exponent dimension system,
                    // and silently dropping the dimension would defeat the check
                    // this evaluator exists to perform.
                    if (!arguments[0].Dimension.IsDimensionless)
                    {
                        throw Failure(
                            path,
                            $"sqrt requires a dimensionless argument, but was given one of dimension "
                            + $"{arguments[0].Dimension}",
                            "form a ratio first, for example sqrt(energy / referenceEnergy)");
                    }

                    return Quantity.Number(Math.Sqrt(arguments[0].SiValue));

                case "floor" when arguments.Count == 1:
                case "mod" when arguments.Count == 2:
                {
                    // Dimensionless only, for the same reason sqrt is: the floor of
                    // a length depends on which unit you take it in, and that is
                    // precisely the ambiguity this evaluator exists to refuse.
                    foreach (var argument in arguments)
                    {
                        if (!argument.Dimension.IsDimensionless)
                        {
                            throw Failure(
                                path,
                                $"{name} requires dimensionless arguments, but was given one of dimension "
                                + $"{argument.Dimension}",
                                "form a ratio first, for example floor(length / pitch)");
                        }
                    }

                    if (name == "floor")
                    {
                        return Quantity.Number(Math.Floor(arguments[0].SiValue));
                    }

                    if (checkValues && arguments[1].SiValue == 0.0)
                    {
                        throw Failure(path, "mod by zero", "use a non-zero divisor");
                    }

                    // Euclidean rather than truncated, so mod(-1, 2) is 1 and an
                    // index counted backwards still alternates the way it should.
                    var quotient = Math.Floor(arguments[0].SiValue / arguments[1].SiValue);

                    return Quantity.Number(arguments[0].SiValue - (quotient * arguments[1].SiValue));
                }

                case "cosPi" when arguments.Count == 1:
                case "sinPi" when arguments.Count == 1:
                {
                    // Dimensionless only, for the same reason sqrt and floor are:
                    // the cosine of a length depends on which unit you take it in.
                    if (!arguments[0].Dimension.IsDimensionless)
                    {
                        throw Failure(
                            path,
                            $"{name} requires a dimensionless argument, but was given one of "
                            + $"dimension {arguments[0].Dimension}",
                            "form a ratio first, for example cosPi(2 * index / poleCount)");
                    }

                    // Half turns, not radians, and the reason is one this engine has
                    // already paid for once. Math.Cos(Math.PI / 2) is 6.1e-17 rather
                    // than zero, so a rod placed at a quarter turn lands a hair off
                    // axis and a multipole built that way carries a spurious dipole
                    // made of round-off. double.CosPi is exact at every quarter turn.
                    // The drive decomposition made the same choice for the same
                    // reason: an antiphase electrode written as Cos of a scaled
                    // argument picks up a quadrature component that is entirely
                    // rounding, and costs a whole extra basis solve to carry it.
                    return Quantity.Number(
                        name == "cosPi"
                            ? double.CosPi(arguments[0].SiValue)
                            : double.SinPi(arguments[0].SiValue));
                }

                case "asinPi" when arguments.Count == 1:
                {
                    // The inverse of sinPi, and in the same units: half turns out, so a
                    // result feeds straight back into cosPi/sinPi with no conversion and
                    // no pi anywhere in a document.
                    //
                    // What needs it is placing something by angle when what is known is a
                    // LENGTH RATIO. A sphere of radius a whose centre sits at radius R
                    // reaches asin(a / R) round the arc - the tangent from the origin - so
                    // a curved electrode built from beads cannot say where its own surface
                    // ends without this. The C-trap's ejection slot is exactly that: the
                    // gap between two bead centres is not the opening between two surfaces,
                    // and a template that confuses them declares a 27 degree slot that is
                    // shut.
                    if (!arguments[0].Dimension.IsDimensionless)
                    {
                        throw Failure(
                            path,
                            "asinPi requires a dimensionless argument, but was given one of "
                            + $"dimension {arguments[0].Dimension}",
                            "form a ratio first, for example asinPi(rodRadius / armRadius)");
                    }

                    // Refused rather than returning NaN. Outside [-1, 1] the ratio was
                    // built the wrong way up or names a body larger than the arm carrying
                    // it, and NaN would travel silently into a placement - which is the
                    // failure this project has already had four times over, a quantity
                    // that is not a number reaching a serialiser long after the mistake.
                    if (checkValues && Math.Abs(arguments[0].SiValue) > 1.0)
                    {
                        throw Failure(
                            path,
                            "asinPi requires an argument between -1 and 1, but was given "
                            + $"{arguments[0].SiValue:G6}",
                            "check the ratio is the right way up: a body cannot subtend an "
                            + "angle at a radius smaller than itself");
                    }

                    // Asin then a division rather than double.AsinPi, which does not
                    // exist. The quarter-turn exactness cosPi and sinPi are chosen for is
                    // not available in this direction and is not needed: nothing is placed
                    // AT the result, it is added to an angle that is placed.
                    return Quantity.Number(Math.Asin(arguments[0].SiValue) / Math.PI);
                }

                case "log" when arguments.Count == 1:
                {
                    // Dimensionless only, for the reason sqrt and cosPi are, and here
                    // the restriction is not merely defensible but the physics: the
                    // logarithm of a length depends on the unit, while every place a
                    // logarithm actually appears in ion optics it is the log of a
                    // ratio of two radii. A coaxial potential is A ln(r/b); a Kingdon
                    // trap's orbital speed is sqrt(qV / (m ln(b/a))). Writing those
                    // requires log and requires it of a ratio.
                    if (!arguments[0].Dimension.IsDimensionless)
                    {
                        throw Failure(
                            path,
                            "log requires a dimensionless argument, but was given one of "
                            + $"dimension {arguments[0].Dimension}",
                            "form a ratio first, for example log(cylinderRadius / wireRadius)");
                    }

                    // Refused rather than returning negative infinity or NaN. A
                    // non-positive argument means the ratio was built the wrong way up
                    // or an electrode has collapsed, and either way the number that
                    // came out of it would be propagated into a geometry.
                    if (checkValues && !(arguments[0].SiValue > 0.0))
                    {
                        throw Failure(
                            path,
                            "log requires a positive argument, but was given "
                            + $"{arguments[0].SiValue:G6}",
                            "check the ratio is the right way up, for example "
                            + "log(cylinderRadius / wireRadius) with the outer radius on top");
                    }

                    // Natural, because that is the one that appears in the physics. A
                    // base-ten logarithm has no place in a field expression and having
                    // both would make `log` the ambiguous spelling.
                    return Quantity.Number(Math.Log(arguments[0].SiValue));
                }

                case "min" when arguments.Count == 2:
                {
                    var (a, b) = Comparable(arguments, literalZero);
                    return a <= b ? a : b;
                }

                case "max" when arguments.Count == 2:
                {
                    var (a, b) = Comparable(arguments, literalZero);
                    return a >= b ? a : b;
                }

                default:
                    throw Failure(
                        path,
                        $"'{name}' is not a known function, or was called with {arguments.Count} arguments",
                        "available: abs(x), sqrt(x), floor(x), mod(a, b), min(a, b), max(a, b), "
                            + "cosPi(x), sinPi(x), asinPi(x) - the last three in half turns, so cosPi(0.5) is "
                            + "exactly zero");
            }
        }

        public void ExpectEnd()
        {
            SkipWhitespace();

            if (_position < text.Length)
            {
                throw Failure(
                    path,
                    $"unexpected trailing text '{text[_position..]}' in '{text}'",
                    "check for a missing operator");
            }
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private bool Match(char expected)
        {
            if (_position >= text.Length || text[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }
    }
}
