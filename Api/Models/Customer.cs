namespace Api.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public List<Address> Addresses { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
}

