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
    private readonly WearPeriodSettingsRepository _wearPeriodSettingsRepository;
    private readonly WearPeriodNormalizationService _wearPeriodNormalizationService;

    public ProductCatalogController(
        ProductCatalogRepository productCatalogRepository,
        WearPeriodSettingsRepository wearPeriodSettingsRepository,
        WearPeriodNormalizationService wearPeriodNormalizationService)
    {
        _productCatalogRepository = productCatalogRepository;
        _wearPeriodSettingsRepository = wearPeriodSettingsRepository;
        _wearPeriodNormalizationService = wearPeriodNormalizationService;
    }

    [HttpGet]
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
            Degree = request.Degree,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection
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
            Degree = request.Degree,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection
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
        var settings = await _wearPeriodSettingsRepository.GetAsync(cancellationToken);
        var normalizedTokens = _wearPeriodNormalizationService.NormalizeCatalogTokens(
            request.SpecificationToken,
            request.ModelToken,
            request.ProductCode,
            request.ProductName,
            settings);

        request.SpecificationToken = normalizedTokens.SpecificationToken;
        request.ModelToken = normalizedTokens.ModelToken;

        if (string.IsNullOrWhiteSpace(request.SpecificationToken) ||
            string.IsNullOrWhiteSpace(request.ModelToken) ||
            string.IsNullOrWhiteSpace(request.Degree))
        {
            ModelState.AddModelError(nameof(request.SpecificationToken), "周期、型号、度数为必填项。");
            return ValidationProblem(ModelState);
        }

        var updatedAtUtc = DateTime.UtcNow;
        var result = await _productCatalogRepository.ImportAsync(
            new[]
            {
                BuildEntry(request, 0, updatedAtUtc, settings)
            },
            ProductCatalogImportModes.Incremental,
            cancellationToken);

        return Ok(BuildImportResponse(result, "manual-create", ProductCatalogImportModes.Incremental, updatedAtUtc));
    }

    [HttpPost("import")]
    public async Task<ActionResult<ProductCatalogImportResponse>> Import(ImportProductCatalogRequest request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "至少需要一条商品编码数据。");
            return ValidationProblem(ModelState);
        }

        var importMode = NormalizeImportMode(request.ImportMode);
        if (!IsKnownImportMode(importMode))
        {
            ModelState.AddModelError(nameof(request.ImportMode), "导入模式无效。");
            return ValidationProblem(ModelState);
        }

        var updatedAtUtc = DateTime.UtcNow;
        var settings = await _wearPeriodSettingsRepository.GetAsync(cancellationToken);
        var entries = request.Entries
            .Where(item => IsMeaningfulRequest(item.ProductCode, item.SpecificationToken, item.ModelToken))
            .Select((item, index) => BuildEntry(item, index, updatedAtUtc, importMode, settings))
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .ToList();

        if (entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "没有识别到有效的商品编码数据。");
            return ValidationProblem(ModelState);
        }

        var result = await _productCatalogRepository.ImportAsync(entries, importMode, cancellationToken);
        return Ok(BuildImportResponse(result, request.SourceFileName, importMode, updatedAtUtc));
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

    [HttpPatch("group-specification")]
    public async Task<IActionResult> UpdateGroupSpecification(UpdateProductCatalogGroupSpecificationRequest request, CancellationToken cancellationToken)
    {
        var sourceSpecificationToken = NormalizeGroupToken(request.SpecificationToken);
        var modelToken = NormalizeGroupToken(request.ModelToken);
        var targetSpecificationToken = NormalizeGroupToken(request.TargetSpecificationToken);
        if (string.IsNullOrWhiteSpace(modelToken) || string.IsNullOrWhiteSpace(targetSpecificationToken))
        {
            ModelState.AddModelError(nameof(request.TargetSpecificationToken), "型号和目标周期不能为空。");
            return ValidationProblem(ModelState);
        }

        var settings = await _wearPeriodSettingsRepository.GetAsync(cancellationToken);
        targetSpecificationToken = _wearPeriodNormalizationService.NormalizeWearPeriod(targetSpecificationToken, settings);

        var updatedAtUtc = DateTime.UtcNow;
        var updated = await _productCatalogRepository.UpdateGroupSpecificationTokenAsync(
            sourceSpecificationToken,
            modelToken,
            targetSpecificationToken,
            updatedAtUtc,
            cancellationToken);
        if (!updated)
        {
            return NotFound();
        }

        return Ok(new
        {
            specificationToken = targetSpecificationToken,
            modelToken,
            updatedAtUtc
        });
    }

    [HttpDelete("group")]
    public async Task<IActionResult> DeleteGroup(
        [FromQuery] string? specificationToken,
        [FromQuery] string? modelToken,
        CancellationToken cancellationToken)
    {
        var normalizedSpecificationToken = NormalizeGroupToken(specificationToken);
        var normalizedModelToken = NormalizeGroupToken(modelToken);
        if (string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            ModelState.AddModelError(nameof(modelToken), "型号不能为空。");
            return ValidationProblem(ModelState);
        }

        var deleted = await _productCatalogRepository.DeleteGroupAsync(
            normalizedSpecificationToken,
            normalizedModelToken,
            cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPut]
    public ActionResult Replace()
    {
        return Conflict(new
        {
            message = "商品编码全量替换已停用，请使用覆盖导入或缺货/到货导入。"
        });
    }

    private ProductCatalogEntryRecord BuildEntry(
        CreateProductCatalogRequest request,
        int sortOrder,
        DateTime updatedAtUtc,
        WearPeriodSettingsResponse settings)
    {
        var normalizedTokens = _wearPeriodNormalizationService.NormalizeCatalogTokens(
            request.SpecificationToken,
            request.ModelToken,
            request.ProductCode,
            request.ProductName,
            settings);

        return ProductCatalogEntryBuilder.Build(new ProductCatalogBuildInput
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            SpecCode = request.SpecCode,
            Barcode = request.Barcode,
            SpecificationToken = normalizedTokens.SpecificationToken,
            ModelToken = normalizedTokens.ModelToken,
            Degree = request.Degree,
            IsOutOfStock = request.IsOutOfStock
        }, sortOrder, updatedAtUtc);
    }

    private ProductCatalogEntryRecord BuildEntry(
        ImportProductCatalogItemRequest request,
        int sortOrder,
        DateTime updatedAtUtc,
        string importMode,
        WearPeriodSettingsResponse settings)
    {
        var isOutOfStock = importMode switch
        {
            ProductCatalogImportModes.StockOut => true,
            ProductCatalogImportModes.StockIn => false,
            _ => request.IsOutOfStock
        };
        var normalizedTokens = _wearPeriodNormalizationService.NormalizeCatalogTokens(
            request.SpecificationToken,
            request.ModelToken,
            request.ProductCode,
            request.ProductName,
            settings);

        return ProductCatalogEntryBuilder.Build(new ProductCatalogBuildInput
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            SpecCode = request.SpecCode,
            Barcode = request.Barcode,
            SpecificationToken = normalizedTokens.SpecificationToken,
            ModelToken = normalizedTokens.ModelToken,
            Degree = request.Degree,
            IsOutOfStock = isOutOfStock
        }, sortOrder, updatedAtUtc);
    }

    private static bool IsMeaningfulRequest(string? productCode, string? specificationToken, string? modelToken)
    {
        return !string.IsNullOrWhiteSpace(productCode) ||
               (!string.IsNullOrWhiteSpace(specificationToken) && !string.IsNullOrWhiteSpace(modelToken));
    }

    private static ProductCatalogImportResponse BuildImportResponse(
        ProductCatalogImportResult result,
        string? sourceFileName,
        string importMode,
        DateTime updatedAtUtc)
    {
        return new ProductCatalogImportResponse
        {
            AddedCount = result.AddedCount,
            UpdatedCount = result.UpdatedCount,
            SkippedCount = result.SkippedCount,
            TotalCount = result.TotalCount,
            SourceFileName = sourceFileName?.Trim() ?? string.Empty,
            ImportMode = importMode,
            UpdatedAtUtc = updatedAtUtc,
            Message = BuildImportMessage(result, importMode)
        };
    }

    private static string BuildImportMessage(ProductCatalogImportResult result, string importMode)
    {
        var action = importMode switch
        {
            ProductCatalogImportModes.Overwrite => "增量导入",
            ProductCatalogImportModes.ClearAndImport => "覆盖导入",
            ProductCatalogImportModes.StockOut => "缺货导入",
            ProductCatalogImportModes.StockIn => "到货导入",
            _ => "增量导入"
        };

        return $"{action}完成：新增 {result.AddedCount}，更新 {result.UpdatedCount}，跳过 {result.SkippedCount}。";
    }

    private static string NormalizeImportMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? ProductCatalogImportModes.Incremental;
    }

    private static bool IsKnownImportMode(string importMode)
    {
        return importMode is ProductCatalogImportModes.Incremental
            or ProductCatalogImportModes.Overwrite
            or ProductCatalogImportModes.ClearAndImport
            or ProductCatalogImportModes.StockOut
            or ProductCatalogImportModes.StockIn;
    }

    private static string NormalizeGroupToken(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized == "-" ? string.Empty : normalized;
    }
}
