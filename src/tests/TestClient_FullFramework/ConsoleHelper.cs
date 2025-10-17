using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace TestClient_FullFramework
{
    public class ConsoleHelper
    {

        private static readonly Subject<ConsoleKeyInfo> PressedKeySubject = new Subject<ConsoleKeyInfo>();
        public static IObservable<ConsoleKeyInfo> PressedKey { get; } = PressedKeySubject.AsObservable();

        
        public static async Task RegisterNewLineHandlerAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!Environment.UserInteractive || Console.IsInputRedirected)
                return;

            await Task.Run(() =>
            {
                var t = true;
                while (t)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        t = false;
                    }
                    var key = Console.ReadKey(true);
                    PressedKeySubject.OnNext(key);
                    switch (key.Key)
                    {
                        case ConsoleKey.Enter:
                            Console.WriteLine();
                            break;
                        default:
                            break;
                    }
                    if (cancellationToken.IsCancellationRequested)
                    {
                        t = false;
                    }
                }
            }, cancellationToken);
        }
    }
}
