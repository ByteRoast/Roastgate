using MudBlazor;
using Roastgate.Core.Nginx;

namespace Roastgate.Web.Services;

/// <summary>Shows the outcome of an action as a toast when the page has nothing left to show about it.</summary>
public static class SnackbarExtensions
{
    extension(ISnackbar snackbar)
    {
        /// <summary>
        /// Toasts a clean success and returns null. A failure or a warning is returned unchanged, so that the page
        /// keeps showing it inline with the nginx output to read.
        /// </summary>
        public ConfigResult? ToastSuccess(ConfigResult result, string? subject = null)
        {
            if (!result.Succeeded || result.Warning)
            {
                return result;
            }

            snackbar.Add(Describe(result, subject), Severity.Success);

            return null;
        }
        
        /// <summary>
        /// Toasts a success, warning included, for an action that leaves the page and so loses its inline alert.
        /// </summary>
        public void ToastBeforeLeaving(ConfigResult result, string subject)
        {
            if (result.Warning)
            {
                snackbar.Add(
                    Describe(result, subject),
                    Severity.Warning,
                    options => options.RequireInteraction = true
                );

                return;
            }

            snackbar.Add(Describe(result, subject), Severity.Success);
        }
    }

    private static string Describe(ConfigResult result, string? subject)
    {
        return subject is null
            ? result.Message
            : subject + ": " + result.Message;
    }
}
