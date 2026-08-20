using System.Globalization;

namespace Dock.Core.Models;

/// <summary>
/// A calculator small enough to live in a text box.
///
/// Deliberately not a general expression language. It exists so that a line typed into the island's
/// capture box can turn out to be a sum, and the thing that matters most about it is what it
/// *refuses*: anything it cannot consume completely, and anything without an operator in it. A task
/// list where "buy 2 x 4 timber" silently became the number 8 would be a worse feature than having
/// no calculator at all.
///
/// Recursive descent over a single string, no allocation beyond the result. Precedence is the
/// ordinary one: parentheses, then unary minus and powers, then multiply/divide/modulo, then
/// add/subtract.
/// </summary>
public static class Arithmetic
{
    /// <summary>
    /// Evaluates a whole string, or fails. Fails on anything left over, on an empty expression, and
    /// on an expression with no operator in it -- a bare "25" is far likelier to be the start of a
    /// task than a calculation, and it has no answer worth showing anyway.
    /// </summary>
    public static bool TryEvaluate(string? input, out double result)
    {
        result = 0;

        var text = input ?? string.Empty;
        if (text.Trim().Length == 0 || !HasOperator(text))
            return false;

        var reader = new Reader(text);

        if (!reader.TryExpression(out var value))
            return false;

        reader.SkipSpace();
        if (!reader.AtEnd)
            return false;

        // Division by zero is infinity or NaN rather than an exception in floating point, and
        // neither is an answer anybody wants shown back to them.
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;

        result = value;
        return true;
    }

    /// <summary>
    /// Formats an answer the way a person would write it: no trailing zeroes, and rounded far
    /// enough in to hide the usual floating-point tail (0.1 + 0.2 reads as 0.3, not 0.30000000000000004).
    /// </summary>
    public static string Format(double value) =>
        Math.Round(value, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether there is an operator outside the leading sign. Scanned rather than left to the
    /// parser, because "-5" parses perfectly well and is not a calculation.
    /// </summary>
    private static bool HasOperator(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '+' or '*' or '/' or '%' or '^')
                return true;

            // A minus only counts once something has come before it, or "-5" would qualify.
            if (text[i] == '-' && text[..i].Trim().Length > 0)
                return true;
        }

        return false;
    }

    /// <summary>A cursor over the input. A struct so the whole evaluation allocates nothing.</summary>
    private ref struct Reader(string text)
    {
        private readonly string _text = text;
        private int _index;

        public bool AtEnd => _index >= _text.Length;

        public void SkipSpace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        private char Current => _text[_index];

        private bool Take(char c)
        {
            SkipSpace();
            if (AtEnd || Current != c)
                return false;

            _index++;
            return true;
        }

        public bool TryExpression(out double value)
        {
            if (!TryTerm(out value))
                return false;

            while (true)
            {
                SkipSpace();
                if (AtEnd || Current is not ('+' or '-'))
                    return true;

                var op = Current;
                _index++;

                if (!TryTerm(out var right))
                    return false;

                value = op == '+' ? value + right : value - right;
            }
        }

        private bool TryTerm(out double value)
        {
            if (!TryFactor(out value))
                return false;

            while (true)
            {
                SkipSpace();
                if (AtEnd || Current is not ('*' or '/' or '%'))
                    return true;

                var op = Current;
                _index++;

                if (!TryFactor(out var right))
                    return false;

                if (right == 0 && op is '/' or '%')
                    return false;

                value = op switch
                {
                    '*' => value * right,
                    '/' => value / right,
                    _ => value % right
                };
            }
        }

        private bool TryFactor(out double value)
        {
            value = 0;
            SkipSpace();

            if (AtEnd)
                return false;

            if (Current == '-')
            {
                _index++;
                if (!TryFactor(out var negated))
                    return false;

                value = -negated;
                return true;
            }

            if (!TryPrimary(out value))
                return false;

            // Right-associative, so 2^3^2 is 2^9 rather than 8^2, which is what the notation means
            // everywhere outside a few spreadsheets.
            if (Take('^'))
            {
                if (!TryFactor(out var exponent))
                    return false;

                value = Math.Pow(value, exponent);
            }

            return true;
        }

        private bool TryPrimary(out double value)
        {
            value = 0;
            SkipSpace();

            if (AtEnd)
                return false;

            if (Take('('))
            {
                if (!TryExpression(out value))
                    return false;

                return Take(')');
            }

            var start = _index;
            while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] == '.'))
                _index++;

            if (_index == start)
                return false;

            return double.TryParse(_text[start.._index], NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }
    }
}
