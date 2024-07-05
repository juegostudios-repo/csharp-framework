using System.Timers;
using Timer = System.Timers.Timer;

namespace JuegoFramework.Helpers
{
    public class SingleExecutionAsyncTimer : IDisposable
    {
        private readonly Func<Task> _asyncAction;
        private readonly Timer _timer;

        public SingleExecutionAsyncTimer(Func<Task> asyncAction, TimeSpan interval)
        {
            _asyncAction = asyncAction ?? throw new ArgumentNullException(nameof(asyncAction));
            _timer = new Timer(interval.TotalMilliseconds) { AutoReset = false };
            _timer.Elapsed += TimerElapsed;
            Start();
        }

        private async void TimerElapsed(object? sender, ElapsedEventArgs e)
        {
            await _asyncAction();
            _timer.Start();
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public bool Enabled
        {
            get => _timer.Enabled;
            set => _timer.Enabled = value;
        }

        public void Dispose()
        {
            _timer.Elapsed -= TimerElapsed;
            _timer.Dispose();
        }
    }
}
