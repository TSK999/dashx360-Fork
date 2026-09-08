using System.ComponentModel;

namespace XboxMetroLauncher.Utilities;

public sealed class PropertySubscription<T> : IDisposable where T : class, INotifyPropertyChanged
{
    private readonly PropertyChangedEventHandler handler;
    private T? current;
    public PropertySubscription(PropertyChangedEventHandler handler) => this.handler = handler;
    public bool Rebind(T value)
    {
        if (ReferenceEquals(current, value)) return false;
        Dispose();
        current = value;
        current.PropertyChanged += handler;
        return true;
    }
    public void Dispose()
    {
        if (current != null) current.PropertyChanged -= handler;
        current = null;
    }
}
