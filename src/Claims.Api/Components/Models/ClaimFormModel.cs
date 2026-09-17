using System.ComponentModel.DataAnnotations;
using Claims.Application;

namespace Claims.Api.Components.Models;

public static class DemoData
{
    public static readonly Guid ActiveMember = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ExpiredMember = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid InactiveMember = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid ActiveProvider = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid SecondActiveProvider = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid InactiveProvider = Guid.Parse("20000000-0000-0000-0000-000000000003");
}

public sealed class ClaimFormModel
{
    [Required]
    public Guid MemberId { get; set; } = DemoData.ActiveMember;

    [Required]
    public Guid ProviderId { get; set; } = DemoData.ActiveProvider;

    [Required, MinLength(1, ErrorMessage = "Add at least one service line.")]
    public List<ClaimLineFormModel> Lines { get; set; } = [new()];

    public SubmitClaimRequest ToRequest() => new()
    {
        MemberId = MemberId,
        ProviderId = ProviderId,
        Lines = Lines.Select(line => new ClaimLineRequest
        {
            ProcedureCode = line.ProcedureCode.Trim(),
            ServiceDate = line.ServiceDate,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        }).ToList()
    };
}

public sealed class ClaimLineFormModel
{
    [Required, RegularExpression(@"^[A-Za-z0-9-]{1,20}$",
        ErrorMessage = "Use 1–20 letters, numbers, or hyphens.")]
    public string ProcedureCode { get; set; } = "";

    [Required]
    public DateOnly? ServiceDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

    [Required, Range(1, 1000)]
    public int? Quantity { get; set; } = 1;

    [Required, Range(typeof(decimal), "0.01", "1000000")]
    public decimal? UnitPrice { get; set; }
}
