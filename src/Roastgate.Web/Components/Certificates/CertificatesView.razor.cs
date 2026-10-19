using Microsoft.AspNetCore.Components;
using MudBlazor;
using Roastgate.Core.Certificates;
using Roastgate.Core.Settings;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Certificates;

public partial class CertificatesView
{
    [Inject] private ICertificateService Certificates { get; set; } = null!;
    [Inject] private ICertificateCatalog Catalog { get; set; } = null!;
    [Inject] private ICertificateRenewalJob RenewalJob { get; set; } = null!;
    [Inject] private AppSettingsService SettingsService { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private CertificateListing? _listing;
    private RenewalStatus? _status;
    private CertbotMode _mode = CertbotMode.Embedded;
    private string _domains = string.Empty;
    private string _certName = string.Empty;
    private string _email = string.Empty;
    private bool _staging;
    private bool _isBusy;
    private string? _output;
    private string? _outputMessage;
    private bool _outputFailed;

    protected override async Task OnInitializedAsync()
    {
        AppSettings settings = await SettingsService.GetAsync();
        _mode = settings.CertbotMode;
        _email = settings.AcmeEmail ?? string.Empty;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _listing = await Catalog.ListAsync();
        _status = await RenewalJob.GetStatusAsync();
    }

    private async Task IssueAsync(bool dryRun)
    {
        string[] domains = _domains.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        await RunAsync(async () => Show(await Certificates.IssueAsync(
            new IssueRequest(domains, _certName, _email, _staging, dryRun),
            await User.GetNameAsync())));
    }

    private async Task RenewAsync(string? name, bool force)
    {
        await RunAsync(async () => Show(await Certificates.RenewAsync(name, force, await User.GetNameAsync())));
    }

    private async Task DeleteAsync(CertificateInfo certificate)
    {
        bool? confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete " + certificate.Name + "?",
            "certbot deletes the certificate and its private key. Let's Encrypt can issue it again later.",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        await RunAsync(async () => Show(await Certificates.DeleteAsync(certificate.Name, await User.GetNameAsync())));
    }

    private void Show(CertificateOperation operation)
    {
        _outputFailed = !operation.Succeeded;
        _outputMessage = operation.Message;
        _output = operation.Output ?? string.Empty;
    }

    private async Task RunAsync(Func<Task> action)
    {
        _isBusy = true;
        _output = null;
        try
        {
            await action();
            await LoadAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static Color ExpiryColor(CertificateInfo certificate)
    {
        int days = certificate.DaysLeft(DateTimeOffset.UtcNow);
        return days < 14 ? Color.Error : days < 30 ? Color.Warning : Color.Default;
    }

    private static string ExpiryText(CertificateInfo certificate)
    {
        int days = certificate.DaysLeft(DateTimeOffset.UtcNow);
        return days < 0 ? "expired " + (-days) + " days ago" : "in " + days + " days";
    }
}
