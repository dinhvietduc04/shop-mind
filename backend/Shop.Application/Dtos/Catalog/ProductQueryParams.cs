namespace Shop.Application.Dtos;

public record ProductQueryParams(
    string? Search,
    string? Category, // slug or id
    string? Brand,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? InStockOnly,
    string? Sort, // price_asc, price_desc, newest, name_asc, name_desc
    int Page = 1,
    int PageSize = 20,
    bool IncludeInactive = false);
