using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Security;

public sealed class PasskeyServiceTests
{
    private static readonly PasskeyRelyingParty Site = new("localhost", "http://localhost:8181");

    private sealed record Setup(TestDatabase Database, IPasskeyService Service, SoftwareAuthenticator Authenticator)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return Database.DisposeAsync();
        }
    }

    private static async Task<Setup> CreateAsync(bool registered = true)
    {
        TestDatabase database = await TestDatabase.CreateAsync();
        
        await database.Get<IAdminAccountStore>()
            .CreateAsync("admin", database.Get<PasswordHasher>().Hash("Correct-Horse-9"));
        
        IPasskeyService service = database.Get<IPasskeyService>();
        SoftwareAuthenticator authenticator = new();
        
        if (registered)
        {
            Assert.NotNull(await RegisterAsync(service, authenticator));
        }

        return new Setup(database, service, authenticator);
    }

    private static async Task<PasskeyInfo?> RegisterAsync(IPasskeyService service, SoftwareAuthenticator authenticator,
        string name = "laptop")
    {
        PasskeyChallenge challenge = await service.BeginRegistrationAsync(Site, "admin");
        
        return await service.CompleteRegistrationAsync(Site, challenge.CeremonyId,
            authenticator.Register(challenge.OptionsJson, Site.Id, Site.Origin), name);
    }

    // ---- Registering -----------------------------------------------------------------

    [Fact]
    public async Task A_registered_passkey_is_listed_with_its_name()
    {
        await using Setup setup = await CreateAsync(registered: false);
        
        Assert.False(await setup.Service.AnyAsync());

        PasskeyInfo? passkey = await RegisterAsync(setup.Service, setup.Authenticator, "  work laptop ");

        Assert.NotNull(passkey);
        Assert.Equal("work laptop", passkey.Name);
        Assert.True(await setup.Service.AnyAsync());
        Assert.Single(await setup.Service.ListAsync());
    }

    [Fact]
    public async Task The_same_authenticator_cannot_be_registered_twice()
    {
        await using Setup setup = await CreateAsync();

        Assert.Null(await RegisterAsync(setup.Service, setup.Authenticator));
        Assert.Single(await setup.Service.ListAsync());
    }

    [Fact]
    public async Task A_registration_made_for_another_address_is_refused()
    {
        await using Setup setup = await CreateAsync(registered: false);
        
        PasskeyChallenge challenge = await setup.Service.BeginRegistrationAsync(Site, "admin");

        string forged = setup.Authenticator.Register(challenge.OptionsJson, Site.Id, "http://evil.example");

        Assert.Null(await setup.Service.CompleteRegistrationAsync(Site, challenge.CeremonyId, forged, "x"));
        Assert.False(await setup.Service.AnyAsync());
    }

    [Fact]
    public async Task A_registration_ceremony_works_once()
    {
        await using Setup setup = await CreateAsync(registered: false);
        
        PasskeyChallenge challenge = await setup.Service.BeginRegistrationAsync(Site, "admin");
        
        string response = setup.Authenticator.Register(challenge.OptionsJson, Site.Id, Site.Origin);

        Assert.NotNull(await setup.Service.CompleteRegistrationAsync(Site, challenge.CeremonyId, response, "a"));
        Assert.Null(await setup.Service.CompleteRegistrationAsync(Site, challenge.CeremonyId, response, "b"));
    }

    [Fact]
    public async Task Garbage_is_refused_without_an_exception()
    {
        await using Setup setup = await CreateAsync(registered: false);
        
        PasskeyChallenge challenge = await setup.Service.BeginRegistrationAsync(Site, "admin");

        Assert.Null(await setup.Service.CompleteRegistrationAsync(Site, challenge.CeremonyId, "not json", "x"));
        Assert.Null(await setup.Service.CompleteRegistrationAsync(Site, "unknown", "{}", "x"));
    }

    // ---- Signing in ------------------------------------------------------------------

    [Fact]
    public async Task A_passkey_signs_in_without_a_password()
    {
        await using Setup setup = await CreateAsync();
        
        PasskeyChallenge challenge = await setup.Service.BeginSignInAsync(Site, ticket: null);

        string? username = await setup.Service.CompleteSignInAsync(
            Site, challenge.CeremonyId, setup.Authenticator.Assert(challenge.OptionsJson, Site.Id, Site.Origin),
            ticket: null);

        Assert.Equal("admin", username);
        
        PasskeyInfo used = Assert.Single(await setup.Service.ListAsync());
     
        Assert.NotNull(used.LastUsedAt);
    }

    [Fact]
    public async Task An_answer_cannot_be_replayed()
    {
        await using Setup setup = await CreateAsync();
        
        PasskeyChallenge challenge = await setup.Service.BeginSignInAsync(Site, ticket: null);
        
        string answer = setup.Authenticator.Assert(challenge.OptionsJson, Site.Id, Site.Origin);

        Assert.Equal("admin", await setup.Service.CompleteSignInAsync(Site, challenge.CeremonyId, answer, null));
        Assert.Null(await setup.Service.CompleteSignInAsync(Site, challenge.CeremonyId, answer, null));
    }

    [Fact]
    public async Task An_answer_signed_for_another_address_is_refused()
    {
        await using Setup setup = await CreateAsync();
        
        PasskeyChallenge challenge = await setup.Service.BeginSignInAsync(Site, ticket: null);

        string phishing = setup.Authenticator.Assert(challenge.OptionsJson, Site.Id, "http://evil.example");

        Assert.Null(await setup.Service.CompleteSignInAsync(Site, challenge.CeremonyId, phishing, null));
    }

    [Fact]
    public async Task An_answer_without_user_verification_is_refused()
    {
        await using Setup setup = await CreateAsync();
        
        PasskeyChallenge challenge = await setup.Service.BeginSignInAsync(Site, ticket: null);
        
        setup.Authenticator.UserVerified = false;

        string answer = setup.Authenticator.Assert(challenge.OptionsJson, Site.Id, Site.Origin);

        Assert.Null(await setup.Service.CompleteSignInAsync(Site, challenge.CeremonyId, answer, null));
    }

    [Fact]
    public async Task An_unknown_passkey_is_refused()
    {
        await using Setup setup = await CreateAsync();
        
        PasskeyChallenge challenge = await setup.Service.BeginSignInAsync(Site, ticket: null);

        string answer = new SoftwareAuthenticator().Assert(challenge.OptionsJson, Site.Id, Site.Origin);

        Assert.Null(await setup.Service.CompleteSignInAsync(Site, challenge.CeremonyId, answer, null));
    }

    [Fact]
    public async Task A_passkey_with_a_stale_counter_is_refused()
    {
        await using Setup setup = await CreateAsync();
        
        PasskeyChallenge first = await setup.Service.BeginSignInAsync(Site, ticket: null);
        
        Assert.Equal("admin",
            await setup.Service.CompleteSignInAsync(Site, first.CeremonyId,
                setup.Authenticator.Assert(first.OptionsJson, Site.Id, Site.Origin), null));

        // A cloned authenticator that starts counting again from the beginning.
        setup.Authenticator.Counter = 0;
        
        PasskeyChallenge second = await setup.Service.BeginSignInAsync(Site, ticket: null);
        
        string answer = setup.Authenticator.Assert(second.OptionsJson, Site.Id, Site.Origin);

        Assert.Null(await setup.Service.CompleteSignInAsync(Site, second.CeremonyId, answer, null));
    }

    // ---- As a second step ------------------------------------------------------------

    [Fact]
    public async Task The_second_step_lists_the_registered_passkeys_and_the_password_less_one_does_not()
    {
        await using Setup setup = await CreateAsync();

        PasskeyChallenge secondStep = await setup.Service.BeginSignInAsync(Site, ticket: "t");
        PasskeyChallenge passwordless = await setup.Service.BeginSignInAsync(Site, ticket: null);

        Assert.Equal(1,
            JsonDocument.Parse(secondStep.OptionsJson).RootElement.GetProperty("allowCredentials").GetArrayLength());
        
        Assert.True(!JsonDocument.Parse(passwordless.OptionsJson).RootElement
                        .TryGetProperty("allowCredentials", out JsonElement allowed)
                    || allowed.GetArrayLength() == 0);
    }

    [Fact]
    public async Task A_ceremony_belongs_to_the_ticket_it_was_started_for()
    {
        await using Setup setup = await CreateAsync();

        PasskeyChallenge forTicket = await setup.Service.BeginSignInAsync(Site, ticket: "ticket-a");
        
        string answer = setup.Authenticator.Assert(forTicket.OptionsJson, Site.Id, Site.Origin);
        
        Assert.Null(await setup.Service.CompleteSignInAsync(Site, forTicket.CeremonyId, answer, "ticket-b"));

        PasskeyChallenge withoutTicket = await setup.Service.BeginSignInAsync(Site, ticket: null);
        
        string other = setup.Authenticator.Assert(withoutTicket.OptionsJson, Site.Id, Site.Origin);
        
        Assert.Null(await setup.Service.CompleteSignInAsync(Site, withoutTicket.CeremonyId, other, "ticket-a"));
    }

    // ---- Removing --------------------------------------------------------------------

    [Fact]
    public async Task Removing_a_passkey_ends_the_open_sessions_and_the_passkey_stops_working()
    {
        await using Setup setup = await CreateAsync();
        
        IAdminAccountStore accounts = setup.Database.Get<IAdminAccountStore>();
        
        string? stampBefore = (await accounts.GetAsync())!.SecurityStamp;
        
        int id = (await setup.Service.ListAsync())[0].Id;

        Assert.True(await setup.Service.RemoveAsync(id));
        Assert.NotEqual(stampBefore, (await accounts.GetAsync())!.SecurityStamp);
        Assert.False(await setup.Service.AnyAsync());
        
        PasskeyChallenge challenge = await setup.Service.BeginSignInAsync(Site, ticket: null);
        
        Assert.Null(await setup.Service.CompleteSignInAsync(Site, challenge.CeremonyId,
            setup.Authenticator.Assert(challenge.OptionsJson, Site.Id, Site.Origin), null));
        
        Assert.False(await setup.Service.RemoveAsync(id));
    }

    [Fact]
    public void Ceremonies_expire_and_the_table_stays_bounded()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        PasskeyCeremonies ceremonies = new(time);

        string id = ceremonies.Issue(PasskeyCeremonyKind.SignIn, "{}", null);
        
        time.Advance(PasskeyCeremonies.Lifetime + TimeSpan.FromSeconds(1));
        
        Assert.Null(ceremonies.Take(id, PasskeyCeremonyKind.SignIn));

        string kindMismatch = ceremonies.Issue(PasskeyCeremonyKind.SignIn, "{}", null);
        
        Assert.Null(ceremonies.Take(kindMismatch, PasskeyCeremonyKind.Registration));
    }
}
