using Microsoft.Extensions.Time.Testing;
using Roastgate.Web.Authentication;

namespace Roastgate.Tests.Web;

public sealed class SignInTicketServiceTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void A_ticket_can_be_redeemed_once()
    {
        SignInTicketService tickets = new(_time);
        string ticket = tickets.Issue("admin");

        Assert.Equal("admin", tickets.Redeem(ticket));
        Assert.Null(tickets.Redeem(ticket));
    }

    [Fact]
    public void A_ticket_expires()
    {
        SignInTicketService tickets = new(_time);
        string ticket = tickets.Issue("admin");

        _time.Advance(SignInTicketService.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(tickets.Redeem(ticket));
    }

    [Fact]
    public void Unknown_tickets_are_rejected()
    {
        Assert.Null(new SignInTicketService(_time).Redeem("deadbeef"));
    }

    [Fact]
    public void Tickets_are_long_unique_and_lowercase_hex()
    {
        SignInTicketService tickets = new(_time);

        string first = tickets.Issue("admin");
        string second = tickets.Issue("admin");

        Assert.NotEqual(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }
}
