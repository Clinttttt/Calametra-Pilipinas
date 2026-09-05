using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Sources;

public static class DataSourceErrors
{
    public static readonly Error NotFound =
        new(ErrorType.NotFound, "data_source.not_found", "The data source was not found.");

    public static readonly Error SlugRequired =
        new(ErrorType.Validation, "data_source.slug_required", "A data source requires a stable slug.");

    public static readonly Error AgencyRequired =
        new(ErrorType.Validation, "data_source.agency_required", "A data source requires a publishing agency.");

    public static readonly Error DatasetNameRequired =
        new(ErrorType.Validation, "data_source.dataset_name_required", "A data source requires a dataset name.");

    public static readonly Error AttributionRequired =
        new(
            ErrorType.Validation,
            "data_source.attribution_required",
            "A data source requires an attribution string. Calametra never displays unattributed data.");

    public static readonly Error RedistributionNotPermitted =
        new(
            ErrorType.Forbidden,
            "data_source.redistribution_not_permitted",
            "This source is not marked redistributable, so its geometry cannot be stored or served locally.");
}
