namespace Shop.Application.Validation;

public static class SlugHelper
{
    public static string Slugify(string input)
    {
        var slug = input.ToLowerInvariant().Trim();
        slug = string.Concat(slug.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
