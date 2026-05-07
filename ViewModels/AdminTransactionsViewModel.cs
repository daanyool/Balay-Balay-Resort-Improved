namespace Balay_Balay_Resort.ViewModels;

public class TransactionItem
{
    public string TransactionId { get; set; } = "";
    public string BookingId { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public string Date { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Status { get; set; } = ""; // Completed, Pending
}

public class AdminTransactionsViewModel
{
    public int TotalTransactions { get; set; }
    public decimal TotalRevenue { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Pending { get; set; }
    public List<TransactionItem> Transactions { get; set; } = new();
    public string SearchQuery { get; set; } = "";
}
