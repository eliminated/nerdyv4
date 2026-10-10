using Nerdy.Contracts;

namespace Nerdy.Api.Features.Me;

/// <summary>
/// Guesses a starting role from an email address so onboarding can pre-select it. Only a suggestion: the person
/// confirms or changes it, so heuristics are fine. Everything runs locally on the server; nothing is looked up
/// elsewhere.
/// <list type="bullet">
/// <item>School or university address → Student, or Teacher when the address hints at staff (staff.uni.edu, prof.lee@…).</item>
/// <item>Alumni address → Lifelong learner.</item>
/// <item>Personal provider (gmail.com, outlook.com…) → Lifelong learner.</item>
/// <item>Any other organisation (work) address → Researcher.</item>
/// </list>
/// Students and staff usually share one domain, so the staff hint catches only some teachers.
/// </summary>
public static class RoleSuggester
{
    private static readonly HashSet<string> PersonalDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "icloud.com", "me.com", "mac.com",
        "aol.com", "proton.me", "protonmail.com", "pm.me", "mail.com", "zoho.com", "zohomail.com", "fastmail.com", "hey.com",
        "tutanota.com", "tuta.io", "qq.com", "163.com", "126.com", "naver.com", "daum.net", "web.de", "t-online.de",
        "free.fr", "orange.fr", "laposte.net", "libero.it", "seznam.cz", "wp.pl", "o2.pl", "rediffmail.com", "mail.ru", "inbox.ru",
        "bk.ru", "list.ru", "rambler.ru", "ukr.net"
    };

    /// <summary>Providers that use many country domains (yahoo.co.uk, gmx.de, outlook.fr…).</summary>
    private static readonly string[] PersonalBrands = ["yahoo", "ymail", "hotmail", "outlook", "live", "gmx", "yandex"];

    private static readonly HashSet<string> StaffHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "staff", "faculty", "teacher", "teachers", "prof", "professor", "lecturer", "lecturers", "tutor", "tutors",
        "educator", "educators", "employee", "employees", "emp", "academic", "academics"
    };

    private static readonly HashSet<string> AlumniHints = new(StringComparer.OrdinalIgnoreCase) { "alumni", "alum", "alumnus", "alumnae" };

    /// <summary>Words in a domain name that suggest a school or university (uni-heidelberg.de, harrowschool.org…).</summary>
    private static readonly string[] AcademicWords = ["uni-", "univ", "universit", "universidad", "hochschule", "fh-", "polytechn", "college", "school", "schule"];

    /// <summary>Reserved and test domains never get a suggestion (RFC 2606 / 6761).</summary>
    private static readonly string[] ReservedTlds = ["test", "example", "invalid", "localhost", "local"];

    public static RoleSuggestionDto? Suggest(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return null;
        var local = email[..at].Trim().ToLowerInvariant();
        var domain = email[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2 || ReservedTlds.Contains(labels[^1]) || domain is "example.com" or "example.net" or "example.org") return null;

        if (IsPersonal(domain, labels)) return Make(UserRole.Learner, RoleSuggestionSource.Personal, domain);
        if (!IsAcademic(labels)) return Make(UserRole.Researcher, RoleSuggestionSource.Organization, domain);

        // Subdomain labels (staff.uni.edu) and parts of the mailbox name (prof.lee@, j.doe.staff@).
        var hints = labels[..^1].Concat(local.Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)).ToList();
        if (hints.Any(AlumniHints.Contains)) return Make(UserRole.Learner, RoleSuggestionSource.Alumni, domain);
        if (hints.Any(StaffHints.Contains)) return Make(UserRole.Teacher, RoleSuggestionSource.SchoolStaff, domain);
        return Make(UserRole.Student, RoleSuggestionSource.School, domain);
    }

    private static bool IsPersonal(string domain, string[] labels) =>
        PersonalDomains.Contains(domain) || (labels.Length is 2 or 3 && PersonalBrands.Contains(labels[0]));

    private static bool IsAcademic(string[] labels)
    {
        // .edu anywhere (stanford.edu, unimelb.edu.au, tsinghua.edu.cn), K-12 districts (lausd.k12.ca.us),
        // academic second-level domains (ox.ac.uk, kaist.ac.kr) and UK/other school domains (x.sch.uk).
        if (labels.Contains("edu") || labels.Contains("k12")) return true;
        if (labels.Length >= 3 && labels[^1].Length == 2 && labels[^2] is "ac" or "sch" or "school") return true;
        // Otherwise look at the organisation's own name (the label before the public suffix).
        var name = labels.Length >= 3 && labels[^1].Length == 2 && labels[^2].Length <= 3 ? labels[^3] : labels[^2];
        return AcademicWords.Any(w => name.Contains(w, StringComparison.Ordinal));
    }

    private static RoleSuggestionDto Make(UserRole role, RoleSuggestionSource source, string domain) =>
        new() { Role = role, Source = source, Domain = domain };
}
