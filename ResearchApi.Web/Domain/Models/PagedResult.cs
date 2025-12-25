namespace ResearchApi.Domain;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Skip,
    int Take,
    int Total)
{
    public int PageSize => Take;
    public int Page => Take <= 0 ? 1 : (Skip / Take) + 1;
    public int TotalPages => Take <= 0 ? 0 : (int)Math.Ceiling(Total / (double)Take);
}