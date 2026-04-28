using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/product-catalog")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ProductCatalogController : ControllerBase
{
    private readonly ProductCatalogRepository _productCatalogRepository;

    public ProductCatalogController(ProductCatalogRepository productCatalogRepository)
    {
        _productCatalogRepository = productCatalogRepository;
    }

    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<IReadOnlyList<ProductCatalogEntryRecord>>> List(CancellationToken cancellationToken)
    {
        var items = await _productCatalogRepository.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("query")]
    public async Task<ActionResult<PagedResponse<ProductCatalogEntryRecord>>> Query([FromQuery] QueryProductCatalogRequest request, CancellationToken cancellationToken)
    {
        var result = await _productCatalogRepository.QueryAsync(new ProductCatalogQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Keyword = request.Keyword,
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ModelToken = request.ModelToken,
            SpecificationToken = request.SpecificationToken,
            Degree = request.Degree
        }, cancellationToken);

        return Ok(new PagedResponse<ProductCatalogEntryRecord>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items
        });
    }

    [HttpGet("query-groups")]
    public async Task<ActionResult<PagedResponse<ProductCatalogGroupResponse>>> QueryGroups([FromQuery] QueryProductCatalogRequest request, CancellationToken cancellationToken)
    {
        var result = await _productCatalogRepository.QueryGroupedAsync(new ProductCatalogQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Keyword = request.Keyword,
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ModelToken = request.ModelToken,
            SpecificationToken = request.SpecificationToken,
            Degree = request.Degree
        }, cancellationToken);

        return Ok(new PagedResponse<ProductCatalogGroupResponse>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items.Select(group => new ProductCatalogGroupResponse
            {
                SpecificationToken = group.SpecificationToken,
                ModelToken = group.ModelToken,
                ItemCount = group.ItemCount,
                DegreeCount = group.Degrees.Count,
                UpdatedAtUtc = group.UpdatedAtUtc,
                Degrees = group.Degrees.Select(degree => new ProductCatalogDegreeResponse
                {
                    Id = degree.Id,
                    ProductCode = degree.ProductCode,
                    ProductName = degree.ProductName,
                    SpecCode = degree.SpecCode,
                    Barcode = degree.Barcode,
                    Degree = degree.Degree,
                    IsOutOfStock = degree.IsOutOfStock,
                    UpdatedAtUtc = degree.UpdatedAtUtc
                }).ToList()
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<ProductCatalogImportResponse>> Create(CreateProductCatalogRequest request, CancellationToken cancellationToken)
    {
        var specificationToken = request.SpecificationToken?.Trim() ?? string.Empty;
        var modelToken = request.ModelToken?.Trim() ?? string.Empty;
        var degree = request.Degree?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(specificationToken) ||
            string.IsNullOrWhiteSpace(modelToken) ||
            string.IsNullOrWhiteSpace(degree))
        {
            ModelState.AddModelError(nameof(request.SpecificationToken), "周期、型号、度数为必填项。");
            return ValidationProblem(ModelState);
        }

        var updatedAtUtc = DateTime.UtcNow;
        var result = await _productCatalogRepository.ImportAsync(
            new[]
            {
                BuildEntry(request, sortOrder: 0, updatedAtUtc)
            },
            cancellationToken);

        return Ok(BuildImportResponse(result, sourceFileName: "manual-create", updatedAtUtc));
    }

    [HttpPost("import")]
    public async Task<ActionResult<ProductCatalogImportResponse>> Import(ImportProductCatalogRequest request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "At least one catalog item is required.");
            return ValidationProblem(ModelState);
        }

        var updatedAtUtc = DateTime.UtcNow;
        var entries = request.Entries
            .Where(item => IsMeaningfulRequest(item.ProductCode, item.SpecificationToken, item.ModelToken))
            .Select((item, index) => BuildEntry(item, index, updatedAtUtc))
            .ToList();

        if (entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "No valid catalog entries were found.");
            return ValidationProblem(ModelState);
        }

        var result = await _productCatalogRepository.ImportAsync(entries, cancellationToken);
        return Ok(BuildImportResponse(result, request.SourceFileName, updatedAtUtc));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var deleted = await _productCatalogRepository.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPatch("{id:long}/out-of-stock")]
    public async Task<IActionResult> UpdateOutOfStock(long id, UpdateProductCatalogOutOfStockRequest request, CancellationToken cancellationToken)
    {
        var updatedAtUtc = DateTime.UtcNow;
        var updated = await _productCatalogRepository.UpdateOutOfStockAsync(id, request.IsOutOfStock, updatedAtUtc, cancellationToken);
        if (!updated)
        {
            return NotFound();
        }

        return Ok(new
        {
            id,
            isOutOfStock = request.IsOutOfStock,
            updatedAtUtc
        });
    }

    [HttpPut]
    public ActionResult Replace()
    {
        return Conflict(new
        {
            message = "Full replacement is disabled. Use incremental import and single-item delete."
        });
    }

    private static ProductCatalogEntryRecord BuildEntry(CreateProductCatalogRequest request, int sortOrder, DateTime updatedAtUtc)
    {
        return ProductCatalogEntryBuilder.Build(new ProductCatalogBuildInput
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            SpecCode = request.SpecCode,
            Barcode = request.Barcode,
            SpecificationToken = request.SpecificationToken,
            ModelToken = request.ModelToken,
            Degree = request.Degree,
            IsOutOfStock = request.IsOutOfStock
        }, sortOrder, updatedAtUtc);
    }

    private static ProductCatalogEntryRecord BuildEntry(ImportProductCatalogItemRequest request, int sortOrder, DateTime updatedAtUtc)
    {
        return ProductCatalogEntryBuilder.Build(new ProductCatalogBuildInput
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            SpecCode = request.SpecCode,
            Barcode = request.Barcode,
            SpecificationToken = request.SpecificationToken,
            ModelToken = request.ModelToken,
            Degree = request.Degree,
            IsOutOfStock = request.IsOutOfStock
        }, sortOrder, updatedAtUtc);
    }

    private static bool IsMeaningfulRequest(string? productCode, string? specificationToken, string? modelToken)
    {
        return !string.IsNullOrWhiteSpace(productCode) ||
               (!string.IsNullOrWhiteSpace(specificationToken) && !string.IsNullOrWhiteSpace(modelToken));
    }

    private static ProductCatalogImportResponse BuildImportResponse(ProductCatalogImportResult result, string? sourceFileName, DateTime updatedAtUtc)
    {
        return new ProductCatalogImportResponse
        {
            AddedCount = result.AddedCount,
            UpdatedCount = result.UpdatedCount,
            SkippedCount = result.SkippedCount,
            TotalCount = result.TotalCount,
            SourceFileName = sourceFileName?.Trim() ?? string.Empty,
            UpdatedAtUtc = updatedAtUtc,
            Message = $"Import completed: added {result.AddedCount}, updated {result.UpdatedCount}, skipped {result.SkippedCount}."
        };
    }
}
