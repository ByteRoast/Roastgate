namespace Roastgate.Web.Components.Layouts;

public partial class MainLayout
{
    private bool _drawerOpen = true;

    private void ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
    }
}
