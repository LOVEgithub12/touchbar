using WindowsTouchBar.Events;

namespace WindowsTouchBar;

internal interface ITouchInput : IDisposable
{
    event Action<TouchEventArgs>? Event;
    event Action<Exception>? Failed;
}
