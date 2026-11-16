using System.Globalization;

namespace UtilitiesManager.Updates;

/// <summary>Version X.Y.Z avec suffixe de preversion facultatif (X.Y.Z-beta.1), comparable.</summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    private SemVersion(int major, int minor, int patch, string prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>Suffixe apres le tiret, vide pour une version publiee.</summary>
    public string Prerelease { get; }

    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Version d'essai : troisieme chiffre non nul (X.Y.n) ou suffixe de preversion. Livraison : X.Y.</summary>
    public bool IsTestVersion => IsPrerelease || Patch > 0;

    /// <summary>Accepte "1.2.3", "v1.2.3", "1.2", "1.2.3-rc.1", "1.2.3+build".</summary>
    public static bool TryParse(string? text, out SemVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var prerelease = string.Empty;
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = value[(dash + 1)..];
            value = value[..dash];
        }

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (i < 3)
            {
                numbers[i] = number;
            }
        }

        version = new SemVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    public static SemVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Version invalide : {text}");

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        if (result != 0)
        {
            return result;
        }

        // Une version publiee passe devant ses preversions.
        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(SemVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;

    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;

    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;

    private static int ComparePrerelease(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftIsNumber = int.TryParse(left[i], out var leftNumber);
            var rightIsNumber = int.TryParse(right[i], out var rightNumber);
            var result = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : string.CompareOrdinal(left[i], right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}
