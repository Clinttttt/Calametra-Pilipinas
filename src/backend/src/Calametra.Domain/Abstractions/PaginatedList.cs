namespace Calametra.Domain.Abstractions;

/// <summary>A single page of results plus the metadata needed to request the next.</summary>
public sealed class PaginatedList<T>
{
    private PaginatedList(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public static PaginatedList<T> Create(IReadOnlyList<T> items, int totalCount, int page, int pageSize) =>
        new(items, totalCount, page, pageSize);

    public static PaginatedList<T> Empty(int page, int pageSize) =>
        new([], 0, page, pageSize);
}
