using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CustomFormService.Api.Validation;

// Exact decimal comparison for Number Config, within PostgreSQL JSONB numeric's
// storage bounds. Never converts through decimal/double or rounds the JSON token.
internal readonly record struct DefinitionNumber(int Sign, string Digits, int Exponent) : IComparable<DefinitionNumber>
{
    // PostgreSQL 17 (verified against 17.11): unconstrained numeric storage limits
    // and numeric.c set_var_from_str's raw-exponent guard, also used by JSONB.
    // https://www.postgresql.org/docs/17/datatype-numeric.html
    // https://www.postgresql.org/docs/17/datatype-json.html
    // https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/utils/adt/numeric.c
    private const int PostgreSqlMaxIntegralDigits = 131072;
    private const int PostgreSqlMaxFractionalDigits = 16383;
    private const int PostgreSqlMaxRawExponent = int.MaxValue / 2;

    public static bool TryRead(JsonElement element, out DefinitionNumber number)
    {
        number = default;
        if (element.ValueKind != JsonValueKind.Number) return false;
        var token = element.GetRawText();
        var negative = token[0] == '-';
        var exponentIndex = token.IndexOfAny(['e', 'E']);
        var exponent = exponentIndex < 0 ? BigInteger.Zero
            : BigInteger.Parse(token[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        // PostgreSQL's numeric input parser caps the raw exponent even for zero.
        // Checking only the nonzero storage weight would let these tokens reach 500.
        if (exponent < -PostgreSqlMaxRawExponent || exponent > PostgreSqlMaxRawExponent) return false;
        var mantissa = token[(negative ? 1 : 0)..(exponentIndex < 0 ? token.Length : exponentIndex)];
        var dot = mantissa.IndexOf('.');
        var fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        if (fractionDigits - exponent > PostgreSqlMaxFractionalDigits) return false;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        exponent -= fractionDigits;
        if (digits.Length == 0)
        {
            number = new(0, "0", 0);
            return true;
        }
        if (digits.Length + exponent > PostgreSqlMaxIntegralDigits) return false;
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;
        number = new(negative ? -1 : 1, trimmed, (int)exponent);
        return true;
    }

    public int CompareTo(DefinitionNumber other)
    {
        if (Sign != other.Sign) return Sign.CompareTo(other.Sign);
        if (Sign == 0) return 0;
        var magnitude = (Digits.Length + Exponent).CompareTo(other.Digits.Length + other.Exponent);
        if (magnitude != 0) return Sign * magnitude;
        for (var i = 0; i < Math.Max(Digits.Length, other.Digits.Length); i++)
        {
            var left = i < Digits.Length ? Digits[i] : '0';
            var right = i < other.Digits.Length ? other.Digits[i] : '0';
            if (left != right) return Sign * left.CompareTo(right);
        }
        return 0;
    }
}
