using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Hazards;

public static class HazardLayerErrors
{
    public static readonly Error NotFound =
        new(ErrorType.NotFound, "hazard_layer.not_found", "The hazard layer was not found.");

    public static readonly Error UnknownType =
        new(ErrorType.Validation, "hazard_layer.unknown_type", "A hazard layer requires a known hazard type.");

    public static readonly Error DisplayNameRequired =
        new(ErrorType.Validation, "hazard_layer.display_name_required", "A hazard layer requires a display name.");

    public static readonly Error WmsConfigurationRequired =
        new(
            ErrorType.Validation,
            "hazard_layer.wms_configuration_required",
            "A remotely delivered layer requires a WMS endpoint and layer name.");

    public static readonly Error LocalStorageNotPermitted =
        new(
            ErrorType.Forbidden,
            "hazard_layer.local_storage_not_permitted",
            "This layer's source is not marked redistributable, so it cannot be delivered as local vector data.");
}
