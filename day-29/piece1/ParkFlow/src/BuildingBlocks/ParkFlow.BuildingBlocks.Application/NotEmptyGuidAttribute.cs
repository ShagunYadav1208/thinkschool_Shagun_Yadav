using System.ComponentModel.DataAnnotations;

namespace ParkFlow.BuildingBlocks.Application;

/// <summary>
/// <c>[Required]</c> alone never rejects <see cref="Guid.Empty"/> — a non-nullable struct is
/// never "missing" as far as that attribute is concerned — so a request DTO that only wants a
/// real id needs this instead. Used on every id field the API accepts directly from a client
/// (day-27 hardening: request DTOs previously had no validation at all).
/// </summary>
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public NotEmptyGuidAttribute() : base("The {0} field must not be an empty GUID.")
    {
    }

    public override bool IsValid(object? value) => value is Guid guid && guid != Guid.Empty;
}
