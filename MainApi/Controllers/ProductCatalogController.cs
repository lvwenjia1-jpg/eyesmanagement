using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/product-catalog")]
[Authorize]
public sealed class ProductCatalogController : ControllerBase
{
    private readonly ProductCatalogRepository _productCatalogRepository;

    public ProductCatalogController(ProductCatalogRepository productCatalogRepository)
    {
        _productCatalogRepository = productCatalogRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductCatalogEntryRecord>>> List(CancellationToken cancellationToken)
    {
        var items = await _productCatalogRepository.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ProductCatalogSyncResponse>> Replace(ReplaceProductCatalogRequest request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "至少需要一条商品编码记录。");
            return ValidationProblem(ModelState);
        }

        var entries = request.Entries.Select((item, index) => new ProductCatalogEntryRecord
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            SpecCode = item.SpecCode,
            Barcode = item.Barcode,
            BaseName = item.BaseName,
            SpecificationToken = item.SpecificationToken,
            ModelToken = item.ModelToken,
            Degree = item.Degree,
            SearchText = item.SearchText,
            SortOrder = index,
            UpdatedAtUtc = DateTime.UtcNow
        }).ToList();

        await _productCatalogRepository.ReplaceAsync(entries, cancellationToken);

        return Ok(new ProductCatalogSyncResponse
        {
            EntryCount = entries
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                .Select(item => item.ProductCode.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            UpdatedByLoginName = User.Identity?.Name ?? string.Empty,
            SourceFileName = request.SourceFileName?.Trim() ?? string.Empty,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }
}
