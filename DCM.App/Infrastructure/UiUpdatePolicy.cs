using System.Windows.Threading;

namespace DCM.App.Infrastructure;

internal static class UiUpdatePolicy
{
    public static readonly DispatcherPriority StatusPriority = DispatcherPriority.Normal;
    public static readonly DispatcherPriority ButtonPriority = DispatcherPriority.Normal;
    public static readonly DispatcherPriority ProgressPriority = DispatcherPriority.Background;
    public static readonly DispatcherPriority LogPriority = DispatcherPriority.Background;
    public static readonly DispatcherPriority LayoutPriority = DispatcherPriority.Loaded;
}
