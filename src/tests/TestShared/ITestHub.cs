using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TestShared
{
    public interface ITestHub {
        void WriteLine(string line);
        Task<Guid> StringToGuid(Guid guid, string test = null);

        IObservable<string> ObservableCounter(int count, int delay);

        ChannelReader<string> ChannelCounter(int count, int delay, CancellationToken cancellationToken);

        void Ping();

        string GetDate2(DateTime date);

    }
}
