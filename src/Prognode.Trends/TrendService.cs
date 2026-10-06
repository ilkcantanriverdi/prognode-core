using Prognode.Contracts.Trends;
using Prognode.Core.Tags;

namespace Prognode.Trends;

public sealed class TrendService(
    ITrendRepository repository,
    ITagRepository tags)
{
    private static readonly string[] DefaultColors =
    ["#3ed7e8","#51e6a6","#f6c96b","#ff7180","#a78bfa","#60a5fa","#f472b6","#fb923c"];

    public Task<IReadOnlyList<TrendDefinition>> GetAllAsync(CancellationToken ct = default) =>
        repository.GetAllAsync(ct);

    public Task<TrendDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        repository.GetByIdAsync(id, ct);

    public async Task<TrendDefinition> CreateAsync(
        string? name, IReadOnlyList<Guid>? tagIds, IReadOnlyList<string>? colors,
        int defaultWindowMinutes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var item = await BuildAsync(Guid.NewGuid(), name, tagIds, colors,
            defaultWindowMinutes, now, ct);
        await repository.AddAsync(item, ct);
        return item;
    }

    public async Task<TrendDefinition> UpdateAsync(
        Guid id, string? name, IReadOnlyList<Guid>? tagIds, IReadOnlyList<string>? colors,
        int defaultWindowMinutes, CancellationToken ct = default)
    {
        var existing = await repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Trend was not found.");
        var item = await BuildAsync(id, name, tagIds, colors,
            defaultWindowMinutes, existing.CreatedAt, ct);
        await repository.UpdateAsync(item, ct);
        return item;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await repository.GetByIdAsync(id, ct) is null) return false;
        await repository.DeleteAsync(id, ct);
        return true;
    }

    private async Task<TrendDefinition> BuildAsync(
        Guid id, string? name, IReadOnlyList<Guid>? tagIds, IReadOnlyList<string>? colors,
        int defaultWindowMinutes, DateTimeOffset createdAt, CancellationToken ct)
    {
        var cleanName=(name??"").Trim();
        if (cleanName.Length < 1) throw new ArgumentException("Trend name is required.");
        if (cleanName.Length > 100) throw new ArgumentException("Trend name cannot exceed 100 characters.");

        var ids=(tagIds??[]).Where(x=>x!=Guid.Empty).Distinct().ToArray();
        if (ids.Length is <1 or >8) throw new ArgumentException("A trend must contain between 1 and 8 Tags.");
        foreach (var tagId in ids)
            _ = await tags.GetByIdAsync(tagId, ct) ?? throw new ArgumentException("One or more selected Tags were not found.");

        var windows=new[]{5,10,15,60,480,1440,10080};
        if (!windows.Contains(defaultWindowMinutes)) defaultWindowMinutes=60;

        var cleanColors=new List<string>();
        for (var i=0;i<ids.Length;i++)
        {
            var c=colors is not null && i<colors.Count ? colors[i] : null;
            cleanColors.Add(IsHexColor(c) ? c!.ToLowerInvariant() : DefaultColors[i%DefaultColors.Length]);
        }

        return new TrendDefinition(id,cleanName,ids,cleanColors,defaultWindowMinutes,createdAt,DateTimeOffset.UtcNow);
    }

    private static bool IsHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length==7 && value[0]=='#' && value.Skip(1).All(Uri.IsHexDigit);
}
