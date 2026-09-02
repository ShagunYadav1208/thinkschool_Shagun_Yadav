using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Models;

public class CreateQuoteRequest
{
    [Required]
    [StringLength(100)]
    public string Author { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Text { get; set; } = string.Empty;
}