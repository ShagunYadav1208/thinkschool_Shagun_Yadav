namespace DapperVsEfApi.Domain;

// The normalized WRITE model. One row per author, one row per quote,
// related by a real foreign key - shaped for correctness and validation,
// not for any particular screen.
public class Author
{
    public int AuthorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
}
